using TimboJimbo.Sequencer;
using TimboJimbo.Sequencer.Segments;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimboJimboEditor.Sequencer.Blocks
{
    [CustomSegmentBlockEditor(typeof(InsertSequenceProvider))]
    public sealed class InsertSequenceProviderBlockEditor : SegmentBlockEditor
    {
        public override void OnBlockGUI(Segment segment, VisualElement block)
        {
            if(segment is not InsertSequenceProvider embeddedSequenceProvider)
            {
                base.OnBlockGUI(segment, block);
                return;
            }
                
            var label = new Label("Insert Sequence Provider")
            {
                style =
                {
                    fontSize = 10,
                    marginLeft = 4,
                    marginTop = 2,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    color = new Color(0.85f, 0.88f, 0.95f),
                    overflow = Overflow.Hidden,
                },
                pickingMode = PickingMode.Ignore,
            };

            if(embeddedSequenceProvider.Provider != null)
                label.text = embeddedSequenceProvider.Provider.ToString();

            block.Add(label);

            var nestedPreview = CreateNestedPreview(embeddedSequenceProvider.GetPlan(null));
            block.Add(nestedPreview);
        }
    }
}