using TimboJimbo.PropertyBindings;
using TimboJimbo.Sequencer;
using TimboJimbo.Sequencer.Segments;
using TimboJimboEditor.PropertyBindings.Utility;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Sequencer.Segments
{
    [CustomSegmentInspector(typeof(PropertySetter))]
    public sealed class PropertySetterSegmentInspector : SegmentInspector
    {
        public override void OnInspectorGUI(SerializedProperty property)
        {
            var setTimeProp = property.FindPropertyRelative("StartTime");
            var bindablePropertyProp = property.FindPropertyRelative("Property");
            var valueProp = property.FindPropertyRelative("Value");
            var preExtrapolationProp = property.FindPropertyRelative("PreExtrapolation");
            var kindProp = bindablePropertyProp.FindPropertyRelative("_kind");

            DrawPropertyField(bindablePropertyProp, kindProp, valueProp);
            ValidatePropertyTarget(property, bindablePropertyProp);
            GUILayout.Space(8);

            EditorGUILayout.PropertyField(setTimeProp);
            GUILayout.Space(8);

            DrawValueField(valueProp, bindablePropertyProp);
            EditorGUILayout.PropertyField(preExtrapolationProp, new GUIContent(
                "Pre-Extrapolation",
                "When this is the earliest segment targeting its property, 'Hold' writes the " +
                "value up-front so the property holds it until the set time is reached."));
        }

        private static void DrawPropertyField(
            SerializedProperty bindablePropertyProp,
            SerializedProperty kindProp,
            SerializedProperty valueProp)
        {
            bool hadProperty = TryGetBindableProperty(bindablePropertyProp, out var previousProperty);
            EditorGUILayout.PropertyField(bindablePropertyProp, new GUIContent("Property"), includeChildren: true);

            if (bindablePropertyProp.hasMultipleDifferentValues || kindProp == null || kindProp.hasMultipleDifferentValues)
                return;

            if (!TryGetBindableProperty(bindablePropertyProp, out var currentProperty))
                return;

            if (hadProperty && currentProperty.Equals(previousProperty))
                return;

            var kind = (ValueKind)kindProp.enumValueIndex;
            if (kind == ValueKind.Invalid)
                return;

            valueProp.boxedValue = ValueContainer.FromDefault(kind);
        }

        private static void DrawValueField(SerializedProperty valueProp, SerializedProperty bindablePropertyProp)
        {
            if (bindablePropertyProp.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox("Select a single property to edit Value.", MessageType.Info);
                return;
            }

            if (!TryGetBindableProperty(bindablePropertyProp, out var bindableProperty))
            {
                EditorGUILayout.HelpBox("Choose a valid property first.", MessageType.Info);
                return;
            }

            if (!TryGetValueContainer(valueProp, out var currentValue))
                currentValue = ValueContainer.FromDefault(bindableProperty.Kind);

            var rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            using (new EditorGUI.MixedValueScope(valueProp.hasMultipleDifferentValues))
            {
                EditorGUI.BeginChangeCheck();
                var nextValue = PropertyBindingsEditorGUI.ValueContainerField(rect, new GUIContent("Value"), bindableProperty, currentValue);
                if (EditorGUI.EndChangeCheck())
                    valueProp.boxedValue = nextValue;
            }
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

        private static GameObject GetTargetGameObject(UnityEngine.Object target)
        {
            return target switch
            {
                GameObject go => go,
                Component component => component.gameObject,
                _ => null
            };
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
