using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor.Animations;
using UnityEngine;
using static Editor.BlendTreeBuilderHelper;

namespace Editor
{
    public class Branch
    {
        public string Name;
        public string Parameter;
        public ChildMotion[] ChildMotions;
        
        
        //For the builder: Allow editing the tree directly through the tool window
        //public BlendTree linkedTree;
    }

    public class OptimizeBranch
    {
        public readonly Branch BaseBranch;
        public AnimatorControllerLayer LinkedLayer;
        public int LinkedLayerIndex;
        public bool IsActive = true;
        public bool IsReplacing = true;
        public bool CanEdit = true;
        public bool Foldout;
        public string InfoLog;
        public string WarnLog;
        public string ErrorLog;
        public string DisplayType;
        public bool IsMotionTimed;

        private OptimizeBranch(Branch branch)
        {
            BaseBranch = branch;
        }
        public static bool TryExtract(AnimatorController controller, int layerIndex, out OptimizeBranch optBranch)
        {
            optBranch = null;
            var layer = controller.layers[layerIndex];
            if (!layer.stateMachine) return false;

            var name = layer.name;
            var parameter = string.Empty;
            var infoReport = new StringBuilder();
            var warnReport = new StringBuilder();
            var errorReport = new StringBuilder();

            //This switch check doesn't handle layers with no states in root statemachine properly
            //I.E: When states are in sub-statemachines
            //But why do that for a simple layer like these
            switch (layer.stateMachine.states.Length)
            {
                case 0: return false;
                case 1:
                {
                    var state = layer.stateMachine.defaultState;
                    if (!state.motion) return false;

                    var hasAnyTransition = false;
                    var success = true;
                    layer.stateMachine.Iteratetransitions(t =>
                    {
                        hasAnyTransition = true;
                        return !(success = !t.destinationStateMachine && (!t.destinationState || t.destinationState == state));
                    });
                    if (!success) return false;

                    if (hasAnyTransition) errorReport.AppendLine("\n- Layer contains transitions from and/or to the only state.");
                    if (!state.timeParameterActive)
                    {
                        var baseBranch = new Branch {Name = controller.layers[layerIndex].name, ChildMotions = new[] {new ChildMotion {motion = state.motion}}};
                        optBranch = new OptimizeBranch(baseBranch) {LinkedLayer = layer, LinkedLayerIndex = layerIndex, DisplayType = "Single State"};
                        return true;
                    }

                    if (state.motion is not AnimationClip) return false;
                    {
                        var baseBranch = new Branch {Name = controller.layers[layerIndex].name, Parameter = state.timeParameter, ChildMotions = new[] {new ChildMotion {motion = state.motion}}};
                        optBranch = new OptimizeBranch(baseBranch) {LinkedLayer = layer, LinkedLayerIndex = layerIndex, DisplayType = "Motion Time State", IsMotionTimed = true, CanEdit = false};
                        return true;
                    }

                }
                default:
                {
                    var visitedStates = new HashSet<AnimatorState>();
                    var endStates = new Dictionary<float, AnimatorState>();

                    var success = false;

                    var foundBehaviours = false;
                    var foundLoopingZeroSpeed = false;
                    var foundNonInstantTransitions = false;

                    layer.stateMachine.Iteratetransitions(t =>
                    {
                        if (t.mute) return false;
                        if (t.destinationStateMachine) return Failure(out success);
                        if (!t.conditions.Any()) return Failure(out success);
                        if (BuiltinParameters.Contains(t.conditions[0].parameter)) return Failure(out success);

                        for (var i = 1; i < t.conditions.Length; i++)
                        {
                            if (!Regex.Match(t.conditions[i].parameter, "^(?i)isloaded").Success && !Regex.Match(t.conditions[i].parameter, "^(?i)hasloaded").Success)
                                return Failure(out success);
                        }


                        if (t.destinationState)
                        {
                            var c = t.conditions[0];
                            if (!string.IsNullOrEmpty(parameter) && parameter != c.parameter) return Failure(out success);
                            parameter = c.parameter;


                            var standardizedThreshold = c.threshold;
                            var dState = t.destinationState;

                            switch (c.mode)
                            {
                                case AnimatorConditionMode.NotEqual: return Failure(out success);
                                case AnimatorConditionMode.IfNot:
                                    standardizedThreshold = 0;
                                    break;
                                case AnimatorConditionMode.If:
                                    standardizedThreshold = 1;
                                    break;
                                case AnimatorConditionMode.Less:
                                case AnimatorConditionMode.Greater:
                                    warnReport.AppendLine("\n- Parameter conditions are not exact. Less & Greater conditions are not handled accurately yet.");
                                    break;
                                case AnimatorConditionMode.Equals:
                                    break;
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }

                            if (endStates.TryGetValue(standardizedThreshold, out AnimatorState endState))
                            {
                                if (endState != dState)
                                    return Failure(out success);
                            }
                            else
                            {
                                if (visitedStates.Contains(dState)) return Failure(out success);
                                endStates.Add(standardizedThreshold, dState);
                                if (!foundLoopingZeroSpeed && dState.speed == 0 && dState.motion && dState.motion.isLooping)
                                {
                                    foundLoopingZeroSpeed = true;
                                    warnReport.AppendLine("\n- Layer contains a looping motion on a state with 0 speed. Blendtrees can't have 0 speed so speed is set to -1 which may be undesirable on a looping animation.");
                                }
                            }





                            if (!foundBehaviours && t.destinationState.behaviours != null && t.destinationState.behaviours.Length > 0)
                            {
                                foundBehaviours = true;
                                errorReport.AppendLine("\n- Layer contains statemachine behaviours, which may be necessary for certain functionality, such as Exclusive Toggles.");
                            }

                        }

                        if (!foundNonInstantTransitions && t is AnimatorStateTransition t3 && ((t3.hasExitTime && t3.exitTime > 0) || t3.duration > 0 || t3.offset > 0))
                        {
                            foundNonInstantTransitions = true;
                            infoReport.AppendLine("\n- Transitions are not instant. Behaviour may change when optimized.");
                        }


                        if (!success && !string.IsNullOrEmpty(parameter) && endStates.Count >= 2) success = true;

                        return false;
                    });
                    if (!success) return false;

                    var animParameter = controller.parameters.FirstOrDefault(p => p.name == parameter);
                    if (animParameter != null)
                    {
                        if (animParameter.type != AnimatorControllerParameterType.Float)
                        {
                            var wasReused = false;
                            foreach (var layer2 in controller.layers)
                            {
                                if (layer2.stateMachine == layer.stateMachine) continue;

                                var parameterReused = false;
                                layer2.stateMachine.Iteratetransitions(t => parameterReused = t.conditions.Any(c => c.parameter == parameter));

                                if (!parameterReused) continue;
                                if (!wasReused)
                                {
                                    wasReused = true;
                                    errorReport.Append("\n- Non-Float Parameter is reused in other layers: ");
                                }

                                errorReport.Append($"[{layer2.name}]");
                            }
                        }
                    }
                    else errorReport.AppendLine($"\n- Parameter {parameter} not found in the controller!");

                    if (endStates.Values.Any(s => s.motion && s.motion.isLooping && !IsConstant(s.motion)))
                        warnReport.AppendLine("\n- Animation Clips used are not constant! Blendtrees usually will be playing the end of the clips");


                    var newChildren = new ChildMotion[endStates.Count];
                    var currentIndex = 0;
                    foreach (var (threshold, state) in endStates)
                    {
                        newChildren[currentIndex++] = new ChildMotion
                        {
                            motion = state.motion,
                            timeScale = state.speed != 0 ? state.speed : -1,
                            threshold = threshold
                        };
                    }



                    var baseBranch = new Branch
                    {
                        Name = name,
                        Parameter = parameter,
                        ChildMotions = newChildren
                    };

                    var infoLog = infoReport.ToString();
                    if (!string.IsNullOrEmpty(infoLog)) infoLog = ("Behaviour may be different due to the following reasons:\n" + infoLog).Trim();


                    var warnLog = warnReport.ToString();
                    if (!string.IsNullOrEmpty(warnLog)) warnLog = ("Behaviour is likely to be different due to the following reasons:\n" + warnLog).Trim();

                    var errorLog = errorReport.ToString();
                    if (!string.IsNullOrEmpty(errorLog)) errorLog = ("Some stuff may break for these reasons:\n" + errorLog).Trim();

                    var active = string.IsNullOrEmpty(errorLog) && string.IsNullOrEmpty(warnLog);
                    optBranch = new OptimizeBranch(baseBranch) {LinkedLayer = layer, LinkedLayerIndex = layerIndex, DisplayType = endStates.Count == 2 ? "Toggle" : "Exclusive Toggle", IsActive = active,InfoLog = infoLog, WarnLog = warnLog, ErrorLog = errorLog};

                    return true;

                    bool Failure(out bool success) => !(success = false);
                }
            }

        }

        public static implicit operator Branch(OptimizeBranch b) => b.BaseBranch;
    }

    public class OptimizationInfo
    {
        public AnimatorController TargetController;
        public BlendTree MasterTree;
        public readonly List<OptimizeBranch> OptBranches = new();
        public int Count => OptBranches.Count;


        public void Add(OptimizeBranch branch)
            => OptBranches.Add(branch);

        public OptimizeBranch this[int i] => OptBranches[i];
    }
}
