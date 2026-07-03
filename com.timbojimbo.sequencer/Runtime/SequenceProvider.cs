using System;
using System.Collections.Generic;
using TimboJimbo.Sequencer.Segments;
using UnityEngine;

namespace TimboJimbo.Sequencer
{
    /// <summary>
    /// A serializable reference to a named <see cref="Sequence"/> on a <see cref="SequenceProvider"/>.
    /// This is the intended way for scripts to reference sequences: hold a <see cref="SequenceRef"/>
    /// field and call <see cref="Play"/> / <see cref="CreatePlayer"/> on it directly.
    /// </summary>
    [Serializable]
    public struct SequenceRef
    {
        public SequenceProvider Provider;
        public string SequenceName;

        /// <summary>True when the referenced provider exists and contains the named sequence.</summary>
        public bool IsValid => Provider != null && Provider.Contains(SequenceName);

        public bool TryResolve(out Sequence sequence)
        {
            if (Provider != null)
                return Provider.TryGetSequence(SequenceName, out sequence);

            sequence = null;
            return false;
        }

        public Sequence Resolve()
        {
            if (TryResolve(out var sequence))
                return sequence;

            throw new InvalidOperationException(
                $"ProviderSequence could not resolve a sequence (provider: '{(Provider != null ? Provider.name : "null")}', name: '{SequenceName}').");
        }

        /// <summary>Creates a player for the referenced sequence without starting it.</summary>
        public SequencePlayer CreatePlayer(bool isPreview = false, bool restoreValuesOnDispose = true, bool autoDrive = false)
        {
            return Resolve().CreatePlayer(isPreview, restoreValuesOnDispose, autoDrive);
        }

        /// <summary>Creates a self-driving player for the referenced sequence and starts it.</summary>
        public SequencePlayer Play(bool loop = false, float speed = 1f, bool restoreValuesOnDispose = true)
        {
            return Resolve().Play(loop, speed, restoreValuesOnDispose);
        }

        public static implicit operator Sequence(SequenceRef sequenceRef) => sequenceRef.Resolve();
    }

    public class SequenceProvider : MonoBehaviour
    {
        public List<Sequence> Sequences = new();

        /// <summary>
        /// Looks up a sequence by name. An empty/null name resolves to the first sequence when one exists.
        /// </summary>
        public bool TryGetSequence(string sequenceName, out Sequence sequence)
        {
            if (Sequences != null && Sequences.Count > 0)
            {
                if (string.IsNullOrEmpty(sequenceName))
                {
                    sequence = Sequences[0];
                    return sequence != null;
                }

                foreach (var candidate in Sequences)
                {
                    if (candidate != null && candidate.Name == sequenceName)
                    {
                        sequence = candidate;
                        return true;
                    }
                }
            }

            sequence = null;
            return false;
        }

        public bool Contains(string sequenceName) => TryGetSequence(sequenceName, out _);

        public Sequence GetSequence(string sequenceName)
        {
            if (TryGetSequence(sequenceName, out var sequence))
                return sequence;

            throw new Exception($"Sequence with name '{sequenceName}' not found.");
        }

        public bool TryGetPlan(string sequenceName, out SegmentPlan plan, SegmentPlan parent = null)
        {
            if (TryGetSequence(sequenceName, out var sequence))
            {
                plan = sequence.GetPlan(parent);
                return true;
            }

            plan = null;
            return false;
        }

        public SegmentPlan GetPlan(string sequenceName, SegmentPlan parent = null)
        {
            return GetSequence(sequenceName).GetPlan(parent);
        }

        /// <summary>Creates a player for the named sequence without starting it.</summary>
        public SequencePlayer CreatePlayer(string sequenceName, bool isPreview = false, bool restoreValuesOnDispose = true, bool autoDrive = false)
        {
            return GetSequence(sequenceName).CreatePlayer(isPreview, restoreValuesOnDispose, autoDrive);
        }

        /// <summary>Creates a player for the named sequence over a playback range, without starting it.</summary>
        public SequencePlayer CreatePlayer(string sequenceName, PlaybackRange playbackRange, bool isPreview = false, bool restoreValuesOnDispose = true, bool autoDrive = false)
        {
            return GetSequence(sequenceName).CreatePlayer(playbackRange, isPreview, restoreValuesOnDispose, autoDrive);
        }

        /// <summary>Creates a self-driving player for the named sequence and starts it.</summary>
        public SequencePlayer Play(string sequenceName, bool loop = false, float speed = 1f, bool restoreValuesOnDispose = true)
        {
            return GetSequence(sequenceName).Play(loop, speed, restoreValuesOnDispose);
        }
    }
}