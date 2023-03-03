using System;
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Avatars.Components;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.ScriptableObjects;
using static DreadScripts.BlendTreeBulder.BlendTreeBuilderCustomGUI;
using static DreadScripts.BlendTreeBulder.BlendTreeBuilderMain;
using static DreadScripts.BlendTreeBulder.BlendTreeBuilderHelper;
using System.Linq;

namespace DreadScripts.BlendTreeBulder
{
    public class BlendTreeBuilderWindow : EditorWindow
    {
        #region Constants
        public const string GENERATED_ASSETS_PATH = "Assets/DreadScripts/BlendTreeBuilder/Generated Assets";
        public const string PRIORITY_WARNING_PREFKEY = "BTBPriorityWarningRead";
        #endregion

        #region Privates
        private static readonly string[] toolbarOptions = { "Optimize", "Build" };
        private static int toolbarIndex;
        private static Vector2 scroll;

        private static BlendTree masterBlendtree;
        private static AnimatorController fxController;
        private static VRCExpressionParameters exParameters;
        private static OptimizationInfo _currentOptInfo;
        private static bool shouldRepaint;
        private static int currentStep;

        private static bool hasReadPriorityWarning;

        private static OptimizationInfo currentOptInfo
        {
            get => _currentOptInfo;
            set
            {
                _currentOptInfo = value;
                allReplace = GetBoolState(_currentOptInfo.optBranches.Select(b => b.isReplacing));
                allActive = GetBoolState(_currentOptInfo.optBranches.Select(b => b.isActive));
            }
        }
        #endregion

        #region Input
        public static bool makeDuplicate = true;
        public static int allActive = 1;
        public static int allReplace = 1;

        private static VRCAvatarDescriptor _avatar;
        public static VRCAvatarDescriptor avatar
        {
            get => _avatar;
            set
            {
                if (_avatar == value) return;
                _avatar = value;
                OnAvatarChanged();
            }
        }
        #endregion

        [MenuItem("DreadTools/BlendTree Builder", false, 72)]
        public static void ShowWindow() => GetWindow<BlendTreeBuilderWindow>("BlendTree Builder").titleContent.image = EditorGUIUtility.IconContent("BlendTree Icon").image;

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            switch (currentStep)
            {
                /*case 0: DrawAvatarSelectionStep(); break;
                case 1: DrawAvatarReadyStep(); break;
                case 2: DrawMainStep(); break;*/
                case 0: DrawReadyStep();
                    break;
                case 1: DrawMainStep();
                    break;
            }

            EditorGUILayout.EndScrollView();

            if (shouldRepaint)
            {
                shouldRepaint = false;
                Repaint();
            }
        }

        private void DrawAvatarSelectionStep()
        {
            using (new TitledScope("Select your Avatar"))
            {
                var tempAvatar = avatar;
                var fieldLabel = "Avatar";

                EditorGUI.BeginChangeCheck();

                if (avatar) DrawValidatedField("Avatar is Set!", ref tempAvatar, fieldLabel);
                else {DrawWarningField("Please select your target Avatar", ref tempAvatar, fieldLabel, () =>
                {
                    AutoDetectAvatar();
                    tempAvatar = avatar;
                }, "Auto-Detect"); }

                if (EditorGUI.EndChangeCheck())
                    avatar = tempAvatar;
                
                DrawNextButton(avatar);
            }
        }

        private void DrawAvatarReadyStep()
        {
            ResetStepsIf(!avatar, false);
            using (new TitledScope("Prepare your Avatar"))
            {
                var fieldLabel = "FX Controller";
                if (fxController) DrawValidatedField("Controller is ready for use!", ref fxController, fieldLabel);
                else
                {
                    DrawWarningField("FX Controller is not setup on the Avatar",
                        ref fxController, fieldLabel,
                        () => { fxController = avatar.ReadyPlayableLayer(VRCAvatarDescriptor.AnimLayerType.FX, GENERATED_ASSETS_PATH); }, 
                        "Ready FX Controller");
                }

                /*var fieldLabel2 = "Expression Parameters";
                if (exParameters) DrawValidatedField("Expression Parameters are ready for use!", ref exParameters, fieldLabel2);
                else
                {
                    DrawWarningField("Expression Parameters are not setup on the Avatar",
                        ref exParameters, fieldLabel2,
                        () => { exParameters = avatar.ReadyExpressionParameters(GENERATED_ASSETS_PATH); }, 
                        "Ready Expression Parameters");
                }*/

                DrawNextButton(fxController);
            }
        }


