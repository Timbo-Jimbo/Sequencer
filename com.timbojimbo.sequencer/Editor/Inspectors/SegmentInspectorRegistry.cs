using System;
using TimboJimbo.Sequencer;

namespace TimboJimboEditor.Sequencer
{
    public static class SegmentInspectorRegistry
    {
        public static SegmentInspector GetInspector(Segment segment)
        {
            if (segment == null)
                return new SegmentInspector();

            return GetInspectorByType(segment.GetType());
        }

        public static SegmentInspector GetInspectorByType(Type segmentType)
        {
            if (EditorExtensionRegistry<CustomSegmentInspectorAttribute, SegmentInspector>.TryGetExtension(segmentType, out var customInspector))
                return customInspector;
            return new SegmentInspector();
        }
    }
}
