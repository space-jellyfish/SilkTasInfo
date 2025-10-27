// ReSharper disable All

using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using Assembly_CSharp.TasInfo.mm.Source;
using Mono.Cecil;
using MonoMod;
using UnityEngine;

// for P/Invoke
using System.Runtime.InteropServices;

#pragma warning disable CS0649, CS0414

class patch_GameManager : GameManager {
    public static readonly long TasPageMarker = 0x1234567812345678;
    private static IntPtr tasPageAddr = IntPtr.Zero;
    public static readonly long TasInfoMark = 1234567890123456789;
    public static string TasInfo;
    public static int InfoFlags;

    //[MonoModIgnore]
    //public extern void orig_LeftScene(bool doAdditiveLoad);

    //[NoInlining]
    //public new void LeftScene(bool doAdditiveLoad) {
    //    //RandomInjection.OnLeftScene();
    //    orig_LeftScene(doAdditiveLoad);
    //}
    // Define constants for mmap flags; see mmap(2) for more complete explanations, but in short:
    // We want the mapped memory to be readable and writeable
    // We ask for the memory to NOT be backed by a file, it's purely in memory (MAP_ANONYMOUS)
    // We ask for the memory to be at this precise addr (MAP_FIXED) - this can get messy if we
    // choose to map over memory that the rest of the process is actually using, but I tried
    // to choose the map carefully after restarting HK about a dozen times to see what regions
    // ended up mapped. If the TAS starts crashing or acting weirdly consider dropping to
    // 0x6f0000001000 or something. IT does need to be page-aligned, typically a multiple of 4096
    // The call needs to also have one of MAP_SHARED or MAP_PRIVATE, but what those do isn't
    // too important for our purpose here - they basically say can another process modify this
    // map if they also map it. Shouldn't make a difference based on how libTAS is connecting the
    // lua to the game memory.
    private const int PROT_READ = 0x1;
    private const int PROT_WRITE = 0x2;
    private const int MAP_PRIVATE = 0x2;
    private const int MAP_FIXED = 0x10;
    private const int MAP_ANONYMOUS = 0x20;

    // Declare the mmap function from libc using P/Invoke
    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr mmap(UIntPtr addr, UIntPtr length, int prot, int flags, int fd, IntPtr offset);

    [DllImport("libc", SetLastError = true)]
    private static extern int getpagesize();

    public static void MapMemory() {
        using (StreamWriter writer = new StreamWriter(@"/tmp/modLog.txt", true)) {
            writer.Write("In MapMemory");
            if (tasPageAddr != IntPtr.Zero) {
                writer.Write("Already mapped at " + tasPageAddr.ToString());
                return;
            }
            writer.Write("Okay getting page size. It is...");
            // Map a file into memory at a specific address
            int pageSize = getpagesize();
            writer.Write(pageSize.ToString());
            writer.Flush();
        
            // We want to get the map exactly here, if possible. The baseAddr is chosen
            // to already be aligned to a page boundary on the assumption the page is
            // the typical size of 4096. We also have to be careful to pick an addr that
            // we don't think is already in use / will be explicitly desired by some
            // logic in HK. This is just a best guess; if this doesn't seem to be working
            // then we probably want to try a different addr. Ultimately we might even want
            // to make this a config item.
            long baseAddr = 0x7f0000001000;

            UIntPtr addr = (UIntPtr)(baseAddr & ~(pageSize - 1)); // align to page boundary
            int prot = PROT_READ | PROT_WRITE;
            int flags = MAP_ANONYMOUS | MAP_FIXED | MAP_PRIVATE;
            int fd = -1;
            IntPtr offset = IntPtr.Zero;

            IntPtr mappedAddress = mmap(addr, (UIntPtr)pageSize, prot, flags, fd, offset);

            if (mappedAddress != IntPtr.Zero && mappedAddress != (IntPtr)(-1)) {
                writer.Write("Got it mapped???");
                writer.Write(mappedAddress.ToString());
                tasPageAddr = mappedAddress;
                // zero out the page
                Marshal.Copy(new byte[pageSize], 0, tasPageAddr, pageSize);
                // Don't write the page marker yet! Let it be written at the same time that we write the addr of the TasInfo string into it so we're confident about the validity of the addr.
            }
        }
    }

    // Meant to be called by TasInfo::onPreRender() when it's done writing
    public static void WriteTasInfoAddr() {
        if (tasPageAddr == IntPtr.Zero) {
            MapMemory();
            if (tasPageAddr == IntPtr.Zero) {
                return;
            }
        }

        Marshal.WriteInt64(tasPageAddr, TasPageMarker);

        unsafe {
            fixed (long* pTasInfoMark = &patch_GameManager.TasInfoMark) {
                IntPtr nextAddr = new IntPtr(tasPageAddr.ToInt64() + sizeof(long));
                Marshal.WriteIntPtr(nextAddr, (IntPtr)pTasInfoMark);
            }
        }

        return;
    }
}



[MonoModCustomMethodAttribute("NoInlining")]
public class NoInlining : Attribute {
}

namespace MonoMod {
    static partial class MonoModRules {
        // ReSharper disable once UnusedParameter.Global
        public static void NoInlining(MethodDefinition method, CustomAttribute attrib) => method.NoInlining = true;
    }
}