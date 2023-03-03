using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        public ChildMotion[] childMotions;
        
        
        //For the builder: Allow editing the tree directly through the tool window
        //public BlendTree linkedTree;
    }

    public class OptimizeBranch
    {
        public Branch baseBranch;
        public AnimatorControllerLayer linkedLayer;
        public int linkedLayerIndex;
        public bool isActive = true;
        public bool isReplacing = true;
        public bool canEdit = true;
        public bool foldout;
        public string infoLog;
        public string warnLog;
        public string errorLog;
        public string displayType;
        public bool isMotionTimed;

        public OptimizeBranch(Branch branch)
        {
            baseBranch = branch;
        }
        public static bool TryExtract(AnimatorController controller, int layerIndex, out OptimizeBranch optBranch)
        {
            bool mayChangeBehaviour = false;
            optBranch = null;
            var layer = controller.layers[layerIndex];
            if (!layer.stateMachine) return false;

            string name = layer.name;
            string parameter = string.Empty;
            StringBuilder infoReport = new StringBuilder();
            StringBuilder warnReport = new StringBuilder();
            StringBuilder errorReport = new StringBuilder();

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

                    bool hasAnyTransition = false;
                    bool success = true;
                    layer.stateMachine.Iteratetransitions(t =>
                    {
                        hasAnyTransition = true;
                        return !(success = !t.destinationStateMachine && (!t.destinationState || t.destinationState == state));
                    });
                    if (!success) return false;

                    if (hasAnyTransition) errorReport.AppendLine("\n- Layer contains transitions from and/or to the only state.");
                    if (!state.timeParameterActive)
                    {
                        Branch baseBranch = new Branch() {name = controller.layers[layerIndex].name, childMotions = new ChildMotion[] {new ChildMotion() {motion = state.motion}}};
                        optBranch = new OptimizeBranch(baseBranch) {linkedLayer = layer, linkedLayerIndex = layerIndex, displayType = "Single State"};
                        return true;
                    }

                    if (state.motion is AnimationClip)
                    {
                        Branch baseBranch = new Branch() {name = controller.layers[layerIndex].name, parameter = state.timeParameter, childMotions = new ChildMotion[] {new ChildMotion() {motion = state.motion}}};
                        optBranch = new OptimizeBranch(baseBranch) {linkedLayer = layer, linkedLayerIndex = layerIndex, displayType = "Motion Time State", isMotionTimed = true, canEdit = false};
                        return true;
                    }

                    return false;
                }
                default:
                {
                    HashSet<AnimatorState> visitedStates = new HashSet<AnimatorState>();
                    Dictionary<float, AnimatorState> endStates = new Dictionary<float, AnimatorState>();

                    bool success = false;

                    bool Failure() => !(success = false);
                    bool foundBehaviours = false;
                    bool foundLoopingZeroSpeed = false;
                    bool foundNonInstantTransitions = false;

                    layer.stateMachine.Iteratetransitions(t =>
                    {
                        if (t.mute) return false;
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


                            float standardizedThreshold = c.threshold;
                            var dState = t.destinationState;

                            switch (c.mode)
                            {
                                case AnimatorConditionMode.NotEqual: return Failure();
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
                            }

                            if (endStates.TryGetValue(standardizedThreshold, out AnimatorState endState))
                            {
                                if (endState != dState)
                                    return Failure();
                            }
                            else
                            {
                                if (visitedStates.Contains(dState)) return Failure();
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

                    AnimatorControllerParameter animParameter = controller.parameters.FirstOrDefault(p => p.name == parameter);
                    if (animParameter != null)
                    {
                        if (animParameter.type != AnimatorControllerParameterType.Float)
                        {
                            bool wasReused = false;
                            foreach (var layer2 in controller.layers)
                            {
                                if (layer2.stateMachine == layer.stateMachine) continue;

                                bool parameterReused = false;
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
                    int currentIndex = 0;
                    foreach (var (threshold, state) in endStates)
                    {
                        newChildren[currentIndex++] = new ChildMotion
                        {
                            motion = state.motion,
                            timeScale = state.speed != 0 ? state.speed : -1,
                            threshold = threshold
                        };
                    }



                    var baseBranch = new Branch()
                    {
                        name = name,
                        parameter = parameter,
                        childMotions = newChildren
                    };

                    string infoLog = infoReport.ToString();
                    if (!string.IsNullOrEmpty(infoLog)) infoLog = ("Behaviour may be different due to the following reasons:\n" + infoLog).Trim();


                    string warnLog = warnReport.ToString();
                    if (!string.IsNullOrEmpty(warnLog)) warnLog = ("Behaviour is likely to be different due to the following reasons:\n" + warnLog).Trim();

                    string errorLog = errorReport.ToString();
                    if (!string.IsNullOrEmpty(errorLog)) errorLog = ("Some stuff may break for these reasons:\n" + errorLog).Trim();

                    var active = string.IsNullOrEmpty(errorLog) && string.IsNullOrEmpty(warnLog);
                    optBranch = new OptimizeBranch(baseBranch) {linkedLayer = layer, linkedLayerIndex = layerIndex, displayType = endStates.Count == 2 ? "Toggle" : "Exclusive Toggle", isActive = active,infoLog = infoLog, warnLog = warnLog, errorLog = errorLog};

                    return true;

                }
            }

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
