using System;
using TimboJimbo.Sequencer;

namespace TimboJimboEditor.Sequencer
{
    public static class SegmentBlockEditorRegistry
    {
        public static SegmentBlockEditor GetEditor(Segment segment)
        {
            if (segment == null)
                return new SegmentBlockEditor();

            return GetEditorByType(segment.GetType());
        }

        public static SegmentBlockEditor GetEditorByType(Type segmentType)
        {
            if (EditorExtensionRegistry<CustomSegmentBlockEditorAttribute, SegmentBlockEditor>.TryGetExtension(segmentType, out var customEditor))
                return customEditor;
            return new SegmentBlockEditor();
        }
    }
}