        private void DrawMasterTreeReadyStep()
        {
            ResetStepsIf(!avatar || !fxController, false);
            using (new TitledScope("Master BlendTree Setup"))
            {
                var fieldLabel = "Master BlendTree";

                if (masterBlendtree) DrawValidatedField("Master BlendTree is ready for use!", ref masterBlendtree, fieldLabel);
                else
                {
                    DrawWarningField("Master BlendTree is not setup on the Controller.",
                        ref masterBlendtree, fieldLabel,
                        () => { masterBlendtree = GetOrGenerateMasterBlendTree(avatar); }, "Ready Master BlendTree");
                }


                DrawNextButton(masterBlendtree);
            }
        }

        private void DrawReadyStep()
        {
            using (new TitledScope("Ready your Avatar"))
            {
                const string fieldLabel = "Avatar";
                const string fieldLabel2 = "FX Controller";
                //xvar fieldLabel3 = "Expression Parameters";

                #region Avatar Ready

                var tempAvatar = avatar;

                EditorGUI.BeginChangeCheck();

                if (avatar) DrawValidatedField("Avatar is Set!", ref tempAvatar, fieldLabel);
                else
                {
                    DrawWarningField("Please select your target Avatar", ref tempAvatar, fieldLabel, () =>
                    {
                        AutoDetectAvatar();
                        tempAvatar = avatar;
                    }, "Auto-Detect");
                }

                if (EditorGUI.EndChangeCheck())
                    avatar = tempAvatar;

                #endregion

                if (fxController) DrawValidatedField("Controller is ready for use!", ref fxController, fieldLabel2);
                else
                {
                    DrawWarningField("FX Controller is not setup on the Avatar",
                        ref fxController, fieldLabel2, !avatar || true ? (Action)null : () => { fxController = avatar.ReadyPlayableLayer(VRCAvatarDescriptor.AnimLayerType.FX, GENERATED_ASSETS_PATH); },
                        "Ready FX");
                }

                /*
                if (exParameters) DrawValidatedField("Expression Parameters are ready for use!", ref exParameters, fieldLabel3);
                else
                {
                    DrawWarningField("Expression Parameters are not setup on the Avatar",
                        ref exParameters, fieldLabel3,
                        () => { exParameters = avatar.ReadyExpressionParameters(GENERATED_ASSETS_PATH); }, 
                        "Ready ExParameters");
                }*/

                DrawNextButton(fxController);
            }

        }
        private void DrawMainStep()
        {
            toolbarIndex = GUILayout.Toolbar(toolbarIndex, toolbarOptions, EditorStyles.toolbarButton);
            switch (toolbarIndex)
            {
                case 0:
                    DrawOptimizationWindow();
                    break;
                case 1:
                    using (new TitledScope(toolbarOptions[1]))
                        EditorGUILayout.HelpBox("Under development!", MessageType.Info);
                    break;
            }
        }

        private void DrawOptimizationWindow()
        {
            ResetStepsIf(!fxController, false);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    using (new BGColoredScope(allActive, Color.grey, Color.green, ColorOrange))
                        if (GUILayout.Button(new GUIContent("All Active", "Will be used during generation"), GUILayout.ExpandWidth(false)))
                        {
                            bool newState = ToggleBoolState(ref allActive);
                            for (int i = 0; i < currentOptInfo.Count; i++)
                                currentOptInfo[i].isActive = newState;
                        }

                    GUILayout.Space(65);
                    EditorGUILayout.LabelField("Optimize", Styles.titleLabel);

                    using (new BGColoredScope(makeDuplicate, Color.green, Color.grey))
                        if (GUILayout.Button(new GUIContent("Backup", "Creates a backup of the controller before optimizing."), GUILayout.ExpandWidth(false)))
                            makeDuplicate = !makeDuplicate;

                    using (new EditorGUI.DisabledScope(currentOptInfo == null || currentOptInfo.Count == 0))
                    using (new BGColoredScope(allReplace, Color.grey, Color.green, ColorOrange))
                        if (GUILayout.Button(new GUIContent("All Replace", "Will remove the optimized layer on apply."), GUILayout.ExpandWidth(false)))
                        {
                            bool newState = ToggleBoolState(ref allReplace);
                            for (int i = 0; i < currentOptInfo.Count; i++)
                                currentOptInfo[i].isReplacing = newState;
                        }

                    


                }
                DrawSeparator();


