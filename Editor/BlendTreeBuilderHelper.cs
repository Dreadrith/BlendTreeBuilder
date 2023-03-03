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

namespace DreadScripts.BlendTreeBulder
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
            bool forceFolder = pathOption == PathOption.ForceFolder;
            bool forceFile = pathOption == PathOption.ForceFile;

            path = forceFile ? LegalizeName(path) : forceFolder ? LegalizePath(path) : LegalizeFullPath(path);
            bool isFolder = forceFolder || (!forceFile && string.IsNullOrEmpty(Path.GetExtension(path)));

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
                string folderPath = Path.GetDirectoryName(path);
                string fileName = Path.GetFileName(path);

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
            if (string.IsNullOrEmpty(folderPath))
                return ReadyAssetPath(LegalizeName(fileName), makeUnique, PathOption.ForceFile);

            return ReadyAssetPath($"{LegalizePath(folderPath)}/{LegalizeName(fileName)}", makeUnique);
        }

        internal static string ReadyAssetPath(Object buddyAsset, string fullName = "", bool makeUnique = true)
        {
            var buddyPath = AssetDatabase.GetAssetPath(buddyAsset);
            string folderPath = Path.GetDirectoryName(buddyPath);
            if (string.IsNullOrEmpty(fullName))
                fullName = Path.GetFileName(buddyPath);
            return ReadyAssetPath(folderPath, fullName, makeUnique);
        }

        internal static string LegalizeFullPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("Legalizing empty path! Returned path as 'EmptyPath'");
                return "EmptyPath";
            }

            var ext = Path.GetExtension(path);
            bool isFolder = string.IsNullOrEmpty(ext);
            if (isFolder) return LegalizePath(path);

            string folderPath = Path.GetDirectoryName(path);
            var fileName = LegalizeName(Path.GetFileNameWithoutExtension(path));

            if (string.IsNullOrEmpty(folderPath)) return $"{fileName}{ext}";
            folderPath = LegalizePath(folderPath);

            return $"{folderPath}/{fileName}{ext}";
        }
        internal static string LegalizePath(string path)
        {
            string regexFolderReplace = Regex.Escape(new string(Path.GetInvalidPathChars()));

            path = path.Replace('\\', '/');
            if (path.IndexOf('/') > 0)
                path = string.Join("/", path.Split('/').Select(s => Regex.Replace(s, $@"[{regexFolderReplace}]", "-")));

            return path;

        }
        internal static string LegalizeName(string name)
        {
            string regexFileReplace = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            return string.IsNullOrEmpty(name) ? "Unnamed" : Regex.Replace(name, $@"[{regexFileReplace}]", "-");
        }
        #endregion

        #region VRC Stuff
        internal static readonly string[] builtinParameters = new string[] {
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
        internal static bool SetPlayableLayer(this VRCAvatarDescriptor avatar, VRCAvatarDescriptor.AnimLayerType type, RuntimeAnimatorController controller)
        {
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

            return SetPlayableLayerInternal(avatar.baseAnimationLayers) || SetPlayableLayerInternal(avatar.specialAnimationLayers);
        }
        internal static AnimatorController ReadyPlayableLayer(this VRCAvatarDescriptor avatar, VRCAvatarDescriptor.AnimLayerType type, string folderPath)
        {
            AnimatorController controller = avatar.GetPlayableLayer(type);
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
            VRCExpressionParameters parameters = avatar.expressionParameters;
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
                if (p.name == parameter)
                {
                    if (p.type != type)
                        Debug.LogWarning($"Type mismatch! Parameter {parameter} already exists in {controller.name} but with type {p.type} rather than {type}");
                    return;
                }
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
            BlendTree newTree = new BlendTree() { name = name };
            Undo.RecordObject(state, "Create Blendtree In State");
            Undo.RegisterCreatedObjectUndo(newTree, "Create Blendtree In State");

            string statePath = AssetDatabase.GetAssetPath(state);
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

        internal static void Iteratetransitions(this AnimatorStateMachine machine, System.Func<AnimatorTransitionBase, bool> transitionAction, bool deep = true)
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

        internal static void IterateTreeChildren(this BlendTree tree, System.Func<ChildMotion, ChildMotion> func, bool deep = true, bool undo = false)
        {
            if (undo) Undo.RecordObject(tree, "IterateTreeUndo");
            ChildMotion[] children = tree.children;
            for (int i = 0; i < children.Length; i++)
            {
                if (deep)
                {
                    var tree2 = children[i].motion as BlendTree;
                    if (tree2 != null) tree2.IterateTreeChildren(func, true);
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

            float[] usedTimes = floatCurves.SelectMany(c => c.keys.Select(k => k.time))
                .Concat(objectCurves.SelectMany(c => c.Select(k => k.time))).Distinct().ToArray();

            (float, AnimationClip)[] clipKeyFrames = new (float, AnimationClip)[usedTimes.Length];
            for (int i = 0; i < usedTimes.Length; i++)
            {
                var time = usedTimes[i];
                var newClip = new AnimationClip(){name = $"{clip.name}_{i}"};

                for (int j = 0; j < floatBinds.Length; j++)
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
                    if (keyArray.GetIndexOf(k => k.time == time, out int exactIndex))
                        objectValue = keyArray[exactIndex].value;
                    else if (keyArray.GetIndexOf(k => k.time > time, out int higherIndex))
                        objectValue = keyArray[higherIndex - 1].value;
                    else objectValue = keyArray[keyArray.Length - 1].value;

                    AnimationUtility.SetObjectReferenceCurve(newClip, bind, new ObjectReferenceKeyframe[] {new ObjectReferenceKeyframe() {time = time, value = objectValue}});
                }

                clipKeyFrames[i] = (time/clip.length, newClip);
            }

            return clipKeyFrames;
        }


        public static bool IsConstant(Motion m) => IsConstant(m as AnimationClip) && IsConstant(m as BlendTree);
        public static bool IsConstant(AnimationClip clip)
        {
            if (!clip) return true;

            EditorCurveBinding[] allCurves = AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)).ToArray();

            for (int i = 0; i < allCurves.Length; i++)
            {
                EditorCurveBinding c = allCurves[i];

                AnimationCurve floatCurve = AnimationUtility.GetEditorCurve(clip, c);
                ObjectReferenceKeyframe[] objectCurve = AnimationUtility.GetObjectReferenceCurve(clip, c);
                bool isFloatCurve = floatCurve != null;

                bool oneStartKey = (isFloatCurve && floatCurve.keys.Length == 1 && floatCurve.keys[0].time == 0) || (!isFloatCurve && objectCurve.Length <= 1 && objectCurve[0].time == 0);
                if (oneStartKey)
                    continue;

                if (isFloatCurve)
                {
                    float v1 = floatCurve.keys[0].value;
                    float t1 = floatCurve.keys[0].time;
                    for (int j = 1; j < floatCurve.keys.Length; j++)
                    {
                        var t2 = floatCurve.keys[j].time;
                        if (floatCurve.keys[j].value != v1 || floatCurve.Evaluate((t1 + t2) / 2f) != v1)
                            return false;
                        t1 = t2;
                    }
                }
                else
                {
                    Object v = objectCurve[0].value;
                    if (objectCurve.Any(o => o.value != v))
                        return false;
                }

            }
            return true;
        }

        public static bool IsConstant(BlendTree tree)
        {
            if (!tree) return true;
            bool isConstant = true;
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
        public static T CopyAssetAndReturn<T>(T obj, string newPath) where T : Object
        {
            string assetPath = AssetDatabase.GetAssetPath(obj);
            Object mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);

            if (!mainAsset) return null;
            if (obj != mainAsset)
            {
                T newAsset = Object.Instantiate(obj);
                AssetDatabase.CreateAsset(newAsset, newPath);
                return newAsset;
            }

            AssetDatabase.CopyAsset(assetPath, newPath);
            return AssetDatabase.LoadAssetAtPath<T>(newPath);
        }

        public static T Duplicate<T>(T obj) where T : Object => CopyAssetAndReturn(obj, ReadyAssetPath(obj));
        public static void MarkDirty(this Object obj) => EditorUtility.SetDirty(obj);
        #endregion

        #region General Stuff
        internal static bool GetIndexOf<T>(this IEnumerable<T> collection, System.Func<T, bool> predicate, out int index)
        {
            index = -1;
            using (var enumerator = collection.GetEnumerator())
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
            int finalState = -1;
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


        internal static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> kvp, out TKey key, out TValue value)
        {
            key = kvp.Key;
            value = kvp.Value;
        }

        #endregion
    }
}
