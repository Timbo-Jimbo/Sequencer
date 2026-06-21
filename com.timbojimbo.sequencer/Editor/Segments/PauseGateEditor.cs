using System;
using System.Linq;
using System.Runtime.Serialization;
using TimboJimbo.Sequencer;
using TimboJimbo.Sequencer.Segments;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Sequencer.Segments
{
    [CustomSegmentEditor(typeof(PauseGate))]
    public sealed class PauseGateEditor : SegmentEditor
    {
        private static Type[] _serializableFactoryTypes;

        public override void OnInspectorGUI(Segment segment, SerializedProperty property)
        {
            var pauseAtProp = property.FindPropertyRelative("PauseAt");
            EditorGUILayout.PropertyField(pauseAtProp);

            var resumeCheckerProp = property.FindPropertyRelative("ResumeChecker");
            DrawResumeCheckerField(resumeCheckerProp);
        }

        private static void DrawResumeCheckerField(SerializedProperty property)
        {
            if (_serializableFactoryTypes == null)
            {
                _serializableFactoryTypes = TypeCache.GetTypesDerivedFrom<PauseGateResumeCheckerFactory>()
                    .Where(t => t.IsDefined(typeof(SerializableAttribute), false) && !t.IsAbstract)
                    .ToArray();
            }

            var currentValue = property.managedReferenceValue;
            var currentType = currentValue?.GetType();

            EditorGUILayout.BeginVertical();

            var rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            var labelRect = new Rect(rect.x, rect.y, EditorGUIUtility.labelWidth, rect.height);
            var typeRect = new Rect(rect.x + EditorGUIUtility.labelWidth, rect.y, rect.width - EditorGUIUtility.labelWidth, rect.height);

            if (property.hasVisibleChildren)
            {
                property.isExpanded = EditorGUI.Foldout(labelRect, property.isExpanded, property.displayName, true);
            }
            else
            {
                EditorGUI.LabelField(labelRect, property.displayName);
            }

            if (EditorGUI.DropdownButton(typeRect, new GUIContent(GetTypeDisplayName(currentType)), FocusType.Keyboard))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("None"), currentType == null, () =>
                {
                    property.managedReferenceValue = null;
                    property.serializedObject.ApplyModifiedProperties();
                    GUI.changed = true;
                });

                foreach (var type in _serializableFactoryTypes)
                {
                    var capturedType = type;
                    var typeName = ObjectNames.NicifyVariableName(type.Name);
                    menu.AddItem(
                        new GUIContent(typeName),
                        currentType == type,
                        () =>
                        {
                            property.managedReferenceValue = FormatterServices.GetUninitializedObject(capturedType);
                            property.serializedObject.ApplyModifiedProperties();
                            GUI.changed = true;
                        }
                    );
                }
                menu.DropDown(typeRect);
            }

            if (property.isExpanded && property.hasVisibleChildren && currentValue != null)
            {
                EditorGUI.indentLevel++;
                var iterator = property.Copy();
                var end = property.GetEndProperty();
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
                {
                    enterChildren = false;
                    EditorGUILayout.PropertyField(iterator, true);
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private static string GetTypeDisplayName(Type type)
        {
            if (type == null)
                return "None";
            return ObjectNames.NicifyVariableName(type.Name);
        }
    }
}