using System;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Sequencer.Builder;
using UnityEngine;

namespace TimboJimbo.Sequencer.Segments
{
	[Serializable]
	[AddSegmentMenu("Property Setter")]
	public class PropertySetter : PropertySegment
	{
		public ValueContainer Value;

		/// Only meaningful when this is the earliest segment targeting its property.
		public PreExtrapolationMode PreExtrapolation = PreExtrapolationMode.Hold;

		protected override PropertyPlayback CreatePlayback(in PlaybackBuildContext context)
		{
			return new Playback(context)
			{
                Value = Value,
                PreExtrapolation = PreExtrapolation
			};
		}

		private sealed class Playback : PropertyPlayback
		{
			public ValueContainer Value;
			public PreExtrapolationMode PreExtrapolation;

			public Playback(in PlaybackBuildContext context) : base(in context) { }

			public override void OnEnter(in PlaybackBoundaryContext context)
            {
                BindingCollection.TryWrite(Property, Value);
            }

            public override bool TryGetPreExtrapolationValue(out ValueContainer value)
            {
                if (PreExtrapolation == PreExtrapolationMode.Hold)
                {
                    value = Value;
                    return true;
                }

                value = default;
                return false;
            }
		}
	}
    

    public static class PropertySetterExtensions
    {
        public static PropertySetter SetPosition(
            this SeqMake _,
            Transform target, 
            Vector3 position
        )
        {
            return new PropertySetter
            {
                Property = BindableProperty.Create(target, TransformProperties.LocalPosition),
                Value = ValueContainer.From(position)
            };
        }

        public static PropertySetter SetScale(
            this SeqMake _,
            Transform target, 
            Vector3 scale
        )
        {
            return new PropertySetter
            {
                Property = BindableProperty.Create(target, TransformProperties.LocalScale),
                Value = ValueContainer.From(scale)
            };
        }

        public static PropertySetter SetRotation(
            this SeqMake _,
            Transform target, 
            Quaternion rotation
        )
        {
            return new PropertySetter
            {
                Property = BindableProperty.Create(target, TransformProperties.LocalRotation),
                Value = ValueContainer.From(rotation)
            };
        }

        public static PropertySetter SetEulerRotation(
            this SeqMake _,
            Transform target, 
            Vector3 eulerAngles
        )
        {
            return new PropertySetter
            {
                Property = BindableProperty.Create(target, TransformProperties.LocalRotation),
                Value = ValueContainer.From(Quaternion.Euler(eulerAngles))
            };
        }
    }
}
