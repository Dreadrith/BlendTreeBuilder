using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Editor
{
    public static class BlendTreeBuilderCustomGUI
    {
        internal sealed class IsolatedDisableScope : IDisposable
        {
            private readonly bool _wasEnabled;

            public IsolatedDisableScope(bool disabled)
            {
                _wasEnabled = GUI.enabled;
                GUI.enabled = !disabled;
            }
            public void Dispose()
            {
                GUI.enabled = _wasEnabled;
            }
        }

        public class BgColoredScope : IDisposable
        {
            private readonly Color _ogColor;
            public BgColoredScope(Color setColor)
            {
                _ogColor = GUI.backgroundColor;
                GUI.backgroundColor = setColor;
            }

            public BgColoredScope(bool isActive, Color active, Color inactive)
            {
                _ogColor = GUI.backgroundColor;
                GUI.backgroundColor = isActive ? active : inactive;
            }

            public BgColoredScope(int selectedIndex, params Color[] colors)
            {
                _ogColor = GUI.backgroundColor;
                GUI.backgroundColor = colors[selectedIndex];
            }
            public void Dispose()
            {
                GUI.backgroundColor = _ogColor;
            }
        }
        public class TitledScope : IDisposable
        {
            internal TitledScope(string title, params GUILayoutOption[] options)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox, options);
                EditorGUILayout.LabelField(title, Styles.TitleLabel);
                DrawSeparator();
            }

            public void Dispose() => EditorGUILayout.EndVertical();
        }

        public static class Styles
        {
            public static readonly GUIStyle FaintLabel
                = new(GUI.skin.label)
                {
                    fontSize = 11,
                    contentOffset = new Vector2(-2.5f, 1.5f),
                    normal = { textColor = EditorGUIUtility.isProSkin ? Color.gray : new Color(0.357f, 0.357f, 0.357f) }
                };

            public static readonly GUIStyle ItalicFaintLabel
                = new(FaintLabel)
                {
                    fontStyle = FontStyle.Italic,
                };

            public static readonly GUIStyle PlaceHolderLabel
                = new(ItalicFaintLabel)
                {
                    alignment = TextAnchor.MiddleRight,
                    contentOffset = new Vector2(-2.5f, 0),
                };

            public static readonly GUIStyle TypeLabel
                = new(ItalicFaintLabel)
                { alignment = TextAnchor.MiddleRight};

            private static readonly GUIStyle CenteredLabel = new(EditorStyles.largeLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };

            public static readonly GUIStyle TitleLabel = new(CenteredLabel) {fontSize = 18, clipping = TextClipping.Overflow};
            public static readonly GUIStyle WrappedLabel = new(GUI.skin.label) {wordWrap = true};

            public static readonly GUIStyle IconButton
                = new()
                {
                    padding = new RectOffset(1, 1, 1, 1),
                    margin = new RectOffset(),
                    alignment = TextAnchor.MiddleCenter,
                    contentOffset = new Vector2(0, 2)
                };

            public static readonly GUIStyle FoldoutLabel = new(GUI.skin.label)
            {
                padding = new RectOffset(1, 1, 1, 1),
                margin = new RectOffset(),
                contentOffset = new Vector2(0, 2)
            };

            public static readonly GUIStyle ComicallyLargeButton = new(GUI.skin.button)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold
            };
        }

        public static class Content
        {
            public static readonly GUIContent GreenLightIcon = EditorGUIUtility.IconContent("greenLight");
            public static readonly GUIContent OrangeLightIcon = EditorGUIUtility.IconContent("orangeLight");
            public static readonly GUIContent BackIcon = EditorGUIUtility.IconContent("back");
            public static readonly GUIContent InfoIcon = EditorGUIUtility.IconContent("console.warnicon.inactive.sml");
            public static readonly GUIContent WarnIcon = EditorGUIUtility.IconContent("console.warnicon.sml");
            public static readonly GUIContent ErrorIcon = EditorGUIUtility.IconContent("CollabError");
            public static readonly GUIContent FoldoutIconOff = EditorGUIUtility.IconContent("IN foldout");
            public static readonly GUIContent FoldoutIconOn = EditorGUIUtility.IconContent("IN foldout on");
        }

        public static void DrawSeparator(int thickness = 2, int padding = 10)
        {
            var r = EditorGUILayout.GetControlRect(GUILayout.Height(thickness + padding));
            r.height = thickness;
            r.y += padding / 2f;
            r.x -= 2;
            r.width += 6;
            ColorUtility.TryParseHtmlString(EditorGUIUtility.isProSkin ? "#595959" : "#858585", out var lineColor);
            EditorGUI.DrawRect(r, lineColor);
        }

        /*
        public static void DrawValidatedField<T>(string msg, ref T refObject, string label) where T : Object
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                refObject = refObject.QuickField(label);
                DrawSeparator();
                DrawValidated(msg);
            }
        }
        */

        /*
        public static void DrawWarningField<T>(string msg, ref T refObject, string label, Action autoFixAction, string fixButtonLabel = "Auto-Fix") where T : Object
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                refObject = refObject.QuickField(label);
                DrawSeparator();
                DrawWarning(msg);
                if (autoFixAction != null)
                {
                    using (new BGColoredScope(ColorOrange))
                        if (GUILayout.Button(fixButtonLabel))
                            autoFixAction();
                }

            }
        }
        */
        public static void DrawValidatedField<T>(string msg, ref T refObject, string label) where T : Object
        {
            using (new GUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                DrawGreenLightIcon(msg);
                refObject = refObject.QuickField(label);
            }
        }

        public static void DrawWarningField<T>(string msg, ref T refObject, string label, Action autoFixAction, string fixLabel) where T : Object
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    DrawOrangeLightIcon(msg);
                    refObject = refObject.QuickField(label);
                    if (autoFixAction == null) return;
                    using (new BgColoredScope(ColorOrange))
                        if (GUILayout.Button(fixLabel, GUILayout.ExpandWidth(false)))
                            autoFixAction();
                }
                

            }
        }

        public static void DrawValidated(string msg)
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label(Content.GreenLightIcon, GUILayout.Width(28), GUILayout.Height(28));
                GUILayout.Label(msg, Styles.WrappedLabel, GUILayout.Height(28));
            }
        }
        public static void DrawWarning(string msg)
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label(Content.OrangeLightIcon, GUILayout.Width(28), GUILayout.Height(28));
                GUILayout.Label(msg, Styles.WrappedLabel, GUILayout.Height(28));
            }
        }

        private static void DrawLightIcon(bool isGreen, string tooltip)
            => GUILayout.Label(new GUIContent(isGreen ? Content.GreenLightIcon : Content.OrangeLightIcon) { tooltip = tooltip }, GUILayout.Width(18), GUILayout.Height(18));

        private static void DrawGreenLightIcon(string tooltip)
            => DrawLightIcon(true, tooltip);

        private static void DrawOrangeLightIcon(string tooltip)
            => DrawLightIcon(false, tooltip);

        private static void DoPlaceholderLabel(Rect r, string label, float minimumWidth = 0, float extraOffset = 0, GUIStyle customStyle = null)
        {
            if (!(r.width > minimumWidth + extraOffset)) return;
            r.x -= extraOffset;
            GUI.Label(r, label, customStyle ?? Styles.PlaceHolderLabel);
        }
        internal static void DoPlaceholderLabel(string label, float minimumWidth = 0, float extraOffset = 0, GUIStyle customStyle = null) => DoPlaceholderLabel(GUILayoutUtility.GetLastRect(), label, minimumWidth, extraOffset, customStyle);


        public static T QuickField<T>(this T target, GUIContent label) where T : Object
            =>  (T)EditorGUILayout.ObjectField(label, target, typeof(T), true);

        private static T QuickField<T>(this T target, string label) where T : Object 
            => target.QuickField(new GUIContent(label));

        public static Color ColorOrange = new(0.992f, 0.784f, 0.4f);
    }
}
