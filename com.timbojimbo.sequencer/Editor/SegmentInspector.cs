using System;
using UnityEditor;

namespace TimboJimboEditor.Sequencer
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class CustomSegmentInspectorAttribute : Attribute
    {
        public Type InspectedType { get; }

        public CustomSegmentInspectorAttribute(Type inspectedType)
        {
            InspectedType = inspectedType;
        }
    }

    public class SegmentInspector
    {
        public virtual void OnInspectorGUI(SerializedProperty segmentProperty) => DrawDefaultFields(segmentProperty);

        protected static void DrawDefaultFields(SerializedProperty property)
        {
            var iterator = property.Copy();
            var end = property.GetEndProperty();
            int childDepth = property.depth + 1;
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
            {
                enterChildren = false;
                if (iterator.depth != childDepth)
                    continue;

                EditorGUILayout.PropertyField(iterator, true);
            }
        }
    }
}
