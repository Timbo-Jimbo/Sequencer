using TimboJimbo.Sequencer;
using TimboJimbo.Sequencer.Segments;
using TimboJimbo.PropertyBindings;
using TimboJimboEditor.PropertyBindings.Utility;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimboJimboEditor.Sequencer.Segments
{
    [CustomSegmentEditor(typeof(PropertyTweener))]
    public sealed class PropertyTweenerEditor : SegmentEditor
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

        private static void DrawReadonlyBindablePropertyField(SerializedProperty bindablePropertyProp)
        {
            var targetProp = bindablePropertyProp.FindPropertyRelative("_target");
            var pathProp = bindablePropertyProp.FindPropertyRelative("_path");

            using (new EditorGUI.DisabledScope(true))
            {
                var rect = EditorGUILayout.GetControlRect();
                rect = EditorGUI.PrefixLabel(rect, new GUIContent("Property"));

                var gap = 4f;
                var objectWidth = rect.width * 0.45f;
                var pathRect = new Rect(rect.x + objectWidth + gap, rect.y, rect.width - objectWidth - gap, rect.height);
                var objectRect = new Rect(rect.x, rect.y, objectWidth, rect.height);

                EditorGUI.PropertyField(objectRect, targetProp, GUIContent.none);

                using (new EditorGUI.MixedValueScope(pathProp.hasMultipleDifferentValues))
                {
                    var path = pathProp.hasMultipleDifferentValues ? string.Empty : pathProp.stringValue;
                    EditorGUI.TextField(pathRect, path);
                }
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

        public override void OnBlockGUI(Segment segment, VisualElement block)
        {
            if (segment is not PropertyTweener tweener)
                return;

            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.FlexStart,
                    marginLeft = 4,
                    marginTop = 2,
                    overflow = Overflow.Hidden,
                },
                pickingMode = PickingMode.Ignore,
            };

            if (tweener.Property.Target is Object obj)
            {
                var compName = ObjectNames.NicifyVariableName(obj.GetType().Name);
                var textColumn = new VisualElement
                {
                    style =
                    {
                        flexDirection = FlexDirection.Column,
                        justifyContent = Justify.Center,
                        flexGrow = 1,
                        minWidth = 0,
                        overflow = Overflow.Hidden,
                    },
                    pickingMode = PickingMode.Ignore,
                };

                textColumn.Add(new Label($"{(obj is Component c ? c.gameObject.name : obj.name)}")
                {
                    style =
                    {
                        fontSize = 10,
                        unityFontStyleAndWeight = FontStyle.Bold,
                        color = new Color(0.941f, 0.941f, 0.941f, 1.000f),
                        overflow = Overflow.Hidden,
                    },
                    pickingMode = PickingMode.Ignore,
                });

                var detailRow = new VisualElement
                {
                    style =
                    {
                        flexDirection = FlexDirection.Row,
                        alignItems = Align.Center,
                        minWidth = 0,
                        overflow = Overflow.Hidden,
                    },
                    pickingMode = PickingMode.Ignore,
                };

                var iconContent = EditorGUIUtility.ObjectContent(obj, obj.GetType());
                if (iconContent.image != null)
                {
                    detailRow.Add(new Image
                    {
                        image = iconContent.image,
                        style =
                        {
                            width = 14,
                            height = 14,
                            marginRight = 3,
                            flexShrink = 0,
                        },
                        pickingMode = PickingMode.Ignore,
                    });
                }

                var propName = NicifiedPropertyNameFromPath(tweener.Property.Path);
                var detailsText = string.IsNullOrEmpty(propName) ? $"{compName} > (no property)" : $"{compName} > {propName}";
                detailRow.Add(new Label(detailsText)
                {
                    style =
                    {
                        fontSize = 10,
                        color = new Color(0.820f, 0.820f, 0.820f, 1.000f),
                        overflow = Overflow.Hidden,
                    },
                    pickingMode = PickingMode.Ignore,
                });

                textColumn.Add(detailRow);

                row.Add(textColumn);
            }
            else
            {
                row.Add(new Label("(no property)")
                {
                    style =
                    {
                        fontSize = 10,
                        unityFontStyleAndWeight = FontStyle.Bold,
                        color = new Color(0.945f, 0.945f, 0.945f, 1.000f),
                        overflow = Overflow.Hidden,
                    },
                    pickingMode = PickingMode.Ignore,
                });
            }

            block.Add(row);
        }

        private static string NicifiedPropertyNameFromPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            int lastDot = path.LastIndexOf('.');
            string last = lastDot >= 0 ? path.Substring(lastDot + 1) : path;
            return ObjectNames.NicifyVariableName(last);
        }

        protected override int GetBlockColorSeed(Segment segment)
        {
            if (segment is not PropertyTweener tweener || tweener.Property.Target is not Object obj)
                return base.GetBlockColorSeed(segment);

            return DeterministicHash($"{obj.GetType().FullName} ({tweener.Property.Path})");
        }

    }
}