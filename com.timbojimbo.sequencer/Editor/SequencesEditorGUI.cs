using System;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Sequencer
{
    internal static class SequencesEditorGUI
    {
        public static void DrawFoldout(
            bool expanded,
            Action drawContent,
            Action<bool> onToggle,
            Action<bool> onGroupToggle = null)
        {
            if (onGroupToggle == null)
                onGroupToggle = onToggle;

            using (var scope = new EditorGUILayout.HorizontalScope(Styles.FoldoutRowStyle, GUILayout.ExpandWidth(true)))
            {
                Event evt = Event.current;

                const float BleedAmount = 4000f;
                var rowRect = scope.rect;
                Rect bleedRect = new(
                    rowRect.x - BleedAmount,
                    rowRect.y,
                    rowRect.width + (BleedAmount * 2f),
                    rowRect.height);

                if (evt.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(bleedRect, Styles.FoldoutBackgroundColor);
                    EditorGUI.DrawRect(
                        new Rect(bleedRect.x, rowRect.y, bleedRect.width, Styles.FoldoutTopBorderThickness),
                        Styles.FoldoutBorderColor);

                    float arrowHeight = EditorGUIUtility.singleLineHeight;
                    Rect arrowRect = new(
                        rowRect.x,
                        rowRect.y + ((rowRect.height - arrowHeight) * 0.5f),
                        13f,
                        arrowHeight);
                    arrowRect.x -= 14f;
                    EditorStyles.foldout.Draw(arrowRect, GUIContent.none, false, false, expanded, false);
                }

                using (new GUILayout.HorizontalScope(GUILayout.MinHeight(EditorGUIUtility.singleLineHeight + 2)))
                {
                    drawContent?.Invoke();
                }

                bool toggled = false;
                bool wasGroupToggle = false;

                if (evt.type == EventType.MouseDown && evt.button == 0 && bleedRect.Contains(evt.mousePosition))
                {
                    toggled = true;
                    wasGroupToggle = evt.alt;
                    GUI.changed = true;
                    evt.Use();
                }

                if (toggled)
                {
                    expanded = !expanded;

                    if (wasGroupToggle)
                        onGroupToggle?.Invoke(expanded);
                    else
                        onToggle?.Invoke(expanded);
                }
            }
        }

        public static bool AddButton(string label = null, string tooltip = null) => GhostButton("Toolbar Plus", label, tooltip);
        public static bool KebabMenuButton(string label = null, string tooltip = null) => GhostButton("_Menu", label, tooltip);

        public static bool ButtonGroupButton(
            GUIContent content,
            int buttonIndex,
            int buttonCount,
            Action onClick = null,
            params GUILayoutOption[] options)
        {
            var leftStyle = EditorStyles.miniButtonLeft;
            var midStyle = EditorStyles.miniButtonMid;
            var rightStyle = EditorStyles.miniButtonRight;
            var standaloneStyle = EditorStyles.miniButton;

            GUIStyle style = buttonIndex switch
            {
                0 when buttonCount == 1 => standaloneStyle,
                0 => leftStyle,
                _ when buttonIndex == buttonCount - 1 => rightStyle,
                _ => midStyle,
            };

            if (GUILayout.Button(content, style, options))
            {
                onClick?.Invoke();
                return true;
            }

            return false;
        }

        public static bool GhostButton(string iconName, string label = null, string tooltip = null)
        {
            GUIContent icon = EditorGUIUtility.IconContent(iconName);
            GUIContent content = new GUIContent(label ?? icon.text, icon.image, tooltip ?? icon.tooltip);
            return GUILayout.Button(content, Styles.GhostIconStyle, GUILayout.ExpandWidth(false));
        }

        private static class Styles
        {
            private static readonly Color FoldoutBackgroundColorDarkSkin = new(0.19f, 0.19f, 0.19f, 1f);
            private static readonly Color FoldoutBackgroundColorLightSkin = new(0.74f, 0.74f, 0.74f, 1f);
            private static readonly Color FoldoutBorderColorDarkSkin = new(0f, 0f, 0f, 0.38f);
            private static readonly Color FoldoutBorderColorLightSkin = new(0f, 0f, 0f, 0.18f);

            private const float FoldoutContentLeftPadding = 0f;
            private const float FoldoutContentRightPadding = 0f;
            private const float FoldoutVerticalPadding = 0f;
            public const float FoldoutTopBorderThickness = 1f;

            public static Color FoldoutBackgroundColor => ForCurrentSkin(FoldoutBackgroundColorDarkSkin, FoldoutBackgroundColorLightSkin);
            public static Color FoldoutBorderColor => ForCurrentSkin(FoldoutBorderColorDarkSkin, FoldoutBorderColorLightSkin);

            public static GUIStyle GhostIconStyle => _ghostIconStyle ??= new GUIStyle(EditorStyles.iconButton)
            {
                alignment = TextAnchor.MiddleCenter,
                imagePosition = ImagePosition.ImageLeft,
                fontSize = EditorStyles.label.fontSize,
                fixedWidth = 0f,
                fixedHeight = EditorGUIUtility.singleLineHeight,
            };

            public static GUIStyle FoldoutRowStyle => _foldoutRowStyle ??= new GUIStyle
            {
                normal = { background = FoldoutBackgroundTexture },
                padding = new RectOffset(
                    (int)FoldoutContentLeftPadding,
                    (int)FoldoutContentRightPadding,
                    (int)(FoldoutTopBorderThickness + FoldoutVerticalPadding),
                    (int)FoldoutVerticalPadding),
                margin = new RectOffset(0, 0, 0, 0),
                stretchWidth = true,
            };

            private static GUIStyle _ghostIconStyle;
            private static GUIStyle _foldoutRowStyle;
            private static Texture2D _foldoutBackgroundTexture;
            private static bool _stylesUseProSkin;

            private static Texture2D FoldoutBackgroundTexture
            {
                get
                {
                    EnsureFoldoutTexture();
                    return _foldoutBackgroundTexture;
                }
            }

            private static Color ForCurrentSkin(Color darkSkinColor, Color lightSkinColor)
            {
                return EditorGUIUtility.isProSkin ? darkSkinColor : lightSkinColor;
            }

            private static void EnsureFoldoutTexture()
            {
                bool isProSkin = EditorGUIUtility.isProSkin;
                if (_foldoutBackgroundTexture != null && _stylesUseProSkin == isProSkin)
                    return;

                _stylesUseProSkin = isProSkin;

                if (_foldoutBackgroundTexture != null)
                    UnityEngine.Object.DestroyImmediate(_foldoutBackgroundTexture);

                _foldoutBackgroundTexture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                _foldoutBackgroundTexture.SetPixel(0, 0, FoldoutBackgroundColor);
                _foldoutBackgroundTexture.Apply();
            }
        }
    }
}