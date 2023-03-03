using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using static DreadScripts.BlendTreeBulder.BlendTreeBuilderHelper;

namespace DreadScripts.BlendTreeBulder
{
    public class BlendTreeBuilderMain
    {
        private const string LAYER_ANYSTATE_IDENTIFIER = "BTB_MasterTree";
        private const string LAYER_NAME_IDENTIFIER = "BTB/MasterTree";
        private const string WEIGHTONE_PARAMETER_NAME = "BTB/One";
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

                if (l.name != LAYER_NAME_IDENTIFIER && !l.stateMachine.anyStateTransitions.Any(t => t && t.isExit && t.mute && t.name == LAYER_ANYSTATE_IDENTIFIER)) continue;

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
            con.ReadyParameter(WEIGHTONE_PARAMETER_NAME, AnimatorControllerParameterType.Float, 1);

            var m = con.AddLayer(con.MakeUniqueLayerName("BTB/MasterTree"), 1).stateMachine;
            var s = m.AddState("Master BlendTree (WD On)", new Vector3(50, 160));
            var tree = s.CreateBlendTreeInState($"{con.name} MasterTree");
            tree.blendType = BlendTreeType.Direct;

            var identifierTransition = m.AddAnyStateTransition((AnimatorState)null);
            identifierTransition.mute = true;
            identifierTransition.isExit = true;
            identifierTransition.name = LAYER_ANYSTATE_IDENTIFIER;

            return tree;
        }

        public static BlendTree GetOrGenerateMasterBlendTree(VRCAvatarDescriptor avi) => GetMasterBlendTree(avi) ?? GenerateMasterBlendTree(avi);
        public static BlendTree GetOrGenerateMasterBlendTree(AnimatorController con) => GetMasterBlendTree(con) ?? GenerateMasterBlendTree(con);

        public static OptimizationInfo GetOptimizationInfo(AnimatorController con)
        {
            OptimizationInfo info = new OptimizationInfo() { targetController = con, masterTree = GetMasterBlendTree(con) };
            for (int i = 0; i < con.layers.Length; i++)
            {
                if (con.layers[i].name == LAYER_NAME_IDENTIFIER || con.layers[i].stateMachine.anyStateTransitions.Any(t => t && t.isExit && t.mute && t.name == LAYER_ANYSTATE_IDENTIFIER)) continue;
                if (OptimizeBranch.TryExtract(con, i, out OptimizeBranch b))
                    info.Add(b);
            }

            return info;
        }
        public static void ApplyOptimization(OptimizationInfo info)
        {
            var con = info.targetController;
            if (!con) throw new NullReferenceException("Optimization target controller cannot be null!");
            var folderPath = $"{BlendTreeBuilderWindow.GENERATED_ASSETS_PATH}/{con.name}";

            Undo.RecordObject(info.targetController, "Apply DBT Optimization");
            var masterTree = info.masterTree ?? GetOrGenerateMasterBlendTree(info.targetController);
            Undo.RecordObject(masterTree, "Apply DBT Optimization");

            var parameters = con.parameters;
            for (int i = info.Count - 1; i >= 0; i--)
            {
                var optBranch = info[i];
                if (!optBranch.isActive) continue;
                if (optBranch.isReplacing && optBranch.linkedLayer != null)
                {
                    var l = optBranch.linkedLayer;
                    if (con.layers.GetIndexOf(l2 => l.stateMachine == l2.stateMachine, out int index))
                    {
                        Debug.Log($"Removed Layer: {l.name}");
                        con.RemoveLayer(index);
                    }
                    else RedLog($"Couldn't find Layer to remove associated with {optBranch.baseBranch.name}!");
                }

                if (optBranch.isMotionTimed)
                {
                    var clip = optBranch.baseBranch.childMotions[0].motion as AnimationClip;
                    if (!clip) continue;

                    var clipKeyFrames = KeyFrameSplitClip(clip);

                    try
                    {
                        AssetDatabase.StartAssetEditing();
                        for (int j = 0; j < clipKeyFrames.Length; j++)
                        {
                            var clipPath = ReadyAssetPath(folderPath, $"{clipKeyFrames[j].Item2.name}.anim", true);
                            AssetDatabase.CreateAsset(clipKeyFrames[j].Item2, clipPath);
                        }
                    }
                    finally { AssetDatabase.StopAssetEditing(); }

                    ChildMotion[] newChildren = new ChildMotion[clipKeyFrames.Length];
                    for (int j = 0; j < clipKeyFrames.Length; j++)
                    {
                        newChildren[j] = new ChildMotion()
                        {
                            threshold = clipKeyFrames[j].Item1,
                            motion = clipKeyFrames[j].Item2,
                            timeScale = 1
                        };
                    }

                    optBranch.baseBranch.childMotions = newChildren;
                }

                AppendBranch(masterTree, optBranch);

                if (parameters.GetIndexOf(p => p.name == optBranch.baseBranch.parameter, out int paramIndex)) 
                    parameters[paramIndex].type = AnimatorControllerParameterType.Float;
                else con.ReadyParameter(optBranch.baseBranch.parameter, AnimatorControllerParameterType.Float, 0);

            }
            con.parameters = parameters;

            FillTreeWithEmpty(masterTree);

            BlendTreeBuilderMenuItem.FixTreeSpeed(masterTree, false);

            GreenLog("Successfully applied optimization!");
        }

        public static void AppendBranch(BlendTree targetTree, Branch branch)
        {
            Motion finalMotion;
            if (branch.childMotions.Length == 1)
                finalMotion = branch.childMotions[0].motion;
            else
            {
                BlendTree newTree = new BlendTree()
                {
                    name = branch.name,
                    blendParameter = branch.parameter,
                    children = branch.childMotions
                };
                CreateBlendTreeAsset(targetTree, newTree);
                finalMotion = newTree;
            }

            ChildMotion[] children = targetTree.children;
            ChildMotion newChild = new ChildMotion()
            {
                directBlendParameter = WEIGHTONE_PARAMETER_NAME,
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
            const string folderPath = BlendTreeBuilderWindow.GENERATED_ASSETS_PATH;
            var emptyClipPath = ReadyAssetPath(folderPath,"Empty Clip.anim", false);
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
