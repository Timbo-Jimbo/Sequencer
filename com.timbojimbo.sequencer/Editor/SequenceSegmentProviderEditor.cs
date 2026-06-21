using TimboJimbo.Sequencer;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Sequencer
{
    [CustomEditor(typeof(SequenceProvider))]
    public sealed class SequenceSegmentProviderEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            if (GUILayout.Button("Open Timeline"))
                SegmentTimelineWindow.Open((SequenceProvider)target);
        }
    }
}