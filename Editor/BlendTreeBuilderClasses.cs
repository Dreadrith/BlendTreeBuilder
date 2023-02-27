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

        public Motion startMotion;
        public float startSpeed;

        public Motion endMotion;
        public float endSpeed;

        public BlendTree linkedTree;
    }

    public class OptimizeBranch
    {
        public Branch baseBranch;
        public AnimatorControllerLayer linkedLayer;
        public bool isActive = true;
        public bool isReplacing = true;
        public bool foldout;
        public string warnLog;
        public string errorLog;

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
            StringBuilder warnReport = new StringBuilder();
            StringBuilder errorReport = new StringBuilder();

            switch (layer.stateMachine.states.Length)
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
                        bool foundBehaviours = false;

                        //Iteration stops when anything returns true
                        layer.stateMachine.Iteratetransitions(t =>
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


                                bool standardizedCondition = false;
                                
                                switch (c.mode)
                                {
                                    case AnimatorConditionMode.If: standardizedCondition = true; break;
                                    case AnimatorConditionMode.Equals when c.threshold == 1:
                                    case AnimatorConditionMode.Greater when c.threshold > Mathf.Epsilon:
                                        standardizedCondition = true;
                                        warnReport.AppendLine("\n- Parameter is not a bool. Conditions has been standardized to true/false which may not reflect the behaviour accurately.");
                                        break;
                                }

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

                                if (!foundBehaviours && t.destinationState.behaviours != null && t.destinationState.behaviours.Length > 0)
                                {
                                    foundBehaviours = true;
                                    errorReport.AppendLine("\n- Layer contains statemachine behaviours, which may be necessary for certain functionality, such as Exclusive Toggles.");
                                }

                            }

                            if (!mayChangeBehaviour && t is AnimatorStateTransition t2 && ((t2.hasExitTime && t2.exitTime > 0) || t2.duration > 0 || t2.offset > 0))
                                mayChangeBehaviour = true;


                            if (!success && !string.IsNullOrEmpty(parameter) && startState && endState) success = true;

                            return false;
                        });
                        if (!success) return false;

                        AnimatorControllerParameter animParameter = controller.parameters.FirstOrDefault(p => p.name == parameter);
                        if (animParameter == null || animParameter.type != AnimatorControllerParameterType.Float)
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
                        //Add this to OptBranch error log instead
                        else errorReport.AppendLine($"\nParameter {parameter} not found in the controller!");
                        

                        var baseBranch = new Branch()
                        {
                            name = name, 
                            parameter = parameter, 
                            startMotion = startState.motion, 
                            endMotion = endState.motion, 
                            startSpeed = startSpeed, 
                            endSpeed = endSpeed
                        };

                        string warnLog = warnReport.ToString();
                        if (!string.IsNullOrEmpty(warnLog)) warnLog = ("Toggle behaviour may be different due to the following reasons:\n" + warnLog).Trim();

                        string errorLog = errorReport.ToString();
                        if (!string.IsNullOrEmpty(errorLog)) errorLog = ("Some stuff may break for these reasons:\n" + errorLog).Trim();

                        optBranch = new OptimizeBranch(baseBranch) { linkedLayer = layer, isActive = string.IsNullOrEmpty(errorLog), warnLog = warnLog, errorLog = errorLog};
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
