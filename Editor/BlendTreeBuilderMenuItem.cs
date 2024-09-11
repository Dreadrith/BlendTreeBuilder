using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Editor
{
    public class BlendTreeBuilderMenuItem : EditorWindow
    {

        [MenuItem("CONTEXT/BlendTree/Fix Speed")]
        private static void FixTreeSpeedCommand(MenuCommand c) => FixTreeSpeed((BlendTree)c.context);

        [MenuItem("CONTEXT/BlendTree/Reset Speed")]
        private static void ResetTreeSpeedCommand(MenuCommand c) => ResetTreeSpeed((BlendTree)c.context);
        

        private static void ResetTreeSpeed(BlendTree tree)
        {
            tree.IterateTreeChildren(cm =>
            {
                cm.timeScale = 1;
                return cm;
            }, true, true);
        }

        public static double GetTreeLength(BlendTree tree)
        {
            double maxLength = 0;
            tree.IterateTreeChildren(cm =>
            {
                switch (cm.motion)
                {
                    case AnimationClip clip:
                    {
                        var l = Mathf.Abs(clip.length / cm.timeScale);
                        if (l > maxLength)
                            maxLength = l;
                        break;
                    }
                    case BlendTree subtree:
                    {
                        var l = subtree.blendType == BlendTreeType.Direct ? FixTreeSpeed(subtree) : GetTreeLength(subtree);
                        if (l > maxLength)
                            maxLength = l;
                        break;
                    }
                }

                return cm;
            }, false, false);
            return maxLength;
        }

        public static void MultiplyTreeSpeed(BlendTree tree, float multiplier)
        {
            tree.IterateTreeChildren(cm =>
            {
                cm.timeScale *= multiplier;
                return cm;
            },true, true);
        }

        public static double FixTreeSpeed(BlendTree tree, bool undo = true)
        {
            var lengths = new List<double>();
            tree.IterateTreeChildren(cm =>
            {
                switch (cm.motion)
                {
                    case AnimationClip clip:
                        lengths.Add(Mathf.Abs(clip.length / cm.timeScale));
                        break;
                    case BlendTree { blendType: BlendTreeType.Direct } subTree:
                        lengths.Add(FixTreeSpeed(subTree));
                        break;
                    case BlendTree subTree:
                        lengths.Add(GetTreeLength(subTree));
                        break;
                }

                return cm;
            }, false);


            var newSpeeds = GetSpeeds(lengths.ToArray());
            var index = 0;

            tree.IterateTreeChildren(cm =>
            {
                switch (cm.motion)
                {
                    case AnimationClip:
                        cm.timeScale = (float)newSpeeds[index++] * (cm.timeScale < 0 ? -1 : 1);
                        break;
                    case BlendTree subTree:
                        MultiplyTreeSpeed(subTree, (float) newSpeeds[index++]);
                        break;
                }

                return cm;
            }, false, undo);

            return lengths.Any() ? lengths.Sum() : 0;
        }

        //Big thanks and credit to jellejurre#8585
        #region Fix Tree Speed Math
        public static double[] GetSpeeds(double[] lengths)
        {
            var speeds = Enumerable.Repeat(1.0, lengths.Length).ToArray();
            
            var newSpeeds = Iterate(speeds, lengths);
            while (GetError(speeds, newSpeeds) > 0.0000001)
            {
                speeds = newSpeeds;
                newSpeeds = Iterate(speeds, lengths);
            }

            return newSpeeds;
        }

        public static double[] Iterate(double[] speeds, double[] lengths)
        {
            var newSpeeds = new double[speeds.Length];
            for (var i = 0; i < speeds.Length; i++)
            {
                double currentSpeed = 0;

                for (var j = 0; j < speeds.Length; j++)
                {
                    if (i > j)
                    {
                        if (newSpeeds[j] != 0 && lengths[i] != 0)
                            currentSpeed += lengths[j] / newSpeeds[j] / lengths[i];
                    }
                    if (i == j)
                    {
                        currentSpeed += 1;
                    }

                    if (i >= j) continue;
                    if (speeds[j] != 0 && lengths[i] != 0)
                        currentSpeed += lengths[j] / speeds[j] / lengths[i];
                }

                newSpeeds[i] = currentSpeed;
            }
            return newSpeeds;
        }

        public static double GetError(double[] a1, double[] a2)
        {
            return a1.Select((t, i) => Math.Abs(t - a2[i])).Sum();
        }
        #endregion
    }
}
