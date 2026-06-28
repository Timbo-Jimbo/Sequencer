using System.Collections.Generic;
using TimboJimbo.Sequencer.Segments;
using UnityEngine;

namespace TimboJimbo.Sequencer
{
    public class SequenceProvider : MonoBehaviour
    {
        public List<Sequence> Sequences = new();

        public bool TryGetPlan(string sequenceName, out SegmentPlan plan, SegmentPlan parent = null)
        {
            foreach (var sequence in Sequences)
            {
                if (sequence.Name == sequenceName)
                {
                    plan = sequence.GetPlan(parent);
                    return true;
                }
            }

            plan = null;
            return false;
        }

        public SegmentPlan GetPlan(string sequenceName, SegmentPlan parent = null)
        {
            foreach (var sequence in Sequences)
            {
                if (sequence.Name == sequenceName)
                {
                    return sequence.GetPlan(parent);
                }
            }

            throw new System.Exception($"Sequence with name '{sequenceName}' not found.");
        }

        public SequenceInstance CreateInstance(string sequenceName, bool isPreview = false, bool restoreValuesOnDispose = true)
        {
            foreach (var sequence in Sequences)
            {
                if (sequence.Name == sequenceName)
                {
                    return SequenceInstance.Create(sequence, isPreview, restoreValuesOnDispose);
                }
            }

            throw new System.Exception($"Sequence with name '{sequenceName}' not found.");
        }

        public SequenceInstance CreateInstance(string sequenceName, PlaybackRange playbackRange, bool isPreview = false, bool restoreValuesOnDispose = true)
        {
            foreach (var sequence in Sequences)
            {
                if (sequence.Name == sequenceName)
                {
                    return SequenceInstance.Create(sequence, playbackRange, isPreview, restoreValuesOnDispose);
                }
            }

            throw new System.Exception($"Sequence with name '{sequenceName}' not found.");
        }

    }
}