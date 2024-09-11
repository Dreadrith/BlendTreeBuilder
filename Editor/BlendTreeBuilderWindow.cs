using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using static Editor.BlendTreeBuilderCustomGUI;
using static Editor.BlendTreeBuilderMain;
using static Editor.BlendTreeBuilderHelper;

namespace Editor
{
    public class BlendTreeBuilderWindow : EditorWindow
    {
        #region Constants
        public const string GeneratedAssetsPath = "Assets/DreadScripts/BlendTreeBuilder/Generated Assets";
        public const string PriorityWarningPrefkey = "BTBPriorityWarningRead";
        #endregion

        #region Privates
        private static readonly string[] ToolbarOptions = { "Optimize", "Build" };
        private static int _toolbarIndex;
        private static Vector2 _scroll;

        private static BlendTree _masterBlendtree;
        private static AnimatorController _fxController;

        private static AnimatorController FXController
        {
            get => _fxController;
            set
            {
                if (_fxController == value) return;
                _fxController = value;
                CurrentOptInfo = null;
            }
        }
        private static VRCExpressionParameters _exParameters;
        private static OptimizationInfo _currentOptInfo;
        private static bool _shouldRepaint;
        private static int _currentStep;

        private static bool _hasReadPriorityWarning;

        private static OptimizationInfo CurrentOptInfo
        {
            get => _currentOptInfo;
            set
            {
                _currentOptInfo = value;
                if (_currentOptInfo == null) return;
                AllReplace = GetBoolState(_currentOptInfo.OptBranches.Select(b => b.IsReplacing));
                AllActive = GetBoolState(_currentOptInfo.OptBranches.Select(b => b.IsActive));
            }
        }
        #endregion

        #region Input
        public static bool MakeDuplicate = true;
        public static int AllActive = 1;
        public static int AllReplace = 1;

        private static VRCAvatarDescriptor _avatar;
        public static VRCAvatarDescriptor Avatar
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
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_currentStep)
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

