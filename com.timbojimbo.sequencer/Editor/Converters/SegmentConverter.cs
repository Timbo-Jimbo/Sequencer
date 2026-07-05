using System;
using System.Collections.Generic;
using TimboJimbo.Sequencer;

namespace TimboJimboEditor.Sequencer.Converters
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class SegmentConverterAttribute : Attribute
    {
    }

    public abstract class SegmentConverter
    {
        /// Shown in the context menu under "Convert/".
        public abstract string MenuName { get; }

        public virtual int Priority => 0;

        /// True if this converter can consume the given selection of segments.
        public abstract bool CanConvert(IReadOnlyList<Segment> segments);

        /// Produce brand new segment instances from the input segments.
        /// The inputs will be deleted and the outputs inserted by the conversion pipeline.
        public abstract List<Segment> Convert(IReadOnlyList<Segment> segments);
    }
}
