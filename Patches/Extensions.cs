using System;
using System.Collections.Generic;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod;
using MonoMod.Cil;
using MonoMod.Utils;
using UnityEngine;

static class patch_Extensions {
    [ThreadStatic]
    public static string NamePrefix = "";

    [MonoModIgnore]
    [PatchRandom]
    public extern static T GetAndRemoveRandomElement<T>(this List<T> list);

    [MonoModIgnore]
    [PatchRandom]
    public extern static T GetRandomElement<T>(this List<T> list);

    [MonoModIgnore]
    [PatchRandom]
    public extern static T GetRandomElement<T>(this T[] array);

    [MonoModIgnore]
    [PatchRandom]
    public extern static void Shuffle<T>(IList<T> ts);

    [MonoModIgnore]
    [PatchRandom]
    public extern static Vector2 RandomInRange(Vector2 range);

    [MonoModIgnore]
    [PatchRandom]
    public extern static Vector3 RandomInRange(Vector3 range);

    public static Func<float, float, string, float> ExtOnRangeFloat;
    public static Func<int, int, string, int> ExtOnRangeInt;
    public static Func<string, Vector2> ExtOnInsideUnitCircle;
    public static Func<string, Vector3> ExtOnOnUnitSphere;
    
    public static float OnRangeFloat(float min, float max, string name) {
        if (ExtOnRangeFloat == null)
            return min;

        return ExtOnRangeFloat(min, max, name);
    }

    public static int OnRangeInt(int min, int max, string name) {
        if (ExtOnRangeInt == null)
            return min;

        return ExtOnRangeInt(min, max, name);
    }

    public static Vector2 OnInsideUnitCircle(string name) {
        if (ExtOnInsideUnitCircle == null)
            return Vector2.zero;

        return ExtOnInsideUnitCircle(name);
    }

    public static Vector3 OnOnUnitSphere(string name) {
        if (ExtOnOnUnitSphere == null)
            return Vector3.zero;

        return ExtOnOnUnitSphere(name);
    }
}

namespace MonoMod {
    [MonoModCustomAttribute(nameof(MonoModRules.PatchRandom))]
    internal class PatchRandomAttribute : Attribute { }

    static partial class MonoModRules {
        private static MethodInfo _rangeFloat;
        private static MethodInfo _rangeInt;
        private static MethodInfo _insideUnitCircle;
        private static MethodInfo _onUnitSphere;

        private static MethodReference _onRangeFloatRef;
        private static MethodReference _onRangeIntRef;
        private static MethodReference _onInsideUnitCircleRef;
        private static MethodReference _onOnUnitSphereRef;
        private static FieldReference _namePrefixRef;
        private static MethodInfo _stringConcat;

        public static void PatchRandom(MethodDefinition method, CustomAttribute attrib) {
            using (var context = new ILContext(method)) {
                InjectOnRangePatcher(method, context);
            }
        }

