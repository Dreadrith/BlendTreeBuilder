using System;
using UnityEditor;
using UnityEngine;
using static Thry.BetterTooltips;
using Object = UnityEngine.Object;

namespace DreadScripts.BlendTreeBulder
{
    public static class BlendTreeBuilderCustomGUI
    {
        internal sealed class IsolatedDisableScope : IDisposable
        {
            private readonly bool wasEnabled;

            public IsolatedDisableScope(bool disabled)
            {
                wasEnabled = GUI.enabled;
                GUI.enabled = !disabled;
            }
            public void Dispose()
            {
                GUI.enabled = wasEnabled;
            }
        }

        public class BGColoredScope : System.IDisposable
        {
            private readonly Color ogColor;
            public BGColoredScope(Color setColor)
            {
                ogColor = GUI.backgroundColor;
                GUI.backgroundColor = setColor;
            }
            public BGColoredScope(bool isActive, Color setColor )
            {
                ogColor = GUI.backgroundColor;
                GUI.backgroundColor = isActive ? setColor : ogColor;
            }
            public BGColoredScope(bool isActive, Color active, Color inactive)
            {
                ogColor = GUI.backgroundColor;
                GUI.backgroundColor = isActive ? active : inactive;
            }

            public BGColoredScope(int selectedIndex, params Color[] colors)
            {
                ogColor = GUI.backgroundColor;
                GUI.backgroundColor = colors[selectedIndex];
            }
            public void Dispose()
            {
                GUI.backgroundColor = ogColor;
            }
        }
        public class TitledScope : IDisposable
        {
            internal TitledScope(string title, params GUILayoutOption[] options)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox, options);
                EditorGUILayout.LabelField(title, Styles.titleLabel);
                DrawSeparator();
            }

            internal TitledScope( string title, Action prevGUI, Action afterGUI, params GUILayoutOption[] options)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox, options);
                using (new GUILayout.HorizontalScope())
                {
                    prevGUI();
                    EditorGUILayout.LabelField(title, Styles.titleLabel);
                    afterGUI();
                }
                DrawSeparator();
            }

            public void Dispose() => EditorGUILayout.EndVertical();
        }

        public static class Styles
        {
            public  static readonly GUIStyle faintLabel
                = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Italic,
                    fontSize = 11,
                };

            public static readonly GUIStyle placeHolderLabel
                = new GUIStyle(faintLabel)
                {
                    alignment = TextAnchor.MiddleRight,
                    contentOffset = new Vector2(-2.5f, 0),
                    normal = { textColor = EditorGUIUtility.isProSkin ? Color.gray : new Color(0.357f, 0.357f, 0.357f) }
                };

            public static readonly GUIStyle centeredLabel = new GUIStyle(EditorStyles.largeLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };

            public static readonly GUIStyle titleLabel = new GUIStyle(centeredLabel) {fontSize = 18, clipping = TextClipping.Overflow};
            public static readonly GUIStyle wrappedLabel = new GUIStyle(GUI.skin.label) {wordWrap = true};

            public static readonly GUIStyle iconButton
                = new GUIStyle()
                {
                    padding = new RectOffset(1, 1, 1, 1),
                    margin = new RectOffset(),
                    alignment = TextAnchor.MiddleCenter
                };

            public static readonly GUIStyle backButton = new GUIStyle(iconButton)
            {
                contentOffset = new Vector2(0, 2),
            };

            public static readonly GUIStyle comicallyLargeButton = new GUIStyle(GUI.skin.button)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold
            };
        }

        public static class Content
        {
            public static readonly GUIContent greenLightIcon = EditorGUIUtility.IconContent("greenLight");
            public static readonly GUIContent orangeLightIcon = EditorGUIUtility.IconContent("orangeLight");
            public static readonly GUIContent backIcon = EditorGUIUtility.IconContent("back");
            public static readonly GUIContent warnIcon = new GUIContent(EditorGUIUtility.IconContent("console.warnicon.sml")){tooltip = "Transitions are not instant. This may change the behaviour of the toggle."};
            public static readonly GUIContent errorIcon = EditorGUIUtility.IconContent("CollabError");
        }

        public static void DrawSeparator(int thickness = 2, int padding = 10)
        {
            Rect r = EditorGUILayout.GetControlRect(GUILayout.Height(thickness + padding));
            r.height = thickness;
            r.y += padding / 2f;
            r.x -= 2;
            r.width += 6;
            ColorUtility.TryParseHtmlString(EditorGUIUtility.isProSkin ? "#595959" : "#858585", out Color lineColor);
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
                    if (autoFixAction != null)
                    {
                        using (new BGColoredScope(ColorOrange))
                            if (GUILayout.Button(fixLabel, GUILayout.ExpandWidth(false)))
                                autoFixAction();
                    }
                }
                

            }
        }

        public static void DrawValidated(string msg)
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label(Content.greenLightIcon, GUILayout.Width(28), GUILayout.Height(28));
                GUILayout.Label(msg, Styles.wrappedLabel, GUILayout.Height(28));
            }
        }
        public static void DrawWarning(string msg)
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label(Content.orangeLightIcon, GUILayout.Width(28), GUILayout.Height(28));
                GUILayout.Label(msg, Styles.wrappedLabel, GUILayout.Height(28));
            }
        }

        private static void DrawLightIcon(bool isGreen, string tooltip)
            => GUILayout.Label(new GUIContent(isGreen ? Content.greenLightIcon : Content.orangeLightIcon) { tooltip = tooltip }, GUILayout.Width(18), GUILayout.Height(18));
        public static void DrawGreenLightIcon(string tooltip)
            => DrawLightIcon(true, tooltip);
        public static void DrawOrangeLightIcon(string tooltip)
            => DrawLightIcon(false, tooltip);

        internal static void DoPlaceholderLabel(Rect r, string label, float minimumWidth = 0, float extraOffset = 0, GUIStyle customStyle = null)
        {
            if (!(r.width > minimumWidth + extraOffset)) return;
            r.x -= extraOffset;
            GUI.Label(r, label, customStyle ?? Styles.placeHolderLabel);
        }
        internal static void DoPlaceholderLabel(string label, float minimumWidth = 0, float extraOffset = 0, GUIStyle customStyle = null) => DoPlaceholderLabel(GUILayoutUtility.GetLastRect(), label, minimumWidth, extraOffset, customStyle);


        public static T QuickField<T>(this T target, GUIContent label) where T : Object
            =>  (T)EditorGUILayout.ObjectField(label, target, typeof(T), true);
        public static T QuickField<T>(this T target, string label) where T : Object 
            => target.QuickField(new GUIContent(label));

        public static Color ColorOrange = new Color(0.992f, 0.784f, 0.4f);
    }
}
