using TimboJimbo.PropertyBindings;
using TimboJimbo.Sequencer.Segments;
using TimboJimboEditor.PropertyBindings.Utility;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Sequencer.Segments
{
    [CustomSegmentInspector(typeof(PropertyTweener))]
    public sealed class PropertyTweenerSegmentInspector : SegmentInspector
    {
        private static GUIStyle MixedTypesStyle;

        public override void OnInspectorGUI(SerializedProperty property)
        {
            var startTimeProp = property.FindPropertyRelative("StartTime");
            var durationProp = property.FindPropertyRelative("Duration");
            var bindablePropertyProp = property.FindPropertyRelative("Property");
            var easeProp = property.FindPropertyRelative("Ease");
            var startModeProp = property.FindPropertyRelative("StartMode");
            var endModeProp = property.FindPropertyRelative("EndMode");
            var startValueProp = property.FindPropertyRelative("StartValue");
            var endValueProp = property.FindPropertyRelative("EndValue");
            var interpolationProp = property.FindPropertyRelative("Interpolation");
            var discreteValueSelectionProp = property.FindPropertyRelative("DiscreteValueSelection");
            var propertyKindProp = bindablePropertyProp.FindPropertyRelative("_kind");

            EditorGUILayout.PropertyField(startTimeProp);
            EditorGUILayout.PropertyField(durationProp);
            EditorGUILayout.PropertyField(easeProp);
            GUILayout.Space(8);

            EditorGUILayout.PropertyField(startModeProp);

            var shouldDrawStartValue = startModeProp.hasMultipleDifferentValues ||
                                       startModeProp.enumValueIndex != (int)EasedStartMode.StartFromCurrent;
            if (shouldDrawStartValue)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    if (bindablePropertyProp.hasMultipleDifferentValues)
                        DrawMixedPropertiesLabel("Value");
                    else
                        DrawValueContainerField(startValueProp, bindablePropertyProp, "Value");
                }
            }

            EditorGUILayout.PropertyField(endModeProp);
            var shouldDrawEndValue = endModeProp.hasMultipleDifferentValues ||
                                     endModeProp.enumValueIndex != (int)EasedEndMode.EndAtInitial;
            if (shouldDrawEndValue)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    if (bindablePropertyProp.hasMultipleDifferentValues)
                        DrawMixedPropertiesLabel("Value");
                    else
                        DrawValueContainerField(endValueProp, bindablePropertyProp, "Value");
                }
            }

            GUILayout.Space(8);
            DrawTypeSpecificModePickers(propertyKindProp, interpolationProp, discreteValueSelectionProp);
        }

        private static void DrawTypeSpecificModePickers(SerializedProperty propertyKindProp, SerializedProperty interpolationProp, SerializedProperty discreteValueSelectionProp)
        {
            if (propertyKindProp == null)
                return;

            if (propertyKindProp.hasMultipleDifferentValues)
            {
                DrawMixedPropertiesLabel("Interpolation");
                DrawMixedPropertiesLabel("Discrete Value Selection");
                return;
            }

            var kind = (ValueKind)propertyKindProp.enumValueIndex;
            switch (kind)
            {
                case ValueKind.Vector2:
                    EditorGUILayout.PropertyField(interpolationProp.FindPropertyRelative("Vector2"), new GUIContent("Interpolation"));
                    break;
                case ValueKind.Vector3:
                    EditorGUILayout.PropertyField(interpolationProp.FindPropertyRelative("Vector3"), new GUIContent("Interpolation"));
                    break;
                case ValueKind.Color:
                    EditorGUILayout.PropertyField(interpolationProp.FindPropertyRelative("Color"), new GUIContent("Interpolation"));
                    break;
                case ValueKind.Quaternion:
                    EditorGUILayout.PropertyField(interpolationProp.FindPropertyRelative("Rotation"), new GUIContent("Interpolation"));
                    break;
            }

            switch (kind)
            {
                case ValueKind.Int:
                case ValueKind.Bool:
                case ValueKind.Enum:
                case ValueKind.Reference:
                case ValueKind.String:
                    EditorGUILayout.PropertyField(discreteValueSelectionProp, new GUIContent("Discrete Value Selection"));
                    break;
            }
        }

        private static void DrawValueContainerField(SerializedProperty valueProp, SerializedProperty bindablePropertyProp, string label)
        {
            if (!TryGetBindableProperty(bindablePropertyProp, out var bindableProperty) ||
                !TryGetValueContainer(valueProp, out var currentValue))
            {
                return;
            }

            var rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            using (new EditorGUI.MixedValueScope(valueProp.hasMultipleDifferentValues || bindablePropertyProp.hasMultipleDifferentValues))
            {
                EditorGUI.BeginChangeCheck();
                var nextValue = PropertyBindingsEditorGUI.ValueContainerField(rect, new GUIContent(label), bindableProperty, currentValue);
                if (EditorGUI.EndChangeCheck())
                    valueProp.boxedValue = nextValue;
            }
        }

        private static void DrawMixedPropertiesLabel(string label)
        {
            if (MixedTypesStyle == null)
            {
                MixedTypesStyle = new GUIStyle(EditorStyles.label)
                {
                    fontStyle = FontStyle.Italic,
                    fontSize = 12,
                    normal = { textColor = new Color(0.8f, 0.8f, 0.8f) }
                };
            }

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.LabelField(label, "— (Multi-Segment Editing)", MixedTypesStyle);
        }

        private static bool TryGetBindableProperty(SerializedProperty bindablePropertyProp, out BindableProperty bindableProperty)
        {
            if (bindablePropertyProp.hasMultipleDifferentValues)
            {
                bindableProperty = default;
                return false;
            }

            bindableProperty = (BindableProperty)bindablePropertyProp.boxedValue;
            return true;
        }

        private static bool TryGetValueContainer(SerializedProperty valueProp, out ValueContainer value)
        {
            if (valueProp.hasMultipleDifferentValues)
            {
                value = default;
                return false;
            }

            value = (ValueContainer)valueProp.boxedValue;
            return true;
        }
    }
}
