using System;
using System.Collections.Generic;
using TimboJimbo.Sequencer;
using UnityEditor;

namespace TimboJimboEditor.Sequencer
{
    public static class SegmentEditorRegistry
    {
        private static Dictionary<Type, Type> _editorTypes;

        private static void EnsureRegistry()
        {
            if (_editorTypes != null)
                return;

            _editorTypes = new Dictionary<Type, Type>();
            var foundTypes = TypeCache.GetTypesWithAttribute<CustomSegmentEditorAttribute>();
            foreach (var editorType in foundTypes)
            {
                if (editorType.IsAbstract || !typeof(SegmentEditor).IsAssignableFrom(editorType))
                    continue;

                var attributes = (CustomSegmentEditorAttribute[])editorType.GetCustomAttributes(typeof(CustomSegmentEditorAttribute), false);
                if (attributes.Length > 0)
                {
                    var inspectedType = attributes[0].InspectedType;
                    _editorTypes[inspectedType] = editorType;
                }
            }
        }

        public static SegmentEditor GetEditor(Segment segment)
        {
            if (segment == null)
                return new SegmentEditor();

            return GetEditorByType(segment.GetType());
        }

        public static SegmentEditor GetEditorByType(Type segmentType)
        {
            EnsureRegistry();
            if (_editorTypes.TryGetValue(segmentType, out var editorType))
            {
                try
                {
                    return (SegmentEditor)Activator.CreateInstance(editorType);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError($"Failed to instantiate SegmentEditor {editorType.Name}: {e.Message}");
                }
            }
            return new SegmentEditor();
        }
    }
}