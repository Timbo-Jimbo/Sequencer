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
        public static PropertySetter SetPosition(this SeqMake make, Transform target, Vector3 position)
            => make.Set(target, TransformProperties.LocalPosition, position);

        public static PropertySetter SetScale(this SeqMake make, Transform target, Vector3 scale)
            => make.Set(target, TransformProperties.LocalScale, scale);

        public static PropertySetter SetRotation(this SeqMake make, Transform target, Quaternion rotation)
            => make.Set(target, TransformProperties.LocalRotation, rotation);

        public static PropertySetter SetEulerRotation(this SeqMake make, Transform target, Vector3 eulerAngles)
            => make.Set(target, TransformProperties.LocalRotation, Quaternion.Euler(eulerAngles));
    }
}
