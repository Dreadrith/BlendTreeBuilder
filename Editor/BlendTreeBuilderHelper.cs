using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

namespace Editor
{
    public static class BlendTreeBuilderHelper
    {
        #region Path Stuff
        internal enum PathOption
        {
            Normal,
            ForceFolder,
            ForceFile
        }
        internal static string ReadyAssetPath(string path, bool makeUnique = false, PathOption pathOption = PathOption.Normal)
        {
            var forceFolder = pathOption == PathOption.ForceFolder;
            var forceFile = pathOption == PathOption.ForceFile;

            path = forceFile ? LegalizeName(path) : forceFolder ? LegalizePath(path) : LegalizeFullPath(path);
            var isFolder = forceFolder || (!forceFile && string.IsNullOrEmpty(Path.GetExtension(path)));

            if (isFolder)
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                    AssetDatabase.ImportAsset(path);
                }
                else if (makeUnique)
                {
                    path = AssetDatabase.GenerateUniqueAssetPath(path);
                    Directory.CreateDirectory(path);
                    AssetDatabase.ImportAsset(path);
                }
            }
            else
            {
                const string basePath = "Assets";
                var folderPath = Path.GetDirectoryName(path);
                var fileName = Path.GetFileName(path);

                if (string.IsNullOrEmpty(folderPath))
                    folderPath = basePath;
                else if (!folderPath.StartsWith(Application.dataPath) && !folderPath.StartsWith(basePath))
                    folderPath = $"{basePath}/{folderPath}";

                if (folderPath != basePath && !Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                    AssetDatabase.ImportAsset(folderPath);
                }

                path = $"{folderPath}/{fileName}";
                if (makeUnique)
                    path = AssetDatabase.GenerateUniqueAssetPath(path);

            }

