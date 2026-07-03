using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Sequencer.Builder;
using UnityEngine;

namespace TimboJimbo.Sequencer.Segments
{
	[Serializable]
	[AddSegmentMenu("Property Setter")]
	public class PropertySetter : Segment, IStartTimeConfigurable, IPlaybackBuilder
	{
		public float SetTime;
		public BindableProperty Property;
		public ValueContainer Value;
        
		public void SetStartTime(float startTime) => SetTime = startTime;
		public float GetStartTime() => SetTime;

		public override SegmentPlan GetPlan([CanBeNull] SegmentPlan parent)
		{
			if (!Property.IsValid)
			{
				return new SegmentPlan(this, parent)
				{
					Timing = { RelativeStartTime = SetTime, RelativeDuration = 0 }
				};
			}

			return new SegmentPlan(this, parent)
			{
				Bindings = { Properties = new HashSet<BindableProperty> { Property } },
				Timing = { RelativeStartTime = SetTime, RelativeDuration = 0 }
			};
		}

		public SegmentPlayback BuildPlayback(in PlaybackBuildContext context)
		{
			if (!Property.IsValid || context.PropertyBindings == null)
				return new NoOpPlayback(context);

			return new Playback(context)
			{
				BindingCollection = context.PropertyBindings,
				Property = Property,
                Value = Value
			};
		}

		private sealed class Playback : SegmentPlayback
		{
			public PropertyBindingCollection BindingCollection;
			public BindableProperty Property;
			public ValueContainer Value;

			public Playback(in PlaybackBuildContext context) : base(in context) { }

			public override void OnEnter(in PlaybackBoundaryContext context)
            {
                BindingCollection.TryWrite(Property, Value);
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
                Property = BindableProperty.CreateThreeComponent(target, "m_LocalPosition", ValueKind.Vector3, "x", "y", "z"),
                Value = ValueContainer.FromVector3(position)
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
                Property = BindableProperty.CreateThreeComponent(target, "m_LocalScale", ValueKind.Vector3, "x", "y", "z"),
                Value = ValueContainer.FromVector3(scale)
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
                Property = BindableProperty.CreateFourComponent(target, "m_LocalRotation", ValueKind.Quaternion, "x", "y", "z", "w"),
                Value = ValueContainer.FromQuaternion(rotation)
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
                Property = BindableProperty.CreateFourComponent(target, "m_LocalRotation", ValueKind.Quaternion, "x", "y", "z", "w"),
                Value = ValueContainer.FromQuaternion(Quaternion.Euler(eulerAngles))
            };
        }
    }
}
