using System;
using System.Collections.Generic;
using System.Linq;
using TimboJimbo.Sequencer;
using TimboJimbo.Sequencer.Segments;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Sequencer.Segments
{
    [CustomSegmentInspector(typeof(InsertSequenceProvider))]
    public sealed class InsertSequenceProviderSegmentInspector : SegmentInspector
    {
        public override void OnInspectorGUI(SerializedProperty segmentProperty)
        {
            var providerProp = segmentProperty.FindPropertyRelative("Provider");
            var sequenceNameProp = segmentProperty.FindPropertyRelative("SequenceName");
            var startTimeProp = segmentProperty.FindPropertyRelative("StartTime");

            if (providerProp == null || sequenceNameProp == null || startTimeProp == null)
            {
                DrawDefaultFields(segmentProperty);
                return;
            }

            EditorGUILayout.PropertyField(providerProp);
            DrawSequenceNamePopup(providerProp, sequenceNameProp);
            EditorGUILayout.PropertyField(startTimeProp);
        }

        private static void DrawSequenceNamePopup(SerializedProperty providerProp, SerializedProperty sequenceNameProp)
        {
            if (providerProp.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox("Select a single Sequence Provider to choose a sequence name.", MessageType.Info);
                return;
            }

            var provider = providerProp.objectReferenceValue as SequenceProvider;
            var sequenceNames = GetSequenceNames(provider);

            if (sequenceNames.Count == 0)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.Popup("Sequence Name", 0, new[] { "(none)" });
                return;
            }

            EnsureValidSequenceName(sequenceNameProp, sequenceNames);

            var currentName = sequenceNameProp.stringValue ?? string.Empty;
            var currentIndex = sequenceNames.IndexOf(currentName);
            if (currentIndex < 0)
                currentIndex = 0;
            var selectedIndex = EditorGUILayout.Popup("Sequence Name", currentIndex, sequenceNames.ToArray());

            if (selectedIndex == currentIndex)
                return;

            sequenceNameProp.stringValue = sequenceNames[selectedIndex];
            sequenceNameProp.serializedObject.ApplyModifiedProperties();
            GUI.changed = true;
        }

        private static void EnsureValidSequenceName(SerializedProperty sequenceNameProp, IReadOnlyList<string> sequenceNames)
        {
            var currentName = sequenceNameProp.stringValue ?? string.Empty;
            if (sequenceNames.Contains(currentName))
                return;

            sequenceNameProp.stringValue = sequenceNames[0];
        }

        private static List<string> GetSequenceNames(SequenceProvider provider)
        {
            var names = new List<string>();

            if (provider?.Sequences == null)
                return names;

            for (int i = 0; i < provider.Sequences.Count; i++)
            {
                var sequence = provider.Sequences[i];
                if (sequence == null || string.IsNullOrWhiteSpace(sequence.Name))
                    continue;

                names.Add(sequence.Name);
            }

            return names;
        }
    }
}
