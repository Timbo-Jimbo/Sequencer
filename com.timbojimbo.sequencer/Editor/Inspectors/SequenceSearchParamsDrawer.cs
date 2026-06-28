using System;
using TimboJimbo.Sequencer.Segments;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Sequencer
{
    [CustomPropertyDrawer(typeof(SequenceSearchParams))]
    public sealed class SequenceSearchParamsDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var lineHeight = EditorGUIUtility.singleLineHeight;
            var spacing = EditorGUIUtility.standardVerticalSpacing;
            var y = position.y;
            var width = position.width;

            var titleRect = new Rect(position.x, y, width, lineHeight);
            EditorGUI.LabelField(titleRect, label);
            y += lineHeight + spacing;

            EditorGUI.indentLevel++;
            try
            {
                DrawLine(ref y, position.x, width, property.FindPropertyRelative("SearchRoot"), "Search Root");

                var limitDepthProp = property.FindPropertyRelative("LimitDepth");
                DrawLine(ref y, position.x, width, limitDepthProp, "Limit Depth");

                if (limitDepthProp != null && (limitDepthProp.hasMultipleDifferentValues || limitDepthProp.boolValue))
                {
                    var depthProp = property.FindPropertyRelative("Depth");
                    DrawLine(ref y, position.x, width, depthProp, "Depth");
                }

                DrawLine(ref y, position.x, width, property.FindPropertyRelative("ExcludeInactiveInHierarchy"), "Exclude Inactive In Hierarchy");
                DrawLine(ref y, position.x, width, property.FindPropertyRelative("ExcludeInactiveSelf"), "Exclude Inactive Self");

                var filterProviderProp = property.FindPropertyRelative("FilterByProviderName");
                DrawLine(ref y, position.x, width, filterProviderProp, "Filter By Provider Name");
                if (filterProviderProp != null && (filterProviderProp.hasMultipleDifferentValues || filterProviderProp.boolValue))
                {
                    var providerRegexProp = property.FindPropertyRelative("ProviderNameRegex");
                    DrawLine(ref y, position.x, width, providerRegexProp, "Provider Name Regex");
                }

                var filterSequenceProp = property.FindPropertyRelative("FilterBySequenceName");
                DrawLine(ref y, position.x, width, filterSequenceProp, "Filter By Sequence Name");
                if (filterSequenceProp != null && (filterSequenceProp.hasMultipleDifferentValues || filterSequenceProp.boolValue))
                {
                    var sequenceRegexProp = property.FindPropertyRelative("SequenceNameRegex");
                    DrawLine(ref y, position.x, width, sequenceRegexProp, "Sequence Name Regex");
                }
            }
            finally
            {
                EditorGUI.indentLevel--;
                EditorGUI.EndProperty();
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var lineHeight = EditorGUIUtility.singleLineHeight;
            var spacing = EditorGUIUtility.standardVerticalSpacing;
            float height = lineHeight + spacing;

            height += lineHeight + spacing; // SearchRoot
            height += lineHeight + spacing; // LimitDepth

            var limitDepthProp = property.FindPropertyRelative("LimitDepth");
            if (limitDepthProp != null && (limitDepthProp.hasMultipleDifferentValues || limitDepthProp.boolValue))
                height += lineHeight + spacing;

            height += lineHeight + spacing; // ExcludeInactiveInHierarchy
            height += lineHeight + spacing; // ExcludeInactiveSelf
            height += lineHeight + spacing; // FilterByProviderName

            var filterProviderProp = property.FindPropertyRelative("FilterByProviderName");
            if (filterProviderProp != null && (filterProviderProp.hasMultipleDifferentValues || filterProviderProp.boolValue))
                height += lineHeight + spacing;

            height += lineHeight + spacing; // FilterBySequenceName

            var filterSequenceProp = property.FindPropertyRelative("FilterBySequenceName");
            if (filterSequenceProp != null && (filterSequenceProp.hasMultipleDifferentValues || filterSequenceProp.boolValue))
                height += lineHeight + spacing;

            return height;
        }

        private static void DrawLine(ref float y, float x, float width, SerializedProperty property, string fallbackLabel)
        {
            if (property == null)
                return;

            var lineHeight = EditorGUI.GetPropertyHeight(property, new GUIContent(fallbackLabel), true);
            var rect = new Rect(x, y, width, lineHeight);
            EditorGUI.PropertyField(rect, property, new GUIContent(fallbackLabel), true);
            y += lineHeight + EditorGUIUtility.standardVerticalSpacing;
        }
    }
}
