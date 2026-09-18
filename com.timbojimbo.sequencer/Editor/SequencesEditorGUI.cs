using System;
using TimboJimboEditor.Core;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Sequencer
{
    /// <summary>Sequencer-specific GUI helpers. Foldouts and header buttons come from Core's <see cref="FoldoutGUI"/>.</summary>
    internal static class SequencesEditorGUI
    {
        public static bool AddButton(string label = null, string tooltip = null) => FoldoutGUI.AddButton(label, tooltip);
        public static bool KebabMenuButton(string label = null, string tooltip = null) => FoldoutGUI.KebabMenuButton(label, tooltip);
        public static bool GhostButton(string iconName, string label = null, string tooltip = null) => FoldoutGUI.GhostButton(iconName, label, tooltip);

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
    }
}
