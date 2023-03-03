using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Linq;
using static DreadScripts.BlendTreeBulder.BlendTreeBuilderHelper;

namespace DreadScripts.BlendTreeBulder
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
                if (cm.motion is AnimationClip clip)
                {
                    var l = Mathf.Abs(clip.length / cm.timeScale);
                    if (l > maxLength)
                        maxLength = l;
                }
                else if (cm.motion is BlendTree subtree)
                {
                    var l = subtree.blendType == BlendTreeType.Direct ? FixTreeSpeed(subtree) : GetTreeLength(subtree);
                    if (l > maxLength)
                        maxLength = l;
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
            List<double> lengths = new List<double>();
            tree.IterateTreeChildren(cm =>
            {
                if (cm.motion is AnimationClip clip)
                    lengths.Add(Mathf.Abs(clip.length / cm.timeScale));
                else if (cm.motion is BlendTree subTree)
                {
                    if (subTree.blendType == BlendTreeType.Direct)
                        lengths.Add(FixTreeSpeed(subTree));
                    else lengths.Add(GetTreeLength(subTree));
                }
                
                return cm;
            }, false);


            double[] newSpeeds = GetSpeeds(lengths.ToArray());
            int index = 0;

            tree.IterateTreeChildren(cm =>
            {
                if (cm.motion is AnimationClip)
                    cm.timeScale = (float)newSpeeds[index++] * (cm.timeScale < 0 ? -1 : 1);
                else if (cm.motion is BlendTree subTree)
                    MultiplyTreeSpeed(subTree, (float) newSpeeds[index++]);
                
                return cm;
            }, false, undo);

            return lengths.Any() ? lengths.Sum() : 0;
        }

        //Big thanks and credit to jellejurre#8585
        #region Fix Tree Speed Math
        public static double[] GetSpeeds(double[] lengths)
        {
            double[] speeds = Enumerable.Repeat(1.0, lengths.Length).ToArray();
            
            double[] newSpeeds = Iterate(speeds, lengths);
            while (GetError(speeds, newSpeeds) > 0.0000001)
            {
                speeds = newSpeeds;
                newSpeeds = Iterate(speeds, lengths);
            }

            return newSpeeds;
        }

        public static double[] Iterate(double[] speeds, double[] lengths)
        {
            double[] newSpeeds = new double[speeds.Length];
            for (int i = 0; i < speeds.Length; i++)
            {
                double currentSpeed = 0;

                for (int j = 0; j < speeds.Length; j++)
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
                    if (i < j)
                    {
                        if (speeds[j] != 0 && lengths[i] != 0)
                            currentSpeed += lengths[j] / speeds[j] / lengths[i];
                    }
                }

                newSpeeds[i] = currentSpeed;
            }
            return newSpeeds;
        }

        public static double GetError(double[] a1, double[] a2)
        {
            double error = 0;
            for (int i = 0; i < a1.Length; i++)
            {
                error += Math.Abs(a1[i] - a2[i]);
            }
            return error;
        }
        #endregion
    }
}
