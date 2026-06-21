using TimboJimbo.Sequencer.Segments;
using UnityEngine;

namespace TimboJimbo.Sequencer
{
    public class SequenceProvider : MonoBehaviour
    {
        public Sequence Sequence = new Sequence();

        public SegmentPlan GetPlan(SegmentPlan parent = null)
        {
            EnsureSequence();
            return Sequence.GetPlan(parent);
        }

        public SequenceInstance CreateInstance(bool isPreview = false, bool restoreValuesOnDispose = true)
        {
            EnsureSequence();
            return SequenceInstance.Create(Sequence, isPreview, restoreValuesOnDispose);
        }

        private void Reset()
        {
            EnsureSequence();
        }

        private void OnValidate()
        {
            EnsureSequence();
        }

        private void EnsureSequence()
        {
            Sequence ??= new Sequence();
            if (Sequence.BindingRoot == null)
                Sequence.BindingRoot = gameObject;
        }
    }
}