            if (!_shouldRepaint) return;
            _shouldRepaint = false;
            Repaint();
        }

        private void DrawAvatarSelectionStep()
        {
            using (new TitledScope("Select your Avatar"))
            {
                var tempAvatar = Avatar;
                const string fieldLabel = "Avatar";

                EditorGUI.BeginChangeCheck();

                if (Avatar) DrawValidatedField("Avatar is Set!", ref tempAvatar, fieldLabel);
                else {DrawWarningField("Please select your target Avatar", ref tempAvatar, fieldLabel, () =>
                {
                    AutoDetectAvatar();
                    tempAvatar = Avatar;
                }, "Auto-Detect"); }

                if (EditorGUI.EndChangeCheck())
                    Avatar = tempAvatar;
                
                DrawNextButton(Avatar);
            }
        }

        /*private void DrawAvatarReadyStep()
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
                }#1#

                DrawNextButton(fxController);
            }
        }*/


        private void DrawMasterTreeReadyStep()
        {
            ResetStepsIf(!Avatar || !FXController, false);
            using (new TitledScope("Master BlendTree Setup"))
            {
                const string fieldLabel = "Master BlendTree";

                if (_masterBlendtree) DrawValidatedField("Master BlendTree is ready for use!", ref _masterBlendtree, fieldLabel);
                else
                {
                    DrawWarningField("Master BlendTree is not setup on the Controller.",
                        ref _masterBlendtree, fieldLabel,
                        () => { _masterBlendtree = GetOrGenerateMasterBlendTree(Avatar); }, "Ready Master BlendTree");
                }


                DrawNextButton(_masterBlendtree);
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

                var tempAvatar = Avatar;

                EditorGUI.BeginChangeCheck();

                if (Avatar) DrawValidatedField("Avatar is Set!", ref tempAvatar, fieldLabel);
                else
                {
                    DrawWarningField("Please select your target Avatar", ref tempAvatar, fieldLabel, () =>
                    {
                        AutoDetectAvatar();
                        tempAvatar = Avatar;
                    }, "Auto-Detect");
                }

                if (EditorGUI.EndChangeCheck())
                    Avatar = tempAvatar;

                #endregion
                var tempController = FXController;

                EditorGUI.BeginChangeCheck();
                if (FXController) DrawValidatedField("Controller is ready for use!", ref tempController, fieldLabel2);
                else
                {
                    DrawWarningField("FX Controller is not setup on the Avatar",
                        ref tempController, fieldLabel2, !Avatar || true ? (Action)null : () => { FXController = Avatar.ReadyPlayableLayer(VRCAvatarDescriptor.AnimLayerType.FX, GeneratedAssetsPath); },
                        "Ready FX");
                }

                if (EditorGUI.EndChangeCheck())
                    FXController = tempController;

                /*
                if (exParameters) DrawValidatedField("Expression Parameters are ready for use!", ref exParameters, fieldLabel3);
                else
                {
                    DrawWarningField("Expression Parameters are not setup on the Avatar",
                        ref exParameters, fieldLabel3,
                        () => { exParameters = avatar.ReadyExpressionParameters(GENERATED_ASSETS_PATH); }, 
                        "Ready ExParameters");
                }*/

                DrawNextButton(FXController);
            }

        }
        private void DrawMainStep()
        {
            _toolbarIndex = GUILayout.Toolbar(_toolbarIndex, ToolbarOptions, EditorStyles.toolbarButton);
            switch (_toolbarIndex)
            {
                case 0:
                    DrawOptimizationWindow();
                    break;
                case 1:
                    using (new TitledScope(ToolbarOptions[1]))
                        EditorGUILayout.HelpBox("Under development!", MessageType.Info);
                    break;
            }
        }

        private static void DrawOptimizationWindow()
        {
            ResetStepsIf(!FXController, false);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    using (new BgColoredScope(AllActive, Color.grey, Color.green, ColorOrange))
                        if (GUILayout.Button(new GUIContent("All Active", "Will be used during generation"), GUILayout.ExpandWidth(false)))
                        {
                            var newState = ToggleBoolState(ref AllActive);
                            for (var i = 0; i < CurrentOptInfo.Count; i++)
                                CurrentOptInfo[i].IsActive = newState;
                        }

                    GUILayout.Space(65);
                    EditorGUILayout.LabelField("Optimize", Styles.TitleLabel);

                    using (new BgColoredScope(MakeDuplicate, Color.green, Color.grey))
                        if (GUILayout.Button(new GUIContent("Backup", "Creates a backup of the controller before optimizing."), GUILayout.ExpandWidth(false)))
                            MakeDuplicate = !MakeDuplicate;

                    using (new EditorGUI.DisabledScope(CurrentOptInfo == null || CurrentOptInfo.Count == 0))
                    using (new BgColoredScope(AllReplace, Color.grey, Color.green, ColorOrange))
                        if (GUILayout.Button(new GUIContent("All Replace", "Will remove the optimized layer on apply."), GUILayout.ExpandWidth(false)))
                        {
                            var newState = ToggleBoolState(ref AllReplace);
                            if (CurrentOptInfo != null)
                                for (var i = 0; i < CurrentOptInfo.Count; i++)
                                    CurrentOptInfo[i].IsReplacing = newState;
                        }

                    


                }
                DrawSeparator();


                if (!_hasReadPriorityWarning)
                {
                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        DrawWarning("Optimizer does not take into account layer priority! If some associated animation clips overlap with others, then behaviour may change.");
                        using (new GUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("Understood"))
                                _hasReadPriorityWarning = true;
                            if (GUILayout.Button("Don't Show Again.", GUILayout.ExpandWidth(false)))
                            {
                                PlayerPrefs.SetInt(PriorityWarningPrefkey, 1);
                                _hasReadPriorityWarning = true;
                            }
                        }

                        EditorGUILayout.Space();
                    }
                }
                if (CurrentOptInfo == null)
                {
                    if (GUILayout.Button("Get Optimization Info", Styles.ComicallyLargeButton))
                        CurrentOptInfo = GetOptimizationInfo(FXController);
                }
                else
                {
                    if (CurrentOptInfo.Count == 0)
                    {
                        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            DrawValidated("Nothing to optimize!");
                            using (new GUILayout.HorizontalScope())
                            {
                                DrawBackButton();
                                if (GUILayout.Button("Refresh"))
                                    CurrentOptInfo = GetOptimizationInfo(FXController);
                            }

                        }
                    }
                    else
                    {
                        for (var i = 0; i < CurrentOptInfo.Count; i++)
                        {
                            var b = CurrentOptInfo[i];

                            using (new EditorGUI.DisabledScope(!b.IsActive))
                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                using (new GUILayout.HorizontalScope())
                                {
                                    using (new IsolatedDisableScope(false))
                                    {
                                        EditorGUI.BeginChangeCheck();
                                        b.IsActive = EditorGUILayout.Toggle(b.IsActive, GUILayout.Width(12), GUILayout.Height(18));
                                        if (EditorGUI.EndChangeCheck())
                                        {
                                            b.Foldout = false;
                                            AllActive = GetBoolState(CurrentOptInfo.OptBranches.Select(branch => branch.IsActive));
                                        }
                                    }
                                    var paramLabel = string.IsNullOrEmpty(b.BaseBranch.Parameter) ? "No Parameter" : b.BaseBranch.Parameter;

                                    var foldIcon = b.Foldout ? Content.FoldoutIconOn : Content.FoldoutIconOff;
                                    if (GUILayout.Button(foldIcon, Styles.IconButton, GUILayout.Width(15), GUILayout.Height(18)))
                                        b.Foldout = !b.Foldout;
                                    GUILayout.Label(b.BaseBranch.Name, Styles.FoldoutLabel);
                                    GUILayout.Label($"({b.LinkedLayerIndex})", Styles.FaintLabel);
                                    GUILayout.Label($"[{paramLabel}]", Styles.ItalicFaintLabel);
                                    //b.foldout = EditorGUILayout.Foldout(b.foldout, fullName);
                                    
                                    GUILayout.FlexibleSpace();
                                    GUILayout.Label(b.DisplayType, Styles.TypeLabel);
                                    using (new IsolatedDisableScope(false))
                                    {
                                        if (!string.IsNullOrEmpty(b.InfoLog)) GUILayout.Label(new GUIContent(Content.InfoIcon) { tooltip = b.InfoLog }, Styles.IconButton, GUILayout.Width(18), GUILayout.Height(18));
                                        if (!string.IsNullOrEmpty(b.WarnLog)) GUILayout.Label(new GUIContent(Content.WarnIcon) { tooltip = b.WarnLog }, Styles.IconButton, GUILayout.Width(18), GUILayout.Height(18));
                                        if (!string.IsNullOrEmpty(b.ErrorLog)) GUILayout.Label(new GUIContent(Content.ErrorIcon) { tooltip = b.ErrorLog }, Styles.IconButton, GUILayout.Width(18), GUILayout.Height(18));
                                    }

                                    using (new BgColoredScope(b.IsReplacing, Color.green, Color.grey))
                                        if (GUILayout.Button(new GUIContent("Replace Layer", "Will remove the optimized animator layer on apply.")))
                                        {
                                            b.IsReplacing = !b.IsReplacing;
                                            AllReplace = GetBoolState(CurrentOptInfo.OptBranches.Select(branch => branch.IsReplacing));
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

                                if (!b.Foldout) continue;
                                var targetChildren = b.BaseBranch.ChildMotions;
                                EditorGUI.BeginChangeCheck();

                                using (new EditorGUI.DisabledScope(!b.CanEdit))
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
                                            for (var j = 0; j < targetChildren.Length; j++)
                                            {
                                                targetChildren[j].motion.QuickField(GUIContent.none);
                                                DoPlaceholderLabel($"Motion {j + 1}", 100, 24);
                                            }
                                            break;
                                    }

                                if (EditorGUI.EndChangeCheck())
                                    b.BaseBranch.ChildMotions = targetChildren;

                            }
                        }

                        EditorGUILayout.Space();

                        using (new GUILayout.HorizontalScope())
                        {
                            DrawBackButton();
                            using (new EditorGUI.DisabledScope(CurrentOptInfo.OptBranches.TrueForAll(b => !b.IsActive)))
                            using (new BgColoredScope(Color.green))
                                if (GUILayout.Button("Optimize!", Styles.ComicallyLargeButton))
                                {
                                    if (MakeDuplicate) Duplicate(FXController).name += " (Backup)";
                                    ApplyOptimization(CurrentOptInfo);
                                    CurrentOptInfo = GetOptimizationInfo(FXController);
                                }

                            if (GUILayout.Button("Refresh", Styles.ComicallyLargeButton, GUILayout.ExpandWidth(false)))
                                CurrentOptInfo = GetOptimizationInfo(FXController);
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
                if (_currentStep != 0)
                    DrawBackButton();
                using (new EditorGUI.DisabledScope(!nextCondition))
                using (new BgColoredScope(nextCondition, Color.green, Color.grey))
                    if (GUILayout.Button("Next"))
                        _currentStep++;
                    
            }
        }

        private static void DrawBackButton()
        {
            if (GUILayout.Button(Content.BackIcon, Styles.IconButton, GUILayout.Width(18), GUILayout.Height(EditorGUIUtility.singleLineHeight)))
                _currentStep--;

        }


        private static void OnAvatarChanged()
        {
            if (!Avatar) return;
            FXController = Avatar.GetPlayableLayer(VRCAvatarDescriptor.AnimLayerType.FX);
            if (!_exParameters) _exParameters = Avatar.expressionParameters;
            _masterBlendtree = GetMasterBlendTree(Avatar);
        }

        private static void ResetStepsIf(bool condition, bool throwError)
        {
            if (!condition) return;
            _currentStep = 0;
            CurrentOptInfo = null;
            if (throwError) throw new Exception("[BlendTree Builder] Unhandled exception occured. Steps have been reset.");

        }

        private void OnEnable()
        {
            _hasReadPriorityWarning = PlayerPrefs.GetInt(PriorityWarningPrefkey, 0) == 1;
            AutoDetectAvatar();
        }

        private void OnFocus()
        {
            OnAvatarChanged();
        }

        #region Sub-Methods
        private static void AutoDetectAvatar() => Avatar = Avatar ? Avatar : FindObjectOfType<VRCAvatarDescriptor>();
        #endregion
    }
}
