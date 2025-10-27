using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour.HookGen;

namespace Assembly_CSharp.TasInfo.mm.Source.Utils {
    public static class HookUtils {

        public static void HookEnter<TClass, TDelegate>(string methodName, TDelegate callback) where TDelegate: Delegate {
            var parameters = GetParameters(callback);
            var method = typeof(TClass).GetMethod(
                methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                parameters,
                null);
            if (method == null) {
                Console.WriteLine($"Could not find method {methodName} on class {typeof(TClass).Name} with parameter count {parameters.Length}");
                return;
            }
            HookEnter(method, callback);
        }

        public static void HookExit<TClass, TDelegate>(string methodName, TDelegate callback) where TDelegate: Delegate {
            var parameters = GetParameters(callback);
            var method = typeof(TClass).GetMethod(
                methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                parameters,
                null);
            if (method == null) {
                Console.WriteLine($"Could not find method {methodName} on class {typeof(TClass).Name} with parameter count {parameters.Length}");
                return;
            }
            HookExit(method, callback);
        }

        public static void HookReplace<TClass, TDelegate>(string methodName, TDelegate callback) where TDelegate: Delegate {
            var parameters = GetParameters(callback);
            var method = typeof(TClass).GetMethod(
                methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                parameters,
                null);
            if (method == null) {
                Console.WriteLine($"Could not find method {methodName} on class {typeof(TClass).Name} with parameter count {parameters.Length}");
                return;
            }
            HookReplace(method, callback);
        }

        private static Type[] GetParameters(Delegate callback) {
            var parameters = callback.Method.GetParameters();

            //For instance methods, we skip the first parameter for the purpose of binding
            return parameters.Skip(1).Select(p => p.ParameterType).ToArray();
        }

        private static void HookEnter<TDelegate>(MethodInfo method, TDelegate callback) where TDelegate: Delegate {
            void Hook(ILContext il) {
                var c = new ILCursor(il);

                for (int i = 0; i < callback.Method.GetParameters().Length; i++) {
                    c.Emit(OpCodes.Ldarg, (ushort)i);
                }
                c.EmitDelegate(callback);
            }

            HookEndpointManager.Modify(method, (Action<ILContext>)Hook);
        }

        private static void HookExit<TDelegate>(MethodInfo method, TDelegate callback) where TDelegate: Delegate {
            void Hook(ILContext il) {
                var c = new ILCursor(il);

                //Exit can occur from multiple places (anywhere there is a Ret instruction)
                //As such, we'll go to every Ret instruction and insert a branch to our new return handler
                //where we can inject the callback
                var label = c.DefineLabel();

                while (c.TryGotoNext(i => i.MatchRet())) {
                    //Replace the Ret with a Nop and then branch to our handler area
                    //This preserves any existing branch references to where this Ret occurred
                    c.Next.OpCode = OpCodes.Nop;
                    c.Emit(OpCodes.Br, label);
                }

                //Now we can put our new exit handler at the end of the method
                c.Goto(c.Instrs.Count - 1, MoveType.After);
                c.MarkLabel(label);
                for (int i = 0; i < callback.Method.GetParameters().Length; i++) {
                    c.Emit(OpCodes.Ldarg, (ushort)i);
                }
                c.EmitDelegate(callback);
                c.Emit(OpCodes.Ret);
            }

            HookEndpointManager.Modify(method, (Action<ILContext>)Hook);
        }

        private static void HookReplace<TDelegate>(MethodInfo method, TDelegate callback) where TDelegate: Delegate {
            void Hook(ILContext il) {
                var c = new ILCursor(il);

                for (int i = 0; i < callback.Method.GetParameters().Length; i++) {
                    c.Emit(OpCodes.Ldarg, (ushort)i);
                }
                c.EmitDelegate(callback);
                c.Emit(OpCodes.Ret);
            }

            HookEndpointManager.Modify(method, (Action<ILContext>)Hook);
        }
    }
}
