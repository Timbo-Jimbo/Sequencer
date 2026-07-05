using System;
using System.Collections.Generic;

namespace TimboJimboEditor.Sequencer.Recorders
{
    public static class SegmentRecorderRegistry
    {
        public static bool HasAnyRecorders()
        {
            return EditorExtensionRegistry<CustomSegmentRecorderAttribute, SegmentRecorder>.HasAnyExtensions();
        }

        public static SegmentRecorder GetRecorderFor(Type segmentType)
        {
            if (EditorExtensionRegistry<CustomSegmentRecorderAttribute, SegmentRecorder>.TryGetExtension(segmentType, out var recorder))
                return recorder;
            return null;
        }

        public static IEnumerable<SegmentRecorder> GetAllRecorders()
        {
            var list = new List<SegmentRecorder>(EditorExtensionRegistry<CustomSegmentRecorderAttribute, SegmentRecorder>.GetAllExtensions());
            list.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            return list;
        }
    }
}