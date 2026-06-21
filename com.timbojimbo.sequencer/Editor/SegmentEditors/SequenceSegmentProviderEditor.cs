using UnityEditor;
using UnityEngine;

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
