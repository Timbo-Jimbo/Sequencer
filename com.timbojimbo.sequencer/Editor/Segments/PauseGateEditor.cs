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

        public override void OnInspectorGUI(SerializedProperty property)
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
            
            if(property.serializedObject.targetObjects.Length > 1)
            {
                EditorGUILayout.LabelField(property.displayName, "Multi-object editing not supported");
                EditorGUILayout.EndVertical();
                return;
            }

            var optionNames = new string[_serializableFactoryTypes.Length + 1];
            optionNames[0] = "None";
            var currentIndex = 0;

            for (var i = 0; i < _serializableFactoryTypes.Length; i++)
            {
                var type = _serializableFactoryTypes[i];
                var typeName = ObjectNames.NicifyVariableName(type.Name);
                optionNames[i + 1] = typeName;

                if (currentType == type)
                {
                    currentIndex = i + 1;
                }
            }

            EditorGUI.BeginChangeCheck();
            var selectedIndex = EditorGUILayout.Popup(property.displayName, currentIndex, optionNames);
            if (EditorGUI.EndChangeCheck())
            {
                if (selectedIndex == 0)
                {
                    property.managedReferenceValue = null;
                }
                else
                {
                    var selectedType = _serializableFactoryTypes[selectedIndex - 1];
                    property.managedReferenceValue = FormatterServices.GetUninitializedObject(selectedType);
                }

                property.serializedObject.ApplyModifiedProperties();
                GUI.changed = true;
            }

            if (property.hasVisibleChildren && currentValue != null)
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