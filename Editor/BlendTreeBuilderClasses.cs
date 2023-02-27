using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor.Animations;
using UnityEngine;
using static DreadScripts.BlendTreeBulder.BlendTreeBuilderHelper;

namespace DreadScripts.BlendTreeBulder
{
    public class Branch
    {
        public string name;
        public string parameter;
        public Motion startMotion;
        public float startSpeed;

        public Motion endMotion;
        public float endSpeed;

        public BlendTree linkedTree;

        public static bool TryExtract(AnimatorControllerLayer l, out OptimizeBranch branch)
        {
            bool mayChangeBehaviour = false;
            branch = null;
            if (!l.stateMachine) return false;

            string name = l.name;
            string parameter = string.Empty;

            switch (l.stateMachine.states.Length)
            {
                case 0: return false;
                case 1:
                    //Check and handle layers with only one state with motion time OR just a regular blendtree
                    //Fail if no motion. If motion is blendtree, branch is just a childmotion with this blendtree.
                    //Split clip to many generated clips, one per frame. Where to save them? Dunno, Assets like the others?
                    //End leaf tree is 1D tree with the parameter of motion time
                    //Generated clips are set as child motions with their normalized time on og clip as their threshold
                    //This has the side-effect of forcing all blending to be linear, which is the usual anyway.
                    return false;
                default:
                {
                    AnimatorState startState = null;
                    float startSpeed = 0;
                    AnimatorState endState = null;
                    float endSpeed = 0;

                    bool success = false;

                    // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                    //This is needed to set success to false and have cleaner code
                    bool Failure() => !(success = false);

                    //Iteration stops when anything returns true
                    l.stateMachine.Iteratetransitions(t =>
                    {
                        if (t.destinationStateMachine) return Failure();
                        if (!t.conditions.Any()) return Failure();
                        if (builtinParameters.Contains(t.conditions[0].parameter)) return Failure();

                        for (int i = 1; i < t.conditions.Length; i++)
                        {
                            if (!Regex.Match(t.conditions[i].parameter, @"^(?i)isloaded").Success && !Regex.Match(t.conditions[i].parameter, @"^(?i)hasloaded").Success)
                                return Failure();
                        }


                        if (t.destinationState)
                        {
                            var c = t.conditions[0];
                            if (!string.IsNullOrEmpty(parameter) && parameter != c.parameter) return Failure();
                            parameter = c.parameter;

                            bool standardizedCondition = c.mode == AnimatorConditionMode.If
                                                         || (c.mode == AnimatorConditionMode.Equals && c.threshold == 1)
                                                         || (c.mode == AnimatorConditionMode.Greater && c.threshold > Mathf.Epsilon);

                            if (standardizedCondition)
                            {
                                if (endState != null && endState != t.destinationState) return Failure();
                                endState = t.destinationState;
                                endSpeed = t.destinationState.speed;
                                if (endSpeed == 0) endSpeed = -1;
                            }
                            else
                            {
                                if (startState != null && startState != t.destinationState) return Failure();
                                startState = t.destinationState;
                                startSpeed = t.destinationState.speed;
                                if (startSpeed == 0) startSpeed = -1;
                            }
                        }

                        if (!mayChangeBehaviour && t is AnimatorStateTransition t2 && ((t2.hasExitTime && t2.exitTime > 0) || t2.duration > 0 || t2.offset > 0))
                            mayChangeBehaviour = true;
                        

                        if (!success && !string.IsNullOrEmpty(parameter) && startState && endState) success = true;

                        return false;
                    });
                    if (!success) return false;

                    var baseBranch = new Branch() {name = name, parameter = parameter, startMotion = startState.motion, endMotion = endState.motion, startSpeed = startSpeed, endSpeed = endSpeed};
                    branch = new OptimizeBranch(baseBranch, l) {mayChangeBehaviour = mayChangeBehaviour};
                    return true;

                }
            }

        }
    }

    public class OptimizeBranch
    {
        public Branch baseBranch;
        public bool isActive = true;
        public bool isReplacing = true;
        public bool foldout;
        public bool mayChangeBehaviour;
        public AnimatorControllerLayer linkedLayer;
        public List<AnimatorControllerLayer> reuseLayers;

        public OptimizeBranch(Branch branch, AnimatorControllerLayer linkedLayer = null)
        {
            baseBranch = branch;
            this.linkedLayer = linkedLayer;
        }

        public static implicit operator Branch(OptimizeBranch b) => b.baseBranch;
    }

    public class OptimizationInfo
    {
        public AnimatorController targetController;
        public BlendTree masterTree;
        public readonly List<OptimizeBranch> optBranches = new List<OptimizeBranch>();
        public int Count => optBranches.Count;


        public void Add(OptimizeBranch branch)
            => optBranches.Add(branch);

        public OptimizeBranch this[int i] => optBranches[i];
    }
}