        private static MethodReference GetOnRangeFloatRef(ModuleDefinition module) {
            if (_onRangeFloatRef != null)
                return _onRangeFloatRef;

            var extensionsType = GetExtensionsType(module);
            _onRangeFloatRef = new MethodReference("OnRangeFloat", module.TypeSystem.Single, extensionsType) {
                HasThis = false,
                CallingConvention = MethodCallingConvention.Default
            };
            _onRangeFloatRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.Single));
            _onRangeFloatRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.Single));
            _onRangeFloatRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            return _onRangeFloatRef;
        }

        private static MethodReference GetOnRangeIntRef(ModuleDefinition module) {
            if (_onRangeIntRef != null)
                return _onRangeIntRef;

            var extensionsType = GetExtensionsType(module);
            _onRangeIntRef = new MethodReference("OnRangeInt", module.TypeSystem.Int32, extensionsType) {
                HasThis = false,
                CallingConvention = MethodCallingConvention.Default
            };
            _onRangeIntRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
            _onRangeIntRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
            _onRangeIntRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            return _onRangeIntRef;
        }

        private static MethodReference GetOnInsideUnitCircleRef(ModuleDefinition module) {
            if (_onInsideUnitCircleRef != null)
                return _onInsideUnitCircleRef;

            var extensionsType = GetExtensionsType(module);
            var vector2Type = module.ImportReference(typeof(Vector2));
            _onInsideUnitCircleRef = new MethodReference("OnInsideUnitCircle", vector2Type, extensionsType) {
                HasThis = false,
                CallingConvention = MethodCallingConvention.Default
            };
            _onInsideUnitCircleRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            return _onInsideUnitCircleRef;
        }

        private static MethodReference GetOnOnUnitSphereRef(ModuleDefinition module) {
            if (_onOnUnitSphereRef != null)
                return _onOnUnitSphereRef;

            var extensionsType = GetExtensionsType(module);
            var vector3Type = module.ImportReference(typeof(Vector3));
            _onOnUnitSphereRef = new MethodReference("OnOnUnitSphere", vector3Type, extensionsType) {
                HasThis = false,
                CallingConvention = MethodCallingConvention.Default
            };
            _onOnUnitSphereRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            return _onOnUnitSphereRef;
        }

        private static TypeDefinition GetExtensionsType(ModuleDefinition module) {
            foreach (var type in module.Types) {
                if (type.Name == "Extensions") {
                    return type;
                }
            }
            throw new InvalidOperationException("Failed to find Extensions type in module");
        }

        private static FieldReference GetNamePrefixRef(ModuleDefinition module) {
            if (_namePrefixRef != null)
                return _namePrefixRef;

            var extensionsType = GetExtensionsType(module);
            _namePrefixRef = new FieldReference("NamePrefix", module.TypeSystem.String, extensionsType);
            return _namePrefixRef;
        }

        private static void InjectOnRangePatcher(MethodDefinition method, ILContext il) {
            // Get the module being modified
            ModuleDefinition module = method.Module;

            if (_rangeFloat == null) {
                var ueRandom = typeof(UnityEngine.Random);
                _rangeFloat = ueRandom.GetMethod("Range", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(float), typeof(float) }, null);
                _rangeInt = ueRandom.GetMethod("Range", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(int), typeof(int) }, null);
                _insideUnitCircle = ueRandom.GetProperty("insideUnitCircle", BindingFlags.Static | BindingFlags.Public)?.GetGetMethod();
                _onUnitSphere = ueRandom.GetProperty("onUnitSphere", BindingFlags.Static | BindingFlags.Public)?.GetGetMethod();
                _stringConcat = typeof(string).GetMethod("Concat", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string), typeof(string) }, null);
            }

            // Get method references that will be valid after patching
            var onRangeFloatRef = GetOnRangeFloatRef(module);
            var onRangeIntRef = GetOnRangeIntRef(module);
            var onInsideUnitCircleRef = GetOnInsideUnitCircleRef(module);
            var onOnUnitSphereRef = GetOnOnUnitSphereRef(module);
            var namePrefixRef = GetNamePrefixRef(module);
            var stringConcatRef = module.ImportReference(_stringConcat);

            var name = TrimNamespace(il.Method.Name);
            var c = new ILCursor(il);
            while (c.TryGotoNext(MoveType.Before, x => x.MatchCall(_rangeFloat))) {
                c.Remove();
                c.Emit(OpCodes.Ldsfld, namePrefixRef);
                c.Emit(OpCodes.Ldstr, name);
                c.Emit(OpCodes.Call, stringConcatRef);
                c.Emit(OpCodes.Call, onRangeFloatRef);
            }

            c.Goto(0);
            while (c.TryGotoNext(MoveType.Before, x => x.MatchCall(_rangeInt))) {
                c.Remove();
                c.Emit(OpCodes.Ldsfld, namePrefixRef);
                c.Emit(OpCodes.Ldstr, name);
                c.Emit(OpCodes.Call, stringConcatRef);
                c.Emit(OpCodes.Call, onRangeIntRef);
            }

            c.Goto(0);
            while (c.TryGotoNext(MoveType.Before, x => x.MatchCall(_insideUnitCircle))) {
                c.Remove();
                c.Emit(OpCodes.Ldsfld, namePrefixRef);
                c.Emit(OpCodes.Ldstr, name);
                c.Emit(OpCodes.Call, stringConcatRef);
                c.Emit(OpCodes.Call, onInsideUnitCircleRef);
            }

            c.Goto(0);
            while (c.TryGotoNext(MoveType.Before, x => x.MatchCall(_onUnitSphere))) {
                c.Remove();
                c.Emit(OpCodes.Ldsfld, namePrefixRef);
                c.Emit(OpCodes.Ldstr, name);
                c.Emit(OpCodes.Call, stringConcatRef);
                c.Emit(OpCodes.Call, onOnUnitSphereRef);
            }
        }

        private static string TrimNamespace(string name) {
            const string playMakerNs = "HutongGames.PlayMaker.Actions.";
            if (name.StartsWith(playMakerNs))
                name = name.Substring(playMakerNs.Length);

            return name;
        }    
    }
}