            return path;
        }
        internal static string ReadyAssetPath(string folderPath, string fileName, bool makeUnique = false)
        {
            if (string.IsNullOrEmpty(fileName))
                return ReadyAssetPath(LegalizePath(folderPath), makeUnique, PathOption.ForceFolder);
            return string.IsNullOrEmpty(folderPath) ? ReadyAssetPath(LegalizeName(fileName), makeUnique, PathOption.ForceFile) : ReadyAssetPath($"{LegalizePath(folderPath)}/{LegalizeName(fileName)}", makeUnique);
        }

        private static string ReadyAssetPath(Object buddyAsset, string fullName = "", bool makeUnique = true)
        {
            var buddyPath = AssetDatabase.GetAssetPath(buddyAsset);
            var folderPath = Path.GetDirectoryName(buddyPath);
            if (string.IsNullOrEmpty(fullName))
                fullName = Path.GetFileName(buddyPath);
            return ReadyAssetPath(folderPath, fullName, makeUnique);
        }

        private static string LegalizeFullPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("Legalizing empty path! Returned path as 'EmptyPath'");
                return "EmptyPath";
            }

            var ext = Path.GetExtension(path);
            var isFolder = string.IsNullOrEmpty(ext);
            if (isFolder) return LegalizePath(path);

            var folderPath = Path.GetDirectoryName(path);
            var fileName = LegalizeName(Path.GetFileNameWithoutExtension(path));

            if (string.IsNullOrEmpty(folderPath)) return $"{fileName}{ext}";
            folderPath = LegalizePath(folderPath);

            return $"{folderPath}/{fileName}{ext}";
        }

        private static string LegalizePath(string path)
        {
            var regexFolderReplace = Regex.Escape(new string(Path.GetInvalidPathChars()));

            path = path.Replace('\\', '/');
            if (path.IndexOf('/') > 0)
                path = string.Join("/", path.Split('/').Select(s => Regex.Replace(s, $"[{regexFolderReplace}]", "-")));

            return path;

        }

        private static string LegalizeName(string name)
        {
            var regexFileReplace = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            return string.IsNullOrEmpty(name) ? "Unnamed" : Regex.Replace(name, $"[{regexFileReplace}]", "-");
        }
        #endregion

        #region VRC Stuff
        internal static readonly string[] BuiltinParameters = {
            "IsLocal",
            "Viseme",
            "GestureLeft",
            "GestureRight",
            "GestureLeftWeight",
            "GestureRightWeight",
            "AngularY",
            "VelocityX",
            "VelocityY",
            "VelocityZ",
            "Upright",
            "Grounded",
            "Seated",
            "AFK",
            "TrackingType",
            "LocomotionMode",
            "VRMode",
            "MuteSelf",
            "InStation"
        };

        internal static AnimatorController GetPlayableLayer(this VRCAvatarDescriptor avi, VRCAvatarDescriptor.AnimLayerType type)
            => avi.baseAnimationLayers.Concat(avi.specialAnimationLayers).FirstOrDefault(p => p.type == type).animatorController as AnimatorController;

        private static bool SetPlayableLayer(this VRCAvatarDescriptor avatar, VRCAvatarDescriptor.AnimLayerType type, RuntimeAnimatorController controller)
        {
            return SetPlayableLayerInternal(avatar.baseAnimationLayers) || SetPlayableLayerInternal(avatar.specialAnimationLayers);

            bool SetPlayableLayerInternal(VRCAvatarDescriptor.CustomAnimLayer[] playableLayers)
            {
                for (var i = 0; i < playableLayers.Length; i++)
                    if (playableLayers[i].type == type)
                    {
                        if (controller) avatar.customizeAnimationLayers = true;
                        playableLayers[i].isDefault = !controller;
                        playableLayers[i].animatorController = controller;
                        EditorUtility.SetDirty(avatar);
                        return true;
                    }
                return false;
            }
        }
        internal static AnimatorController ReadyPlayableLayer(this VRCAvatarDescriptor avatar, VRCAvatarDescriptor.AnimLayerType type, string folderPath)
        {
            var controller = avatar.GetPlayableLayer(type);
            if (!controller)
            {
                controller = new AnimatorController();

                var assetFullName = $"{avatar.gameObject.name} {type}.controller";
                var assetPath = ReadyAssetPath(folderPath, assetFullName, true);
                AssetDatabase.CreateAsset(controller, assetPath);
                controller.AddLayer("Base Layer");
            }

            SetPlayableLayer(avatar, type, controller);
            return controller;

        }
        internal static VRCExpressionParameters ReadyExpressionParameters(this VRCAvatarDescriptor avatar, string folderPath)
        {
            var parameters = avatar.expressionParameters;
            if (!parameters)
            {
                parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                parameters.parameters = Array.Empty<VRCExpressionParameters.Parameter>();

                var assetFullName = $"{avatar.name} Parameters.asset";
                var assetPath = ReadyAssetPath(folderPath, assetFullName, true);

                AssetDatabase.CreateAsset(parameters, assetPath);
            }
            avatar.customExpressions = true;
            avatar.expressionParameters = parameters;
            EditorUtility.SetDirty(avatar);
            return parameters;
        }
        #endregion

        #region Controller Stuff
        internal static void ReadyParameter(this AnimatorController controller, string parameter, AnimatorControllerParameterType type, float defaultValue)
        {
            foreach (var p in controller.parameters)
            {
                if (p.name != parameter) continue;
                if (p.type != type)
                    Debug.LogWarning($"Type mismatch! Parameter {parameter} already exists in {controller.name} but with type {p.type} rather than {type}");
                return;
            }

            controller.AddParameter(new AnimatorControllerParameter { name = parameter, type = type, defaultBool = defaultValue != 0, defaultInt = (int)defaultValue, defaultFloat = defaultValue });
        }
        internal static AnimatorControllerLayer AddLayer(this AnimatorController controller, string name, float defaultWeight)
        {
            var newLayer = new AnimatorControllerLayer
            {
                name = name,
                defaultWeight = defaultWeight,
                stateMachine = new AnimatorStateMachine
                {
                    name = name,
                    hideFlags = HideFlags.HideInHierarchy,
                    exitPosition = new Vector3(50, 40),
                    anyStatePosition = new Vector3(50, 80),
                    entryPosition = new Vector3(50, 120)
                },
            };
            AssetDatabase.AddObjectToAsset(newLayer.stateMachine, controller);
            controller.AddLayer(newLayer);
            return newLayer;
        }

        internal static BlendTree CreateBlendTreeInState(this AnimatorState state, string name = "")
        {
            var newTree = new BlendTree { name = name };
            Undo.RecordObject(state, "Create Blendtree In State");
            Undo.RegisterCreatedObjectUndo(newTree, "Create Blendtree In State");

            var statePath = AssetDatabase.GetAssetPath(state);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(statePath);
            if (controller)
            {
                AssetDatabase.AddObjectToAsset(newTree, controller);
                newTree.hideFlags = HideFlags.HideInHierarchy;

            }
            else
            {
                var folderPath = Path.GetDirectoryName(statePath);

                var fileName = name;
                if (string.IsNullOrEmpty(fileName))
                    if (string.IsNullOrEmpty(fileName = state.name))
                        fileName = "Blendtree";

                AssetDatabase.CreateAsset(newTree, ReadyAssetPath(folderPath, $"{fileName}.blendtree", true));

            }
            state.motion = newTree;
            EditorUtility.SetDirty(state);
            return newTree;
        }

        internal static void Iteratetransitions(this AnimatorStateMachine machine, Func<AnimatorTransitionBase, bool> transitionAction, bool deep = true)
        {
            if (machine.entryTransitions.Any(t => transitionAction(t))) return;
            if (machine.anyStateTransitions.Any(t => transitionAction(t))) return;
            if (machine.states.SelectMany(cs => cs.state.transitions).Any(t => transitionAction(t))) return;
            if (machine.stateMachines.SelectMany(cm => machine.GetStateMachineTransitions(cm.stateMachine)).Any(t => transitionAction(t))) return;

            if (!deep) return;
            foreach (var cm in machine.stateMachines)
                if (cm.stateMachine != machine)
                    cm.stateMachine.Iteratetransitions(transitionAction);
        }

        internal static void IterateTreeChildren(this BlendTree tree, Func<ChildMotion, ChildMotion> func, bool deep = true, bool undo = false)
        {
            if (undo) Undo.RecordObject(tree, "IterateTreeUndo");
            var children = tree.children;
            for (var i = 0; i < children.Length; i++)
            {
                if (deep)
                {
                    var tree2 = children[i].motion as BlendTree;
                    if (tree2 != null) tree2.IterateTreeChildren(func);
                    else children[i] = func(children[i]);
                } else children[i] = func(children[i]);
            }
            
            tree.children = children;
        }

        #endregion

        #region Animation Stuff

        public static (float, AnimationClip)[] KeyFrameSplitClip(AnimationClip clip)
        {
            var floatBinds = AnimationUtility.GetCurveBindings(clip);
            var floatCurves = floatBinds.Select(bind => AnimationUtility.GetEditorCurve(clip, bind)).ToArray();

            var objectBinds = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            var objectCurves = objectBinds.Select(bind => AnimationUtility.GetObjectReferenceCurve(clip, bind)).ToArray();

            var usedTimes = floatCurves.SelectMany(c => c.keys.Select(k => k.time))
                .Concat(objectCurves.SelectMany(c => c.Select(k => k.time))).Distinct().ToArray();

            var clipKeyFrames = new (float, AnimationClip)[usedTimes.Length];
            for (var i = 0; i < usedTimes.Length; i++)
            {
                var time = usedTimes[i];
                var newClip = new AnimationClip {name = $"{clip.name}_{i}"};

                for (var j = 0; j < floatBinds.Length; j++)
                {
                    var bind = floatBinds[j];
                    var curve = floatCurves[j];
                    newClip.SetCurve(bind.path, bind.type, bind.propertyName, new AnimationCurve(new Keyframe(0, curve.Evaluate(time))));
                }

                for (int j = 0; j < objectBinds.Length; j++)
                {
                    var bind = objectBinds[j];
                    var keyArray = objectCurves[j];
                    Object objectValue;

                    //cringe code
                    if (keyArray.GetIndexOf(k => Mathf.Approximately(k.time, time), out var exactIndex))
                        objectValue = keyArray[exactIndex].value;
                    else if (keyArray.GetIndexOf(k => k.time > time, out var higherIndex))
                        objectValue = keyArray[higherIndex - 1].value;
                    else objectValue = keyArray[^1].value;

                    AnimationUtility.SetObjectReferenceCurve(newClip, bind, new[] {new ObjectReferenceKeyframe {time = time, value = objectValue}});
                }

                clipKeyFrames[i] = (time/clip.length, newClip);
            }

            return clipKeyFrames;
        }


        public static bool IsConstant(Motion m) => IsConstant(m as AnimationClip) && IsConstant(m as BlendTree);

        private static bool IsConstant(AnimationClip clip)
        {
            if (!clip) return true;

            var allCurves = AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)).ToArray();

            foreach (var c in allCurves)
            {
                var floatCurve = AnimationUtility.GetEditorCurve(clip, c);
                var objectCurve = AnimationUtility.GetObjectReferenceCurve(clip, c);
                var isFloatCurve = floatCurve != null;

                var oneStartKey = (isFloatCurve && floatCurve.keys.Length == 1 && floatCurve.keys[0].time == 0) || (!isFloatCurve && objectCurve.Length <= 1 && objectCurve[0].time == 0);
                if (oneStartKey)
                    continue;

                if (isFloatCurve)
                {
                    var v1 = floatCurve.keys[0].value;
                    var t1 = floatCurve.keys[0].time;
                    var j = 1;
                    for (; j < floatCurve.keys.Length; j++)
                    {
                        var t2 = floatCurve.keys[j].time;
                        if (!Mathf.Approximately(floatCurve.keys[j].value, v1) || !Mathf.Approximately(floatCurve.Evaluate((t1 + t2) / 2f), v1))
                            return false;
                        t1 = t2;
                    }
                }
                else
                {
                    var v = objectCurve[0].value;
                    if (objectCurve.Any(o => o.value != v))
                        return false;
                }
            }
            return true;
        }

        private static bool IsConstant(BlendTree tree)
        {
            if (!tree) return true;
            var isConstant = true;
            tree.IterateTreeChildren(cm =>
            {
                if (isConstant && cm.motion is AnimationClip clip)
                    isConstant &= IsConstant(clip);
                return cm;
            });
            return isConstant;
        }
        #endregion

        #region Asset Stuff

        private static T CopyAssetAndReturn<T>(T obj, string newPath) where T : Object
        {
            var assetPath = AssetDatabase.GetAssetPath(obj);
            var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);

            if (!mainAsset) return null;
            if (obj != mainAsset)
            {
                var newAsset = Object.Instantiate(obj);
                AssetDatabase.CreateAsset(newAsset, newPath);
                return newAsset;
            }

            AssetDatabase.CopyAsset(assetPath, newPath);
            return AssetDatabase.LoadAssetAtPath<T>(newPath);
        }

        public static T Duplicate<T>(T obj) where T : Object => CopyAssetAndReturn(obj, ReadyAssetPath(obj));

        #endregion

        #region General Stuff
        internal static bool GetIndexOf<T>(this IEnumerable<T> collection, Func<T, bool> predicate, out int index)
        {
            index = -1;
            using var enumerator = collection.GetEnumerator();
            while (enumerator.MoveNext())
            {
                index++;
                if (predicate(enumerator.Current))
                    return true;
            }
            return false;
        }

        internal static int GetBoolState(IEnumerable<bool> boolCollection, int defaultState = 0)
        {
            var finalState = -1;
            using (var enumerator = boolCollection.GetEnumerator())
                while (enumerator.MoveNext())
                {
                   switch (finalState)
                   {
                        case -1:
                            finalState = enumerator.Current ? 1 : 0;
                            break;
                        case 0:
                            if (enumerator.Current) return 2;
                            break;
                        case 1:
                            if (!enumerator.Current) return 2;
                            break;
                   }
                }

            return finalState == -1 ? defaultState : finalState;
        }

        internal static bool ToggleBoolState(ref int boolstate)
        {
            switch (boolstate)
            {
                case 0:
                    boolstate = 1;
                    return true;
                default:
                    boolstate = 0;
                    return false;
            }
        }

        #endregion
    }
}
