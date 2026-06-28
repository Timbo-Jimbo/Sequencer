using TimboJimbo.PropertyBindings;
using TimboJimbo.Sequencer;
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

            DrawPropertyField(bindablePropertyProp, propertyKindProp, startValueProp, endValueProp);
            ValidatePropertyTarget(property, bindablePropertyProp);
            GUILayout.Space(8);
            
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

        private static void DrawPropertyField(
            SerializedProperty bindablePropertyProp,
            SerializedProperty propertyKindProp,
            SerializedProperty startValueProp,
            SerializedProperty endValueProp)
        {
            bool hadPreviousProperty = TryGetBindableProperty(bindablePropertyProp, out var previousProperty);
            EditorGUILayout.PropertyField(bindablePropertyProp, new GUIContent("Property"), includeChildren: true);

            if (bindablePropertyProp.hasMultipleDifferentValues || propertyKindProp == null || propertyKindProp.hasMultipleDifferentValues)
                return;

            if (!TryGetBindableProperty(bindablePropertyProp, out var currentProperty))
                return;

            bool propertyChanged = !hadPreviousProperty || !currentProperty.Equals(previousProperty);
            if (!propertyChanged)
                return;

            var kind = (ValueKind)propertyKindProp.enumValueIndex;
            if (kind == ValueKind.Invalid)
                return;

            var seedValue = ResolveSeedValue(bindablePropertyProp, kind);

            startValueProp.boxedValue = seedValue;
            endValueProp.boxedValue = seedValue;
        }

        private static void ValidatePropertyTarget(SerializedProperty segmentProperty, SerializedProperty bindablePropertyProp)
        {
            var segmentModel = segmentProperty.serializedObject.targetObject as SegmentSelectionModel;
            var provider = segmentModel?.Handle.Provider;
            if (provider == null)
                return;

            if (!TryGetBindableProperty(bindablePropertyProp, out var bindableProperty))
                return;

            if (!IsTargetOutsideProvider(provider, bindableProperty.Target))
                return;

            EditorUtility.DisplayDialog(
                "Invalid Property Target",
                "The target must be a child of the Sequence Provider.",
                "OK");

            ClearBindableProperty(bindablePropertyProp);
        }

        private static bool IsTargetOutsideProvider(SequenceProvider provider, UnityEngine.Object target)
        {
            if (provider == null || target == null)
                return false;

            var targetGo = GetTargetGameObject(target);
            if (targetGo == null)
                return false;

            var providerGo = provider.gameObject;
            if (providerGo == null)
                return true;

            return !targetGo.transform.IsChildOf(providerGo.transform);
        }

        private static void ClearBindableProperty(SerializedProperty bindablePropertyProp)
        {
            var targetProp = bindablePropertyProp.FindPropertyRelative("_target");
            var pathProp = bindablePropertyProp.FindPropertyRelative("_path");
            var kindProp = bindablePropertyProp.FindPropertyRelative("_kind");
            var componentLayoutProp = bindablePropertyProp.FindPropertyRelative("_componentLayout");
            var componentOnePathProp = bindablePropertyProp.FindPropertyRelative("_componentOnePath");
            var componentTwoPathProp = bindablePropertyProp.FindPropertyRelative("_componentTwoPath");
            var componentThreePathProp = bindablePropertyProp.FindPropertyRelative("_componentThreePath");
            var componentFourPathProp = bindablePropertyProp.FindPropertyRelative("_componentFourPath");

            if (targetProp == null || pathProp == null || kindProp == null || componentLayoutProp == null ||
                componentOnePathProp == null || componentTwoPathProp == null || componentThreePathProp == null || componentFourPathProp == null)
            {
                return;
            }

            targetProp.objectReferenceValue = null;
            pathProp.stringValue = string.Empty;
            kindProp.enumValueIndex = (int)ValueKind.Invalid;
            componentLayoutProp.enumValueIndex = (int)ComponentLayout.One;
            componentOnePathProp.stringValue = string.Empty;
            componentTwoPathProp.stringValue = string.Empty;
            componentThreePathProp.stringValue = string.Empty;
            componentFourPathProp.stringValue = string.Empty;
        }

        private static ValueContainer ResolveSeedValue(SerializedProperty bindablePropertyProp, ValueKind expectedKind)
        {
            if (TryGetBindableProperty(bindablePropertyProp, out var bindableProperty) &&
                TryReadCurrentValue(bindableProperty, out var currentValue) &&
                currentValue.Kind == expectedKind)
            {
                return currentValue;
            }

            return ValueContainer.FromDefault(expectedKind);
        }

        private static bool TryReadCurrentValue(BindableProperty bindableProperty, out ValueContainer value)
        {
            value = default;

            if (bindableProperty.Target == null || string.IsNullOrEmpty(bindableProperty.Path))
                return false;

            var root = GetTargetGameObject(bindableProperty.Target);
            if (root == null)
                return false;

            try
            {
                using var collection = PropertyBindingCollection.Bind(root, new[] { bindableProperty });
                return collection.TryRead(bindableProperty, out value);
            }
            catch
            {
                value = default;
                return false;
            }
        }

        private static GameObject GetTargetGameObject(UnityEngine.Object target)
        {
            return target switch
            {
                GameObject go => go,
                Component component => component.gameObject,
                _ => null
            };
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
