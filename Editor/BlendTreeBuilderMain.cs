using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using static Editor.BlendTreeBuilderHelper;

namespace Editor
{
    public class BlendTreeBuilderMain
    {
        private const string LayerAnystateIdentifier = "BTB_MasterTree";
        private const string LayerNameIdentifier = "BTB/MasterTree";
        private const string WeightoneParameterName = "BTB/One";
        public static BlendTree GetMasterBlendTree(VRCAvatarDescriptor avi)
        {
            var fx = avi.GetPlayableLayer(VRCAvatarDescriptor.AnimLayerType.FX);
            return fx ? GetMasterBlendTree(fx) : null;
        }
        public static BlendTree GetMasterBlendTree(AnimatorController controller)
        {
            foreach (var l in controller.layers)
            {
                if (!l.stateMachine)
                {
                    RedLog($"WARNING! {l.name} in {controller.name} has a blank graph (null statemachine)! This will cause problems.");
                    continue;
                }

                if (l.name != LayerNameIdentifier && !l.stateMachine.anyStateTransitions.Any(t => t && t.isExit && t.mute && t.name == LayerAnystateIdentifier)) continue;

                var s = l.stateMachine.defaultState;
                if (!s) continue;

                var tree = s.motion as BlendTree;
                if (tree) return tree;
            }
            return null;
        }

        public static BlendTree GenerateMasterBlendTree(VRCAvatarDescriptor avi)
        {
            var fx = avi.GetPlayableLayer(VRCAvatarDescriptor.AnimLayerType.FX);
            if (fx) return GenerateMasterBlendTree(fx);

            RedLog("No FX Controller found on target Avatar!");
            return null;

        }
        public static BlendTree GenerateMasterBlendTree(AnimatorController con)
        {
            con.ReadyParameter(WeightoneParameterName, AnimatorControllerParameterType.Float, 1);

            var m = con.AddLayer(con.MakeUniqueLayerName("BTB/MasterTree"), 1).stateMachine;
            var s = m.AddState("Master BlendTree (WD On)", new Vector3(30, 160));
            var tree = s.CreateBlendTreeInState($"{con.name} MasterTree");
            tree.blendType = BlendTreeType.Direct;

            var identifierTransition = m.AddAnyStateTransition((AnimatorState)null);
            identifierTransition.mute = true;
            identifierTransition.isExit = true;
            identifierTransition.name = LayerAnystateIdentifier;

            return tree;
        }

        public static BlendTree GetOrGenerateMasterBlendTree(VRCAvatarDescriptor avi) => GetMasterBlendTree(avi) ?? GenerateMasterBlendTree(avi);
        public static BlendTree GetOrGenerateMasterBlendTree(AnimatorController con) => GetMasterBlendTree(con) ?? GenerateMasterBlendTree(con);