                if (!hasReadPriorityWarning)
                {
                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        DrawWarning("Optimizer does not take into account layer priority! If some associated animation clips overlap with others, then behaviour may change.");
                        using (new GUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("Understood"))
                                hasReadPriorityWarning = true;
                            if (GUILayout.Button("Don't Show Again.", GUILayout.ExpandWidth(false)))
                            {
                                PlayerPrefs.SetInt(PRIORITY_WARNING_PREFKEY, 1);
                                hasReadPriorityWarning = true;
                            }
                        }

                        EditorGUILayout.Space();
                    }
                }
                if (currentOptInfo == null)
                {
                    if (GUILayout.Button("Get Optimization Info", Styles.comicallyLargeButton))
                        currentOptInfo = GetOptimizationInfo(fxController);
                }
                else
                {
                    if (currentOptInfo.Count == 0)
                    {
                        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            DrawValidated("Nothing to optimize!");
                            using (new GUILayout.HorizontalScope())
                            {
                                DrawBackButton();
                                if (GUILayout.Button("Refresh"))
                                    currentOptInfo = GetOptimizationInfo(fxController);
                            }

                        }
                    }
                    else
                    {
                        for (int i = 0; i < currentOptInfo.Count; i++)
                        {
                            var b = currentOptInfo[i];

                            using (new EditorGUI.DisabledScope(!b.isActive))
                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                using (new GUILayout.HorizontalScope())
                                {
                                    using (new IsolatedDisableScope(false))
                                    {
                                        EditorGUI.BeginChangeCheck();
                                        b.isActive = EditorGUILayout.Toggle(b.isActive, GUILayout.Width(12), GUILayout.Height(18));
                                        if (EditorGUI.EndChangeCheck())
                                        {
                                            b.foldout = false;
                                            allActive = GetBoolState(currentOptInfo.optBranches.Select(branch => branch.isActive));
                                        }
                                    }
                                    string paramLabel = string.IsNullOrEmpty(b.baseBranch.parameter) ? "No Parameter" : b.baseBranch.parameter;

                                    GUIContent foldIcon = b.foldout ? Content.foldoutIconOn : Content.foldoutIconOff;
                                    if (GUILayout.Button(foldIcon, Styles.iconButton, GUILayout.Width(15), GUILayout.Height(18)))
                                        b.foldout = !b.foldout;
                                    GUILayout.Label(b.baseBranch.name, Styles.foldoutLabel);
                                    GUILayout.Label($"({b.linkedLayerIndex})", Styles.faintLabel);
                                    GUILayout.Label($"[{paramLabel}]", Styles.italicFaintLabel);
                                    //b.foldout = EditorGUILayout.Foldout(b.foldout, fullName);
                                    
                                    GUILayout.FlexibleSpace();
                                    GUILayout.Label(b.displayType, Styles.typeLabel);
                                    using (new IsolatedDisableScope(false))
                                    {
                                        if (!string.IsNullOrEmpty(b.infoLog)) GUILayout.Label(new GUIContent(Content.infoIcon) { tooltip = b.infoLog }, Styles.iconButton, GUILayout.Width(18), GUILayout.Height(18));
                                        if (!string.IsNullOrEmpty(b.warnLog)) GUILayout.Label(new GUIContent(Content.warnIcon) { tooltip = b.warnLog }, Styles.iconButton, GUILayout.Width(18), GUILayout.Height(18));
                                        if (!string.IsNullOrEmpty(b.errorLog)) GUILayout.Label(new GUIContent(Content.errorIcon) { tooltip = b.errorLog }, Styles.iconButton, GUILayout.Width(18), GUILayout.Height(18));
                                    }

                                    using (new BGColoredScope(b.isReplacing, Color.green, Color.grey))
                                        if (GUILayout.Button(new GUIContent("Replace Layer", "Will remove the optimized animator layer on apply.")))
                                        {
                                            b.isReplacing = !b.isReplacing;
                                            allReplace = GetBoolState(currentOptInfo.optBranches.Select(branch => branch.isReplacing));
                                        }

                                    /*using (new IsolatedDisableScope(false))
                                    using (new BGColoredScope(b.isActive, Color.green, Color.grey))
                                        if (GUILayout.Button(new GUIContent("Active", "Will be used during generation")))
                                        {
                                            b.isActive = !b.isActive;
                                            b.foldout = false;
                                            allActive = GetBoolState(currentOptInfo.optBranches.Select(branch => branch.isActive));
                                        }*/
                                }

                                if (!b.foldout) continue;
                                var targetChildren = b.baseBranch.childMotions;
                                EditorGUI.BeginChangeCheck();

                                using (new EditorGUI.DisabledScope(!b.canEdit))
                                    switch (targetChildren.Length)
                                    {
                                        case 1:
                                            targetChildren[0].motion.QuickField(GUIContent.none);
                                            DoPlaceholderLabel("Motion", 40, 24);
                                            break;
                                        case 2:
                                            using (new GUILayout.HorizontalScope())
                                            {
                                                targetChildren[0].motion.QuickField(GUIContent.none);
                                                DoPlaceholderLabel("Off", 40, 24);

                                                targetChildren[0].motion.QuickField(GUIContent.none);
                                                DoPlaceholderLabel("On", 40, 24);
                                            }
                                            break;
                                        default:
                                            for (int j = 0; j < targetChildren.Length; j++)
                                            {
                                                targetChildren[j].motion.QuickField(GUIContent.none);
                                                DoPlaceholderLabel($"Motion {j + 1}", 100, 24);
                                            }
                                            break;
                                    }

                                if (EditorGUI.EndChangeCheck())
                                    b.baseBranch.childMotions = targetChildren;

                            }
                        }

                        EditorGUILayout.Space();

                        using (new GUILayout.HorizontalScope())
                        {
                            DrawBackButton();
                            using (new EditorGUI.DisabledScope(currentOptInfo.optBranches.TrueForAll(b => !b.isActive)))
                            using (new BGColoredScope(Color.green))
                                if (GUILayout.Button("Optimize!", Styles.comicallyLargeButton))
                                {
                                    if (makeDuplicate) Duplicate(fxController).name += " (Backup)";
                                    ApplyOptimization(currentOptInfo);
                                    currentOptInfo = GetOptimizationInfo(fxController);
                                }

                            if (GUILayout.Button("Refresh", Styles.comicallyLargeButton, GUILayout.ExpandWidth(false)))
                                currentOptInfo = GetOptimizationInfo(fxController);
                        }

                    }
                }
            }
        }

        private static void DrawNextButton(bool nextCondition)
        {
            EditorGUILayout.Space();
            using (new GUILayout.HorizontalScope())
            {
                if (currentStep != 0)
                    DrawBackButton();
                using (new EditorGUI.DisabledScope(!nextCondition))
                using (new BGColoredScope(nextCondition, Color.green, Color.grey))
                    if (GUILayout.Button("Next"))
                        currentStep++;
                    
            }
        }

        private static void DrawBackButton()
        {
            if (GUILayout.Button(Content.backIcon, Styles.iconButton, GUILayout.Width(18), GUILayout.Height(EditorGUIUtility.singleLineHeight)))
                currentStep--;

        }


        private static void OnAvatarChanged()
        {
            if (!avatar) return;
            if (!fxController) fxController = avatar.GetPlayableLayer(VRCAvatarDescriptor.AnimLayerType.FX);
            if (!exParameters) exParameters = avatar.expressionParameters;
            masterBlendtree = GetMasterBlendTree(avatar);
        }

        private static void ResetStepsIf(bool condition, bool throwError)
        {
            if (!condition) return;
            currentStep = 0;
            currentOptInfo = null;
            if (throwError) throw new Exception("[BlendTree Buildter] Unhandled exception occured. Steps have been reset.");

        }

        private void OnEnable()
        {
            hasReadPriorityWarning = PlayerPrefs.GetInt(PRIORITY_WARNING_PREFKEY, 0) == 1;
            AutoDetectAvatar();
        }

        private void OnFocus()
        {
            OnAvatarChanged();
        }

        #region Sub-Methods
        private static void AutoDetectAvatar() => avatar = avatar ? avatar : FindObjectOfType<VRCAvatarDescriptor>();
        #endregion
    }
}
