using System.Collections.Generic;
using TimboJimbo.Sequencer;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Sequencer
{
    /// <summary>
    /// Draws a <see cref="SequenceRef"/> on a single line: an object field for the provider
    /// and a dropdown of that provider's sequence names (no magic-string typing).
    /// </summary>
    [CustomPropertyDrawer(typeof(SequenceRef))]
    public class SequenceRefDrawer : PropertyDrawer
    {
        private const float Spacing = 4f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var providerProp = property.FindPropertyRelative(nameof(SequenceRef.Provider));
            var nameProp = property.FindPropertyRelative(nameof(SequenceRef.SequenceName));

            var controlId = GUIUtility.GetControlID(FocusType.Passive);
            position = EditorGUI.PrefixLabel(position, controlId, label);

            var providerWidth = position.width * 0.6f - Spacing * 0.5f;
            var providerRect = new Rect(position.x, position.y, providerWidth, EditorGUIUtility.singleLineHeight);
            var nameRect = new Rect(providerRect.xMax + Spacing, position.y, position.width - providerWidth - Spacing, EditorGUIUtility.singleLineHeight);

            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            EditorGUI.PropertyField(providerRect, providerProp, GUIContent.none);
            DrawNameDropdown(nameRect, providerProp, nameProp);

            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }

        private static void DrawNameDropdown(Rect rect, SerializedProperty providerProp, SerializedProperty nameProp)
        {
            var provider = providerProp.objectReferenceValue as SequenceProvider;

            if (provider == null || provider.Sequences == null || provider.Sequences.Count == 0)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUI.Popup(rect, 0, new[] { provider == null ? "(no provider)" : "(no sequences)" });
                return;
            }

            var names = new List<string>(provider.Sequences.Count);
            foreach (var sequence in provider.Sequences)
            {
                if (sequence == null)
                    names.Add("(null)");
                else
                    names.Add(string.IsNullOrEmpty(sequence.Name) ? "(unnamed)" : sequence.Name);
            }

            var current = nameProp.stringValue;

            // Empty resolves to the first sequence at runtime, so highlight index 0.
            var selected = string.IsNullOrEmpty(current) ? 0 : IndexOfSequence(provider, current);

            if (selected < 0)
            {
                // The stored name no longer exists - surface it so it isn't silently lost.
                var options = new List<string>(names) { $"{current} (missing)" };
                var missingIndex = options.Count - 1;
                var newIndex = EditorGUI.Popup(rect, missingIndex, options.ToArray());
                if (newIndex != missingIndex)
                    nameProp.stringValue = provider.Sequences[newIndex].Name;
                return;
            }

            var picked = EditorGUI.Popup(rect, selected, names.ToArray());
            if (picked >= 0 && picked < provider.Sequences.Count)
                nameProp.stringValue = provider.Sequences[picked].Name;
        }

        private static int IndexOfSequence(SequenceProvider provider, string name)
        {
            for (int i = 0; i < provider.Sequences.Count; i++)
            {
                var sequence = provider.Sequences[i];
                if (sequence != null && sequence.Name == name)
                    return i;
            }

            return -1;
        }
    }
}
