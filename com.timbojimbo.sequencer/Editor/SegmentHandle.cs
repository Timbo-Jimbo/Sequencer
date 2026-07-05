using System;
using TimboJimbo.Sequencer;

namespace TimboJimboEditor.Sequencer
{
    [Serializable]
    public struct SegmentHandle : IEquatable<SegmentHandle>
    {
        public SequenceProvider Provider;
        public string SequenceName;
        public int Index;

        public SegmentHandle(SequenceProvider provider, string sequenceName, int index)
        {
            Provider = provider;
            SequenceName = sequenceName;
            Index = index;
        }

        public readonly string PropertyPath
        {
            get
            {
                if (Provider == null || Provider.Sequences == null || Provider.Sequences.Count == 0)
                    return string.Empty;

                var seqName = SequenceName ?? string.Empty;

                int sequenceIndex = Provider.Sequences.FindIndex(sequence => sequence != null && sequence.Name == seqName);
                if (sequenceIndex < 0)
                    return string.Empty;

                return $"Sequences.Array.data[{sequenceIndex}].Segments.Array.data[{Index}]";
            }
        }

        public readonly bool Equals(SegmentHandle other)
        {
            return ReferenceEquals(Provider, other.Provider)
                   && string.Equals(SequenceName, other.SequenceName, StringComparison.Ordinal)
                   && Index == other.Index;
        }

        public override readonly bool Equals(object obj)
        {
            return obj is SegmentHandle other && Equals(other);
        }

        public override readonly int GetHashCode()
        {
            return HashCode.Combine(Provider, SequenceName, Index);
        }
    }
}
