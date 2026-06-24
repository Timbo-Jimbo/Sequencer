using System;
using System.Collections.Generic;

namespace TimboJimboEditor.Sequencer
{
    public static class SegmentRecorderRegistry
    {
        public static bool HasAnyRecorders()
        {
            return GetRecorderFor(typeof(TimboJimbo.Sequencer.Segments.PropertyTweener)) != null; // True if there are recorders
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