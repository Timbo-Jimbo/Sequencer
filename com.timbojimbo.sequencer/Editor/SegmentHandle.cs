using System;
using TimboJimbo.Sequencer;

namespace TimboJimboEditor.Sequencer
{
    [Serializable]
    public struct SegmentHandle : IEquatable<SegmentHandle>
    {
        public SequenceProvider Provider;
        public int Index;

        public SegmentHandle(SequenceProvider provider, int index)
        {
            Provider = provider;
            Index = index;
        }

        public readonly string PropertyPath => $"Sequence.Segments.Array.data[{Index}]";

        public readonly bool Equals(SegmentHandle other)
        {
            return ReferenceEquals(Provider, other.Provider) && Index == other.Index;
        }

        public override readonly bool Equals(object obj)
        {
            return obj is SegmentHandle other && Equals(other);
        }

        public override readonly int GetHashCode()
        {
            return HashCode.Combine(Provider, Index);
        }
    }
}