        public static OptimizationInfo GetOptimizationInfo(AnimatorController con)
        {
            var info = new OptimizationInfo() { TargetController = con, MasterTree = GetMasterBlendTree(con) };
            for (var i = 0; i < con.layers.Length; i++)
            {
                if (con.layers[i].name == LayerNameIdentifier || con.layers[i].stateMachine.anyStateTransitions.Any(t => t && t.isExit && t.mute && t.name == LayerAnystateIdentifier)) continue;
                if (OptimizeBranch.TryExtract(con, i, out var b))
                    info.Add(b);
            }

            return info;
        }
        public static void ApplyOptimization(OptimizationInfo info)
        {
            var con = info.TargetController;
            if (!con) throw new NullReferenceException("Optimization target controller cannot be null!");
            var folderPath = $"{BlendTreeBuilderWindow.GeneratedAssetsPath}/{con.name}";

            Undo.RecordObject(info.TargetController, "Apply DBT Optimization");
            var masterTree = info.MasterTree ?? GetOrGenerateMasterBlendTree(info.TargetController);
            Undo.RecordObject(masterTree, "Apply DBT Optimization");

            var parameters = con.parameters;
            for (var i = info.Count - 1; i >= 0; i--)
            {
                var optBranch = info[i];
                if (!optBranch.IsActive) continue;
                if (optBranch.IsReplacing && optBranch.LinkedLayer != null)
                {
                    var l = optBranch.LinkedLayer;
                    if (con.layers.GetIndexOf(l2 => l.stateMachine == l2.stateMachine, out var index))
                    {
                        Debug.Log($"Removed Layer: {l.name}");
                        con.RemoveLayer(index);
                    }
                    else RedLog($"Couldn't find Layer to remove associated with {optBranch.BaseBranch.Name}!");
                }

                if (optBranch.IsMotionTimed)
                {
                    var clip = optBranch.BaseBranch.ChildMotions[0].motion as AnimationClip;
                    if (!clip) continue;

                    var clipKeyFrames = KeyFrameSplitClip(clip);

                    try
                    {
                        AssetDatabase.StartAssetEditing();
                        for (var j = 0; j < clipKeyFrames.Length; j++)
                        {
                            var clipPath = ReadyAssetPath(folderPath, $"{clipKeyFrames[j].Item2.name}.anim", true);
                            AssetDatabase.CreateAsset(clipKeyFrames[j].Item2, clipPath);
                        }
                    }
                    finally { AssetDatabase.StopAssetEditing(); }

                    var newChildren = new ChildMotion[clipKeyFrames.Length];
                    for (var j = 0; j < clipKeyFrames.Length; j++)
                    {
                        newChildren[j] = new ChildMotion()
                        {
                            threshold = clipKeyFrames[j].Item1,
                            motion = clipKeyFrames[j].Item2,
                            timeScale = 1
                        };
                    }

                    optBranch.BaseBranch.ChildMotions = newChildren;
                }

                AppendBranch(masterTree, optBranch);

                if (parameters.GetIndexOf(p => p.name == optBranch.BaseBranch.Parameter, out var paramIndex)) 
                    parameters[paramIndex].type = AnimatorControllerParameterType.Float;
                else con.ReadyParameter(optBranch.BaseBranch.Parameter, AnimatorControllerParameterType.Float, 0);

            }
            con.parameters = parameters;

            FillTreeWithEmpty(masterTree);

           // BlendTreeBuilderMenuItem.FixTreeSpeed(masterTree, false);

            GreenLog("Successfully applied optimization!");
        }

        public static void AppendBranch(BlendTree targetTree, Branch branch)
        {
            Motion finalMotion;
            if (branch.ChildMotions.Length == 1)
                finalMotion = branch.ChildMotions[0].motion;
            else
            {
                var newTree = new BlendTree()
                {
                    useAutomaticThresholds = false,
                    name = branch.Name,
                    blendParameter = branch.Parameter,
                    children = branch.ChildMotions
                };
                CreateBlendTreeAsset(targetTree, newTree);
                finalMotion = newTree;
            }

            var children = targetTree.children;
            var newChild = new ChildMotion()
            {
                directBlendParameter = WeightoneParameterName,
                motion = finalMotion,
                timeScale = 1
            };

            ArrayUtility.Add(ref children, newChild);
            targetTree.children = children;

        }

        public static void CreateBlendTreeAsset(BlendTree associatedTree, BlendTree assetTree, string name = "")
        {
            Undo.RegisterCreatedObjectUndo(assetTree, "Create Blendtree Asset");
            var treePath = AssetDatabase.GetAssetPath(associatedTree);
            var con = AssetDatabase.LoadAssetAtPath<AnimatorController>(treePath);
            if (con)
            {
                AssetDatabase.AddObjectToAsset(assetTree, con);
                assetTree.hideFlags = HideFlags.HideInHierarchy;
            }
            else
            {
                var folderPath = Path.GetDirectoryName(treePath);

                var fileName = name;
                if (string.IsNullOrEmpty(fileName))
                    if (string.IsNullOrEmpty(fileName = assetTree.name))
                        fileName = "Blendtree";

                AssetDatabase.CreateAsset(assetTree, ReadyAssetPath(folderPath, $"{fileName}.blendtree", true));
            }
        }

        public static void FillTreeWithEmpty(BlendTree mainTree)
        {
            const string folderPath = BlendTreeBuilderWindow.GeneratedAssetsPath;
            var emptyClipPath = ReadyAssetPath(folderPath,"Empty Clip.anim");
            var emptyClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(emptyClipPath);
            if (!emptyClip)
            {
                emptyClip = new AnimationClip();
                AssetDatabase.CreateAsset(emptyClip, emptyClipPath);
            }

            mainTree.IterateTreeChildren(cm =>
            {
                if (!cm.motion) cm.motion = emptyClip;
                return cm;
            });

        }

        private static void RedLog(string msg)
        {
            Debug.LogError($"<color=red>[BlendTreeBuilder] {msg}</color>");
        }

        private static void GreenLog(string msg)
        {
            Debug.Log($"<color=green>[BlendTreeBuilder] {msg}</color>");
        }

    }
}
