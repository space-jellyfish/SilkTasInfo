using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Assembly_CSharp.TasInfo.mm.Source.Utils;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using Mono.Cecil.Cil;
using MonoMod;
using MonoMod.Cil;
using MonoMod.RuntimeDetour.HookGen;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace Assembly_CSharp.TasInfo.mm.Source {
    public static class RandomInjection {
        private static MethodInfo _rangeFloat;
        private static MethodInfo _rangeInt;
        private static MethodInfo _getRwi;
        private static MethodInfo _insideUnitCircle;
        private static MethodInfo _onUnitSphere;
        private static Dictionary<string, PlaybackState> _playback;
        private static List<Dictionary<string, List<float>>> _recording;
        private static Dictionary<object, string> _objectIds;
        private static List<string> _detailLog;
        private static List<string> _sceneNames;
        private static object _lock;
        private static int _sceneIndex;
        private static int _nextSceneRollCount;

        public static bool EnablePlayback;
        public static bool EnableRecording;
        public static bool EnableDetailLogging;

        public static int SceneIndex => _sceneIndex;

        private const BindingFlags allFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static void OnPreRender() {
            if (UnityEngine.Input.GetKeyDown(KeyCode.Equals)) {
                DumpLogs();
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.LeftBracket)) {
                RollRngNextScene();
            }
        }

        public static void Init() {
            _lock = new object();
            _sceneNames = new List<string>();
            EnableRecording = true;
            EnableDetailLogging = false;
            EnablePlayback = true;
            _sceneIndex = 0;
            _sceneNames.Add("Start");

            if (EnableDetailLogging)
                _detailLog = new List<string>();

            var ueRandom = typeof(UnityEngine.Random);
            _rangeFloat = ueRandom.GetMethod("Range", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(float), typeof(float) }, null);
            _rangeInt = ueRandom.GetMethod("Range", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(int), typeof(int) }, null);
            _getRwi = typeof(ActionHelpers).GetMethod("GetRandomWeightedIndex", BindingFlags.Static | BindingFlags.Public);
            _insideUnitCircle = ueRandom.GetProperty("insideUnitCircle", BindingFlags.Static | BindingFlags.Public)?.GetGetMethod();
            _onUnitSphere = ueRandom.GetProperty("onUnitSphere", BindingFlags.Static | BindingFlags.Public)?.GetGetMethod();

            var targetMethods = new List<MethodInfo>();
            RandomInjectionCallsV1.AddRandomCalls(targetMethods);
            foreach (var method in targetMethods.Where(m => m != null)) {
                try {
                    if (typeof(FsmStateAction).IsAssignableFrom(method.DeclaringType)) {
                        InjectOnRangeFsm(method);
                    } else if (typeof(MonoBehaviour).IsAssignableFrom(method.DeclaringType) && !method.IsStatic) {
                        InjectOnRangeMB(method);
                    } else {
                        InjectOnRange(method);
                    }
                } catch (Exception ex) {
                    Debug.Log($"Failed to hook method: {method.DeclaringType.FullName}.{method.Name}");
                }
            }

            if (targetMethods.Any(m => m == null))
                Debug.Log("One or more Random-using methods failed to bind");

            var rwiMethods = new[] {
                typeof(AudioPlayerOneShot).GetMethod("DoPlayRandomClip", allFlags),
                typeof(AudioPlayRandom).GetMethod("DoPlayRandomClip", allFlags),
                typeof(AudioPlayRandomVoice).GetMethod("DoPlayRandomClip", allFlags),
                typeof(SelectRandomGameObjectV2).GetMethod("DoSelectRandomGameObject", allFlags),
                typeof(SendRandomEventV2).GetMethod("OnEnter", allFlags),
                typeof(SendRandomEventV3).GetMethod("OnEnter", allFlags),
                typeof(SendRandomEventV3ActiveBool).GetMethod("OnEnter", allFlags),
                typeof(SendRandomEventV4).GetMethod("OnEnter", allFlags),
                typeof(PlayRandomAnimation).GetMethod("DoPlayRandomAnimation", allFlags),
                typeof(PlayRandomSound).GetMethod("DoPlayRandomClip", allFlags),
                typeof(SelectRandomColor).GetMethod("DoSelectRandomColor", allFlags),
                typeof(SelectRandomGameObject).GetMethod("DoSelectRandomGameObject", allFlags),
                typeof(SelectRandomFloat).GetMethod("DoSelectRandomString", allFlags),
                typeof(SelectRandomInt).GetMethod("DoSelectRandomString", allFlags),
                typeof(SendRandomEvent).GetMethod("OnEnter", allFlags),
                typeof(SelectRandomString).GetMethod("DoSelectRandomString", allFlags),
                typeof(SelectRandomVector2).GetMethod("DoSelectRandom", allFlags),
                typeof(SelectRandomVector3).GetMethod("DoSelectRandomColor", allFlags),
            };

            foreach (var method in rwiMethods.Where(m => m != null)) {
                InjectGetRwiFsm(method);
            }

            if (rwiMethods.Any(m => m == null))
                Debug.Log("One or more RWI-using methods failed to bind");


            //Various methods call extension/helper methods that we've patched directly
            //These need to be injected to record contextual name information in the thread-local field on entry
            try {
                InjectNamePrefixForType(typeof(LocalisedTextCollectionData), null, "GetRandom");
                InjectNamePrefixForType(typeof(RandomAudioClipTable), null, "SelectRandomClip");
                InjectNamePrefixForType(typeof(BreakableWithExternalDebris), null, "CreateAdditionalDebrisParts");
                InjectNamePrefixForType(typeof(Breakable), null, "Break");
                InjectNamePrefixForType(typeof(BreakableHolder), null, "FlingHolding");
                InjectNamePrefixForType(typeof(BreakableObject), null, "OnTriggerEnter2D");
                InjectNamePrefixForType(typeof(BlackThreadState), null, "SetupThreaded");
                InjectNamePrefixForType(typeof(BlackThreadState), "<ThreadAttackTest>d__", "MoveNext");
                InjectNamePrefixForType(typeof(BlackThreadStrand), "<BehaviourRoutine>d__", "MoveNext");
                InjectNamePrefixForType(typeof(EnableRandomGameObjectOnEnable), null, "OnEnable");
                InjectNamePrefixForType(typeof(HeroAnimationController), null, "UpdateAnimation");
                InjectNamePrefixForType(typeof(CorpseItems), null, "GetPickupItems");
                InjectNamePrefixForType(typeof(HealthManager), null, "Die");
            } catch (Exception ex) {
                Debug.Log("Error while injecting extension context: " + ex.Message);
            }

            //Hook System.Random.Next calls in specific methods
            try {
                InjectRandomNextForType(typeof(ActivateRandomChildren), null, "OnEnable");
                InjectRandomNextForType(typeof(HutongGames.PlayMaker.Actions.GetRandomChildSceneSeed), null, "OnEnter");
            } catch (Exception ex) {
                Debug.Log("Error while injecting System.Random.Next hooks: " + ex.Message);
            }

            //Wire up rules patches
            patch_Extensions.ExtOnRangeInt = OnRangeInt;
            patch_Extensions.ExtOnRangeFloat = OnRangeFloat;
            patch_Extensions.ExtOnInsideUnitCircle = OnInsideUnitCircle;
            patch_Extensions.ExtOnOnUnitSphere = OnOnUnitSphere;

            _playback = new Dictionary<string, PlaybackState>();
            _recording = new List<Dictionary<string, List<float>>>();
            _objectIds = new Dictionary<object, string>();

            //Load first scene playback, if it exists
            if (EnablePlayback)
                LoadPlaybackFile(0);
        }

        private static void LoadPlaybackFile(int sceneIndex) {
            Debug.Log($"Checking for playback file for S{sceneIndex:00000}...");
            string playbackPath = "./Playback/RNG";
            if (Directory.Exists(playbackPath)) {
                var files = Directory.GetFiles(playbackPath);
                var file = files.FirstOrDefault(f => Path.GetFileName(f).StartsWith($"S{sceneIndex:00000}"));
                EnablePlayback = file != null;
                if (EnablePlayback) {
                    LoadPlaybackFile(file);
                    Debug.Log($"Loaded Playback: {file}");
                }
            }
        }

        private static void LoadPlaybackFile(string file) {
            using (var stream = File.Open(file, FileMode.Open, FileAccess.Read))
            using (var reader = new StreamReader(stream)) {
                var playback = _playback;
                playback.Clear();
                string line;
                while ((line = reader.ReadLine()) != null) {
                    if (line.Length > 0 && line[0] != '#') {
                        int indexFirst = line.IndexOf(',');
                        if (indexFirst > -1) {
                            var name = line.Substring(0, indexFirst).Trim();
                            var values = StringUtils.ParseFloats(line.Substring(indexFirst + 1));
                            if (!playback.TryGetValue(name, out var playbackState)) {
                                playbackState = new PlaybackState();
                                playback.Add(name, playbackState);
                            }

                            playbackState.Values.AddRange(values);
                        }
                    }
                }
            }
        }

        public static void DumpLogs() {
            lock (_lock) {
                if (!Directory.Exists("./Recording"))
                    Directory.CreateDirectory("./Recording");
                string recordingPath = "./Recording/RNG";
                if (!Directory.Exists(recordingPath))
                    Directory.CreateDirectory(recordingPath);

                for (int i = 0; i < _recording.Count; i++) {
                    var recording = _recording[i];
                    var sceneName = i < _sceneNames.Count ? _sceneNames[i] : "Unknown";
                    using (var stream = File.Open($"{recordingPath}/S{i:00000}_{sceneName}.csv", FileMode.Create, FileAccess.Write))
                    using (var writer = new StreamWriter(stream)) {

                        foreach (var name in recording.Keys) {
                            writer.WriteLine($"{name},{StringUtils.Join(",", recording[name])}");
                        }
                    }
                }

                if (EnableDetailLogging && _detailLog != null) {
                    using (var stream = File.Open("./HK_Rng_Details.csv", FileMode.Create, FileAccess.Write))
                    using (var writer = new StreamWriter(stream)) {
                        foreach (var entry in _detailLog) {
                            writer.WriteLine(entry);
                        }
                    }
                }
            }
        }

        public static void RollRngNextScene() {
            _nextSceneRollCount++;
        }

        //public static void NotifyBeginScene(string destScene) {
        //    if (!_leftScene) {
        //        _sceneIndex++;
        //    }
        //    _leftScene = false;

        //    lock (_lock) {
        //        _sceneNames.Add(destScene);
        //        if (EnableDetailLogging && _detailLog != null) {
        //            _detailLog.Add("");
        //            _detailLog.Add("");
        //            _detailLog.Add("### Scene: " + destScene);
        //            _detailLog.Add("");
        //        }
        //    }
        //}

        //public static void OnLeftScene() {
        //    _sceneIndex++;
        //    _leftScene = true;
        //    Debug.Log("Left scene");
        //    if (_nextSceneRollCount > 0) {
        //        //Repeatedly call Random to increment the seed
        //        for (int i = 0; i < _nextSceneRollCount; i++) {
        //            var discard = UnityEngine.Random.Range(0, 1);
        //        }
        //        _nextSceneRollCount = 0;

        //        //Disable playback for just this scene
        //        if (_sceneIndex < _playback.Count-1) {
        //            _playback[_sceneIndex+1].Clear();
        //        }
        //    }
        //}

        public static void OnChangeScene(Scene scene) {
            lock (_lock) {
                _sceneIndex++;
                var name = scene.name;
                _sceneNames.Add(name);
                if (EnableDetailLogging && _detailLog != null) {
                    _detailLog.Add("");
                    _detailLog.Add("");
                    _detailLog.Add("### Scene: " + name);
                    _detailLog.Add("");
                }

                //Object tracking is on a per-scene basis
                _objectIds.Clear();
            }

            if (_nextSceneRollCount > 0) {
                //Repeatedly call Random to increment the seed
                for (int i = 0; i < _nextSceneRollCount; i++) {
                    var discard = UnityEngine.Random.Range(0, 1);
                }
                _nextSceneRollCount = 0;

                //Disable playback for just this scene
                _playback.Clear();
            } else {                
                LoadPlaybackFile(_sceneIndex);
            }
        }

        private static void InjectOnRange(MethodInfo method) {
            HookEndpointManager.Modify(method, (Action<ILContext>)InjectOnRange);
        }

        private static void InjectOnRangeMB(MethodInfo method) {
            HookEndpointManager.Modify(method, (Action<ILContext>)InjectOnRangeMB);
        }

        private static void InjectOnRangeFsm(MethodInfo method) {
            HookEndpointManager.Modify(method, (Action<ILContext>)InjectOnRangeFsm);
        }

        private static void InjectGetRwiFsm(MethodInfo method) {
            HookEndpointManager.Modify(method, (Action<ILContext>)InjectGetRwiFsm);
        }

        private static void InjectRandomNextMB(ILContext il, string methodName) {
            var nextMax = typeof(System.Random).GetMethod("Next", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(int) }, null);
            var nextMinMax = typeof(System.Random).GetMethod("Next", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(int), typeof(int) }, null);

            var c = new ILCursor(il);

            // Hook Next(int maxValue)
            c.Goto(0);
            while (c.TryGotoNext(MoveType.Before, x => x.MatchCallvirt(nextMax))) {
                c.Remove();
                c.Emit(OpCodes.Ldstr, methodName);
                c.Emit(OpCodes.Ldarg_0);
                c.Emit(OpCodes.Call, typeof(RandomInjection).GetMethod("OnNextMaxMB", BindingFlags.Public | BindingFlags.Static));
            }

            // Hook Next(int minValue, int maxValue)
            c.Goto(0);
            while (c.TryGotoNext(MoveType.Before, x => x.MatchCallvirt(nextMinMax))) {
                c.Remove();
                c.Emit(OpCodes.Ldstr, methodName);
                c.Emit(OpCodes.Ldarg_0);
                c.Emit(OpCodes.Call, typeof(RandomInjection).GetMethod("OnNextMinMaxMB", BindingFlags.Public | BindingFlags.Static));
            }
        }

        private static void InjectRandomNextFsm(ILContext il, string methodName) {
            var nextMax = typeof(System.Random).GetMethod("Next", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(int) }, null);
            var nextMinMax = typeof(System.Random).GetMethod("Next", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(int), typeof(int) }, null);

            var c = new ILCursor(il);

            // Hook Next(int maxValue)
            c.Goto(0);
            while (c.TryGotoNext(MoveType.Before, x => x.MatchCallvirt(nextMax))) {
                c.Remove();
                c.Emit(OpCodes.Ldstr, methodName);
                c.Emit(OpCodes.Ldarg_0);
                c.Emit(OpCodes.Castclass, typeof(FsmStateAction));
                c.Emit(OpCodes.Call, typeof(RandomInjection).GetMethod("OnNextMaxFsm", BindingFlags.Public | BindingFlags.Static));
            }

            // Hook Next(int minValue, int maxValue)
            c.Goto(0);
            while (c.TryGotoNext(MoveType.Before, x => x.MatchCallvirt(nextMinMax))) {
                c.Remove();
                c.Emit(OpCodes.Ldstr, methodName);
                c.Emit(OpCodes.Ldarg_0);
                c.Emit(OpCodes.Castclass, typeof(FsmStateAction));
                c.Emit(OpCodes.Call, typeof(RandomInjection).GetMethod("OnNextMinMaxFsm", BindingFlags.Public | BindingFlags.Static));
            }
        }

        internal static void InjectOnRange(ILContext il) {
            var name = TrimNamespace(il.Method.Name);
            var c = new ILCursor(il);
            while (c.TryGotoNext(MoveType.Before, x => x.MatchCall(_rangeFloat))) {
                c.Remove();
                c.Emit(OpCodes.Ldstr, name);
                c.Emit(OpCodes.Call, typeof(RandomInjection).GetMethod("OnRangeFloat", BindingFlags.Public | BindingFlags.Static));
            }

            c.Goto(0);
            while (c.TryGotoNext(MoveType.Before, x => x.MatchCall(_rangeInt))) {
                c.Remove();
                c.Emit(OpCodes.Ldstr, name);
                c.Emit(OpCodes.Call, typeof(RandomInjection).GetMethod("OnRangeInt", BindingFlags.Public | BindingFlags.Static));
            }

            c.Goto(0);
            while (c.TryGotoNext(MoveType.Before, x => x.MatchCall(_insideUnitCircle))) {
                c.Remove();
                c.Emit(OpCodes.Ldstr, name);
                c.Emit(OpCodes.Call, typeof(RandomInjection).GetMethod("OnInsideUnitCircle", BindingFlags.Public | BindingFlags.Static));
            }

            c.Goto(0);
            while (c.TryGotoNext(MoveType.Before, x => x.MatchCall(_onUnitSphere))) {
                c.Remove();
                c.Emit(OpCodes.Ldstr, name);
                c.Emit(OpCodes.Call, typeof(RandomInjection).GetMethod("OnOnUnitSphere", BindingFlags.Public | BindingFlags.Static));
            }

            // Inject NamePrefix handling for extension method calls
            InjectExtensionPrefixStatic(il, name);
        }

        internal static void InjectOnRangeMB(ILContext il) {
            var name = TrimNamespace(il.Method.Name);
            var c = new ILCursor(il);
            while (c.TryGotoNext(MoveType.Before, x => x.MatchCall(_rangeFloat))) {
                c.Remove();
                c.Emit(OpCodes.Ldstr, name);
                c.Emit(OpCodes.Ldarg_0);
                c.Emit(OpCodes.Call, typeof(RandomInjection).GetMethod("OnRangeFloatMB", BindingFlags.Public | BindingFlags.Static));
            }

            c.Goto(0);
            while (c.TryGotoNext(MoveType.Before, x => x.MatchCall(_rangeInt))) {
                c.Remove();
                c.Emit(OpCodes.Ldstr, name);
                c.Emit(OpCodes.Ldarg_0);
                c.Emit(OpCodes.Call, typeof(RandomInjection).GetMethod("OnRangeIntMB", BindingFlags.Public | BindingFlags.Static));
            }

            c.Goto(0);
            while (c.TryGotoNext(MoveType.Before, x => x.MatchCall(_insideUnitCircle))) {
                c.Remove();
                c.Emit(OpCodes.Ldstr, name);
                c.Emit(OpCodes.Ldarg_0);
                c.Emit(OpCodes.Call, typeof(RandomInjection).GetMethod("OnInsideUnitCircleMB", BindingFlags.Public | BindingFlags.Static));
            }

            c.Goto(0);
            while (c.TryGotoNext(MoveType.Before, x => x.MatchCall(_onUnitSphere))) {
                c.Remove();
                c.Emit(OpCodes.Ldstr, name);
                c.Emit(OpCodes.Ldarg_0);
                c.Emit(OpCodes.Call, typeof(RandomInjection).GetMethod("OnOnUnitSphereMB", BindingFlags.Public | BindingFlags.Static));
            }

            // Inject NamePrefix handling for extension method calls
            InjectExtensionPrefixMB(il, name);
        }

        internal static void InjectOnRangeFsm(ILContext il) {
            var name = TrimNamespace(il.Method.Name);
            var c = new ILCursor(il);
            while (c.TryGotoNext(MoveType.Before, x => x.MatchCall(_rangeFloat))) {
                c.Remove();
                c.Emit(OpCodes.Ldstr, name);
                c.Emit(OpCodes.Ldarg_0);
                c.Emit(OpCodes.Castclass, typeof(FsmStateAction));
                c.Emit(OpCodes.Call, typeof(RandomInjection).GetMethod("OnRangeFloatFsm", BindingFlags.Public | BindingFlags.Static));
            }

            c.Goto(0);
            while (c.TryGotoNext(MoveType.Before, x => x.MatchCall(_rangeInt))) {
                c.Remove();
                c.Emit(OpCodes.Ldstr, name);
                c.Emit(OpCodes.Ldarg_0);
                c.Emit(OpCodes.Castclass, typeof(FsmStateAction));
                c.Emit(OpCodes.Call, typeof(RandomInjection).GetMethod("OnRangeIntFsm", BindingFlags.Public | BindingFlags.Static));
            }

            c.Goto(0);
            while (c.TryGotoNext(MoveType.Before, x => x.MatchCall(_insideUnitCircle))) {
                c.Remove();
                c.Emit(OpCodes.Ldstr, name);
                c.Emit(OpCodes.Ldarg_0);
                c.Emit(OpCodes.Castclass, typeof(FsmStateAction));
                c.Emit(OpCodes.Call, typeof(RandomInjection).GetMethod("OnInsideUnitCircleFsm", BindingFlags.Public | BindingFlags.Static));
            }

            c.Goto(0);
            while (c.TryGotoNext(MoveType.Before, x => x.MatchCall(_onUnitSphere))) {
                c.Remove();
                c.Emit(OpCodes.Ldstr, name);
                c.Emit(OpCodes.Ldarg_0);
                c.Emit(OpCodes.Castclass, typeof(FsmStateAction));
                c.Emit(OpCodes.Call, typeof(RandomInjection).GetMethod("OnOnUnitSphereFsm", BindingFlags.Public | BindingFlags.Static));
            }

            // Inject NamePrefix handling for extension method calls
            InjectExtensionPrefixFsm(il, name);
        }

        private static void InjectGetRwiFsm(ILContext il) {
            var name = TrimNamespace(il.Method.Name);
            var c = new ILCursor(il);
            while (c.TryGotoNext(MoveType.Before, x => x.MatchCall(_getRwi))) {
                c.Remove();
                c.Emit(OpCodes.Ldstr, name);
                c.Emit(OpCodes.Ldarg_0);
                c.Emit(OpCodes.Castclass, typeof(FsmStateAction));
                c.Emit(OpCodes.Call, typeof(RandomInjection).GetMethod("OnGetRwiFsm", BindingFlags.Public | BindingFlags.Static));
            }
        }

        private static void InjectExtensionPrefixStatic(ILContext il, string methodName) {
            var module = il.Method.Module;
            var extensionsType = typeof(patch_Extensions);
            if (extensionsType == null) return;

            var namePrefixField = extensionsType.GetField("NamePrefix", allFlags);            
            var getNameMethod = typeof(RandomInjection).GetMethod("GetNameStatic", BindingFlags.Public | BindingFlags.Static);

            InjectExtensionMethodPrefixes(il, methodName, namePrefixField, getNameMethod, false, false);
        }

        private static void InjectExtensionPrefixMB(ILContext il, string methodName) {
            var module = il.Method.Module;
            var extensionsType = typeof(patch_Extensions);
            if (extensionsType == null) return;

            var namePrefixField = extensionsType.GetField("NamePrefix", allFlags);            
            var getNameMethod = typeof(RandomInjection).GetMethod("GetNameMB", BindingFlags.Public | BindingFlags.Static);

            InjectExtensionMethodPrefixes(il, methodName, namePrefixField, getNameMethod, true, false);
        }

        private static void InjectExtensionPrefixFsm(ILContext il, string methodName) {
            var module = il.Method.Module;
            var extensionsType = typeof(patch_Extensions);
            if (extensionsType == null) return;

            var namePrefixField = extensionsType.GetField("NamePrefix", allFlags);            
            var getNameMethod = typeof(RandomInjection).GetMethod("GetNameFsm", BindingFlags.Public | BindingFlags.Static);

            InjectExtensionMethodPrefixes(il, methodName, namePrefixField, getNameMethod, true, true);
        }

        public static void InjectNamePrefixAtStart(ILContext il, string methodName, bool isMB, bool isFsm) {
            var module = il.Method.Module;
            var extensionsType = typeof(patch_Extensions);
            if (extensionsType == null) {
                Debug.Log("Failed to find Extensions Type");
                return;
            }

            var namePrefixField = extensionsType.GetField("NamePrefix", allFlags);            
            MethodInfo getNameMethod;
            if (isFsm) {
                getNameMethod = typeof(RandomInjection).GetMethod("GetNameFsm", BindingFlags.Public | BindingFlags.Static);
            } else if (isMB) {
                getNameMethod = typeof(RandomInjection).GetMethod("GetNameMB", BindingFlags.Public | BindingFlags.Static);
            } else {
                getNameMethod = typeof(RandomInjection).GetMethod("GetNameStatic", BindingFlags.Public | BindingFlags.Static);
            }

            var c = new ILCursor(il);
            c.Goto(0); // Go to start of method

            c.Emit(OpCodes.Ldstr, methodName);
            if (isMB || isFsm) {
                c.Emit(OpCodes.Ldarg_0);
                if (isFsm) {
                    c.Emit(OpCodes.Castclass, typeof(FsmStateAction));
                }
            }
            c.Emit(OpCodes.Call, getNameMethod);
            c.Emit(OpCodes.Stsfld, namePrefixField);
        }

        public static void InjectNamePrefixForType(Type type, string nestedTypeName, string methodName) {
            const BindingFlags allFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            if (type == null)
                return;

            Type targetType = type;
            if (!string.IsNullOrEmpty(nestedTypeName)) {
                var candidates = type.GetNestedTypes(allFlags).Where(n => n.Name.StartsWith(nestedTypeName)).ToList();
                if (candidates.Count > 1) {
                    targetType = candidates.FirstOrDefault(c => c.Name == nestedTypeName);
                }
                if (targetType == type) {
                    targetType = candidates.FirstOrDefault();
                }
                if (targetType == null) {
                    Debug.Log($"Failed to find nested type {nestedTypeName} in {type.Name}");
                    return;
                }
            }

            var methods = targetType.GetMethods(allFlags).Where(m => m.Name == methodName).ToArray();
            if (methods.Length == 0) {
                Debug.Log($"Failed to find method {methodName} in {targetType.Name}");
                return;
            }

            foreach (var method in methods) {
                try {
                    bool isFsm = typeof(FsmStateAction).IsAssignableFrom(method.DeclaringType);
                    bool isMB = typeof(MonoBehaviour).IsAssignableFrom(method.DeclaringType) && !method.IsStatic;

                    var trimmedName = TrimNamespace(method.DeclaringType.FullName + "." + method.Name);

                    HookEndpointManager.Modify(method, (Action<ILContext>)(il => {
                        InjectNamePrefixAtStart(il, trimmedName, isMB, isFsm);
                    }));
                } catch (Exception ex) {
                    Debug.Log($"Failed to inject NamePrefix for method: {method.DeclaringType.FullName}.{method.Name}: {ex.Message}");
                }
            }
        }

        public static void InjectRandomNextForType(Type type, string nestedTypeName, string methodName) {
            const BindingFlags allFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            if (type == null)
                return;

            Type targetType = type;
            if (!string.IsNullOrEmpty(nestedTypeName)) {
                var candidates = type.GetNestedTypes(allFlags).Where(n => n.Name.StartsWith(nestedTypeName)).ToList();
                if (candidates.Count > 1) {
                    targetType = candidates.FirstOrDefault(c => c.Name == nestedTypeName);
                }
                if (targetType == type) {
                    targetType = candidates.FirstOrDefault();
                }
                if (targetType == null) {
                    Debug.Log($"Failed to find nested type {nestedTypeName} in {type.Name}");
                    return;
                }
            }

            var methods = targetType.GetMethods(allFlags).Where(m => m.Name == methodName).ToArray();
            if (methods.Length == 0) {
                Debug.Log($"Failed to find method {methodName} in {targetType.Name}");
                return;
            }

            foreach (var method in methods) {
                try {
                    bool isFsm = typeof(FsmStateAction).IsAssignableFrom(method.DeclaringType);
                    bool isMB = typeof(MonoBehaviour).IsAssignableFrom(method.DeclaringType) && !method.IsStatic;

                    var trimmedName = TrimNamespace(method.DeclaringType.FullName + "." + method.Name);

                    if (isFsm) {
                        HookEndpointManager.Modify(method, (Action<ILContext>)(il => {
                            InjectRandomNextFsm(il, trimmedName);
                        }));
                    } else if (isMB) {
                        HookEndpointManager.Modify(method, (Action<ILContext>)(il => {
                            InjectRandomNextMB(il, trimmedName);
                        }));
                    } else {
                        Debug.Log($"Method {method.DeclaringType.FullName}.{method.Name} is neither MonoBehaviour nor FsmStateAction");
                    }
                } catch (Exception ex) {
                    Debug.Log($"Failed to inject Random.Next hooks for method: {method.DeclaringType.FullName}.{method.Name}: {ex.Message}");
                }
            }
        }

        private static void InjectExtensionMethodPrefixes(ILContext il, string methodName, FieldInfo namePrefixField, MethodInfo getNameMethod, bool loadArg0, bool castToFsm) {
            var c = new ILCursor(il);
            var extensionMethods = new[] {
                ("Extensions", "GetRandomElement"),
                ("Extensions", "GetAndRemoveRandomElement"),
                ("Extensions", "Shuffle"),
                ("Extensions", "RandomInRange"),
                ("Probability", "GetRandomItemRootByProbability"),
                ("RandomTable", "TrySelectValue")
            };

            foreach (var (typeName, methodNameToFind) in extensionMethods) {
                c.Goto(0);
                while (c.TryGotoNext(MoveType.Before,
                    x => x.OpCode == OpCodes.Call &&
                         x.Operand is Mono.Cecil.MethodReference mr &&
                         mr.Name == methodNameToFind &&
                         (mr.DeclaringType.Name == typeName || mr.DeclaringType.FullName.Contains(typeName)))) {

                    // Inject before the call: set NamePrefix
                    c.Emit(OpCodes.Ldstr, methodName);
                    if (loadArg0) {
                        c.Emit(OpCodes.Ldarg_0);
                        if (castToFsm) {
                            c.Emit(OpCodes.Castclass, typeof(FsmStateAction));
                        }
                    }
                    c.Emit(OpCodes.Call, getNameMethod);
                    c.Emit(OpCodes.Stsfld, namePrefixField);

                    // Move past the extension method call
                    c.Index++;

                    // Inject after the call: clear NamePrefix
                    c.Emit(OpCodes.Ldstr, "");
                    c.Emit(OpCodes.Stsfld, namePrefixField);
                }
            }
        }

        public static string TrimNamespace(string name) {
            const string playMakerNs = "HutongGames.PlayMaker.Actions.";
            if (name.StartsWith(playMakerNs))
                name = name.Substring(playMakerNs.Length);

            return name;
        }

        public static float OnRangeFloat(float min, float max, string name) {
            //Bypass when no actual range
            if (min == max) {
                _ = UnityEngine.Random.Range(min, max);
                return min;
            }

            lock (_lock) {
                CheckScene();
                name = $"{name}({min}:{max})";
                float result;
                if (EnablePlayback && TryGetPlayback(name, out var playbackState) && playbackState.Index < playbackState.Values.Count) {
                    playbackState.Index++;
                    result = playbackState.Values[playbackState.Index - 1];
                    //Improve edge case sync by calling Random so that seed progression should match even when using playback ideally
                    var discard = UnityEngine.Random.Range(min, max);
                } else {
                    result = UnityEngine.Random.Range(min, max);
                }

                if (EnableRecording) {
                    var list = GetRecording(name);
                    list.Add(result);
                }

                if (_detailLog != null) {
                    _detailLog.Add($"{name}: {result}");
                }

                return result;
            }
        }

        public static int OnRangeInt(int min, int max, string name) {
            //Bypass when no actual range
            if (min == max) {
                _ = UnityEngine.Random.Range(min, max);
                return min;
            }

            lock (_lock) {
                CheckScene();
                name = $"{name}({min}:{max})";
                int result;
                if (EnablePlayback && TryGetPlayback(name, out var playbackState) && playbackState.Index < playbackState.Values.Count) {
                    playbackState.Index++;
                    result = (int)playbackState.Values[playbackState.Index - 1];
                    //Improve edge case sync by calling Random so that seed progression should match even when using playback ideally
                    var discard = UnityEngine.Random.Range(min, max);
                } else {
                    result = UnityEngine.Random.Range(min, max);
                }

                if (EnableRecording) {
                    var list = GetRecording(name);
                    list.Add(result);
                }

                if (_detailLog != null) {
                    _detailLog.Add($"{name}: {result}");
                }

                return result;
            }
        }

        private static string GetObjectId(GameObject obj) {
            if (!obj)
                return "X";

            if (!_objectIds.TryGetValue(obj, out var id)) {
                string posPrefix = "";
                if (obj.transform) {
                    posPrefix = $"X{obj.transform.position.x:0.00}:Y{obj.transform.position.y:0.00}";
                } else {
                    posPrefix = "N";
                }
                var existingCount = _objectIds.Values.Count(x => x.StartsWith(posPrefix));
                id = $"{obj.name ?? ""}:{posPrefix}:{existingCount + 1}";
                _objectIds.Add(obj, id);
            }

            return id;
        }

        public static Vector2 OnInsideUnitCircle(string name) {
            lock (_lock) {
                CheckScene();
                name = $"{name}(insideUnitCircle)";
                Vector2 result;
                if (EnablePlayback && TryGetPlayback(name, out var playbackState) && playbackState.Index + 1 < playbackState.Values.Count) {
                    result = new Vector2(
                        playbackState.Values[playbackState.Index],
                        playbackState.Values[playbackState.Index + 1]
                    );
                    playbackState.Index += 2;
                    // Call Random to maintain seed progression
                    var discard = UnityEngine.Random.insideUnitCircle;
                } else {
                    result = UnityEngine.Random.insideUnitCircle;
                }

                if (EnableRecording) {
                    var list = GetRecording(name);
                    list.Add(result.x);
                    list.Add(result.y);
                }

                if (_detailLog != null) {
                    _detailLog.Add($"{name}: ({result.x}, {result.y})");
                }

                return result;
            }
        }

        public static Vector3 OnOnUnitSphere(string name) {
            lock (_lock) {
                CheckScene();
                name = $"{name}(onUnitSphere)";
                Vector3 result;
                if (EnablePlayback && TryGetPlayback(name, out var playbackState) && playbackState.Index + 2 < playbackState.Values.Count) {
                    result = new Vector3(
                        playbackState.Values[playbackState.Index],
                        playbackState.Values[playbackState.Index + 1],
                        playbackState.Values[playbackState.Index + 2]
                    );
                    playbackState.Index += 3;
                    // Call Random to maintain seed progression
                    var discard = UnityEngine.Random.onUnitSphere;
                } else {
                    result = UnityEngine.Random.onUnitSphere;
                }

                if (EnableRecording) {
                    var list = GetRecording(name);
                    list.Add(result.x);
                    list.Add(result.y);
                    list.Add(result.z);
                }

                if (_detailLog != null) {
                    _detailLog.Add($"{name}: ({result.x}, {result.y}, {result.z})");
                }

                return result;
            }
        }

        public static Vector2 OnInsideUnitCircleMB(string name, MonoBehaviour component) {
            var id = component ? GetObjectId(component.gameObject) : "Dead";
            return OnInsideUnitCircle($"[{id}]{name}");
        }

        public static Vector3 OnOnUnitSphereMB(string name, MonoBehaviour component) {
            var id = component ? GetObjectId(component.gameObject) : "Dead";
            return OnOnUnitSphere($"[{id}]{name}");
        }

        public static Vector2 OnInsideUnitCircleFsm(string name, FsmStateAction action) {
            var id = GetObjectId(action.Fsm?.GameObject);
            return OnInsideUnitCircle($"[{id}/{action.Fsm?.GameObjectName ?? ""}/{action.Fsm?.Name ?? ""}/{action.State?.Name ?? ""}]{name}");
        }

        public static Vector3 OnOnUnitSphereFsm(string name, FsmStateAction action) {
            var id = GetObjectId(action.Fsm?.GameObject);
            return OnOnUnitSphere($"[{id}/{action.Fsm?.GameObjectName ?? ""}/{action.Fsm?.Name ?? ""}/{action.State?.Name ?? ""}]{name}");
        }

        public static string GetNameStatic(string methodName) {
            return methodName;
        }

        public static string GetNameMB(string methodName, MonoBehaviour component) {
            var id = component ? GetObjectId(component.gameObject) : "Dead";
            return $"[{id}]{methodName}";
        }

        public static string GetNameFsm(string methodName, FsmStateAction action) {
            var id = GetObjectId(action.Fsm?.GameObject);
            return $"[{id}/{action.Fsm?.GameObjectName ?? ""}/{action.Fsm?.Name ?? ""}/{action.State?.Name ?? ""}]{methodName}";
        }

        public static float OnRangeFloatMB(float min, float max, string name, MonoBehaviour component) {
            //Bypass when no actual range
            if (min == max) {
                _ = UnityEngine.Random.Range(min, max);
                return min;
            }

            var id = component ? GetObjectId(component.gameObject) : "Dead";
            return OnRangeFloat(min, max, $"[{id}]{name}");
        }

        public static int OnRangeIntMB(int min, int max, string name, MonoBehaviour component) {
            //Bypass when no actual range
            if (min == max) {
                _ = UnityEngine.Random.Range(min, max);
                return min;
            }

            var id = component ? GetObjectId(component.gameObject) : "Dead";
            return OnRangeInt(min, max, $"[{id}]{name}");
        }

        public static float OnRangeFloatFsm(float min, float max, string name, FsmStateAction action) {
            //Bypass when no actual range
            if (min == max) {
                _ = UnityEngine.Random.Range(min, max);
                return min;
            }

            var id = GetObjectId(action.Fsm?.GameObject);
            return OnRangeFloat(min, max, $"[{id}/{action.Fsm?.GameObjectName ?? ""}/{action.Fsm?.Name ?? ""}/{action.State?.Name ?? ""}]{name}");
        }

        public static int OnRangeIntFsm(int min, int max, string name, FsmStateAction action) {
            //Bypass when no actual range
            if (min == max) {
                _ = UnityEngine.Random.Range(min, max);
                return min;
            }

            var id = GetObjectId(action.Fsm?.GameObject);
            return OnRangeInt(min, max, $"[{id}/{action.Fsm?.GameObjectName ?? ""}/{action.Fsm?.Name ?? ""}/{action.State?.Name ?? ""}]{name}");
        }

        public static int OnGetRwiFsm(FsmFloat[] weights, string name, FsmStateAction action) {
            lock (_lock) {
                CheckScene();
                var id = GetObjectId(action.Fsm?.GameObject);
                var compName = $"[{id}/{action.Fsm?.GameObjectName ?? ""}/{action.Fsm?.Name ?? ""}/{action.State?.Name ?? ""}]{name}";
                int result;
                if (EnablePlayback && TryGetPlayback(compName, out var playbackState) && playbackState.Index < playbackState.Values.Count) {
                    playbackState.Index++;
                    result = (int)playbackState.Values[playbackState.Index - 1];
                    //Improve edge case sync by calling Random so that seed progression should match even when using playback ideally
                    var discard = ActionHelpers.GetRandomWeightedIndex(weights);
                } else {
                    result = ActionHelpers.GetRandomWeightedIndex(weights);
                }

                if (EnableRecording) {
                    var list = GetRecording(compName);
                    list.Add(result);
                }

                if (_detailLog != null) {
                    _detailLog.Add($"{compName}: {result}");
                }

                return result;
            }
        }

        public static int OnNextMaxMB(System.Random instance, int maxValue, string name, MonoBehaviour component) {
            lock (_lock) {
                CheckScene();
                var id = component ? GetObjectId(component.gameObject) : "Dead";
                var compName = $"[{id}]{name}(0:{maxValue})";
                int result;
                if (EnablePlayback && TryGetPlayback(compName, out var playbackState) && playbackState.Index < playbackState.Values.Count) {
                    playbackState.Index++;
                    result = (int)playbackState.Values[playbackState.Index - 1];
                    //Improve edge case sync by calling Random so that seed progression should match even when using playback ideally
                    var discard = instance.Next(maxValue);
                } else {
                    result = instance.Next(maxValue);
                }

                if (EnableRecording) {
                    var list = GetRecording(compName);
                    list.Add(result);
                }

                if (_detailLog != null) {
                    _detailLog.Add($"{compName}: {result}");
                }

                return result;
            }
        }

        public static int OnNextMinMaxMB(System.Random instance, int minValue, int maxValue, string name, MonoBehaviour component) {
            lock (_lock) {
                CheckScene();
                var id = component ? GetObjectId(component.gameObject) : "Dead";
                var compName = $"[{id}]{name}({minValue}:{maxValue})";
                int result;
                if (EnablePlayback && TryGetPlayback(compName, out var playbackState) && playbackState.Index < playbackState.Values.Count) {
                    playbackState.Index++;
                    result = (int)playbackState.Values[playbackState.Index - 1];
                    //Improve edge case sync by calling Random so that seed progression should match even when using playback ideally
                    var discard = instance.Next(minValue, maxValue);
                } else {
                    result = instance.Next(minValue, maxValue);
                }

                if (EnableRecording) {
                    var list = GetRecording(compName);
                    list.Add(result);
                }

                if (_detailLog != null) {
                    _detailLog.Add($"{compName}: {result}");
                }

                return result;
            }
        }

        public static int OnNextMaxFsm(System.Random instance, int maxValue, string name, FsmStateAction action) {
            lock (_lock) {
                CheckScene();
                var id = GetObjectId(action.Fsm?.GameObject);
                var compName = $"[{id}/{action.Fsm?.GameObjectName ?? ""}/{action.Fsm?.Name ?? ""}/{action.State?.Name ?? ""}]{name}(0:{maxValue})";
                int result;
                if (EnablePlayback && TryGetPlayback(compName, out var playbackState) && playbackState.Index < playbackState.Values.Count) {
                    playbackState.Index++;
                    result = (int)playbackState.Values[playbackState.Index - 1];
                    //Improve edge case sync by calling Random so that seed progression should match even when using playback ideally
                    var discard = instance.Next(maxValue);
                } else {
                    result = instance.Next(maxValue);
                }

                if (EnableRecording) {
                    var list = GetRecording(compName);
                    list.Add(result);
                }

                if (_detailLog != null) {
                    _detailLog.Add($"{compName}: {result}");
                }

                return result;
            }
        }

        public static int OnNextMinMaxFsm(System.Random instance, int minValue, int maxValue, string name, FsmStateAction action) {
            lock (_lock) {
                CheckScene();
                var id = GetObjectId(action.Fsm?.GameObject);
                var compName = $"[{id}/{action.Fsm?.GameObjectName ?? ""}/{action.Fsm?.Name ?? ""}/{action.State?.Name ?? ""}]{name}({minValue}:{maxValue})";
                int result;
                if (EnablePlayback && TryGetPlayback(compName, out var playbackState) && playbackState.Index < playbackState.Values.Count) {
                    playbackState.Index++;
                    result = (int)playbackState.Values[playbackState.Index - 1];
                    //Improve edge case sync by calling Random so that seed progression should match even when using playback ideally
                    var discard = instance.Next(minValue, maxValue);
                } else {
                    result = instance.Next(minValue, maxValue);
                }

                if (EnableRecording) {
                    var list = GetRecording(compName);
                    list.Add(result);
                }

                if (_detailLog != null) {
                    _detailLog.Add($"{compName}: {result}");
                }

                return result;
            }
        }

        private static void CheckScene() {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.name != _sceneNames[_sceneIndex]) {
                OnChangeScene(scene);
            }
        }

        private static bool TryGetPlayback(string name, out PlaybackState state) {
            state = null;
            return _playback.TryGetValue(name, out state);
        }

        private static List<float> GetRecording(string name) {
            while (_sceneIndex >= _recording.Count) {
                _recording.Add(new Dictionary<string, List<float>>());
            }

            if (!_recording[_sceneIndex].TryGetValue(name, out var result)) {
                result = new List<float>();
                _recording[_sceneIndex].Add(name, result);
            }

            return result;
        }

        private sealed class PlaybackState {
            public PlaybackState() {
                Values = new List<float>();
            }

            public int Index { get; set; }

            public List<float> Values { get; }
        }
    }
}