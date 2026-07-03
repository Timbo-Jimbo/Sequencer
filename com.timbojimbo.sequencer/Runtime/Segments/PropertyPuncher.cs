using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Sequencer.Builder;
using UnityEngine;

namespace TimboJimbo.Sequencer.Segments
{
	[Serializable]
	[AddSegmentMenu("Property Puncher")]
	public class PropertyPuncher : Segment, IStartTimeConfigurable, IDurationConfigurable, IPlaybackBuilder
	{
		public float StartTime;
		public float Duration;
		public BindableProperty Property;
		public ValueContainer Strength;
		[Min(1)] public int Vibrato = 10;
		[Range(0f, 1f)] public float Elasticity = 1f;
		public bool Additive = true;

		public void SetDuration(float duration) => Duration = duration;
		public float GetDuration() => Duration;
		public void SetStartTime(float startTime) => StartTime = startTime;
		public float GetStartTime() => StartTime;

		public override SegmentPlan GetPlan([CanBeNull] SegmentPlan parent)
		{
			if (!Property.IsValid)
			{
				return new SegmentPlan(this, parent)
				{
					Timing = { RelativeStartTime = StartTime, RelativeDuration = Duration }
				};
			}

			return new SegmentPlan(this, parent)
			{
				Bindings = { Properties = new HashSet<BindableProperty> { Property } },
				Timing = { RelativeStartTime = StartTime, RelativeDuration = Duration }
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
				Strength = Strength,
				Vibrato = Vibrato,
				Elasticity = Elasticity,
				Additive = Additive
			};
		}

		private sealed class Playback : SegmentPlayback
		{
			public PropertyBindingCollection BindingCollection;
			public BindableProperty Property;
			public ValueContainer Strength;
			public int Vibrato;
			public float Elasticity;
			public bool Additive;

			private ValueContainer _baseValue;
			private bool _baseValueInitialized;
			private float _vibrato;
			private float _elasticity;

			public Playback(in PlaybackBuildContext context) : base(in context)
			{
				ExecutionOrder = 100;
			}

			public override void Setup(in PlaybackSetupContext context)
			{
				_vibrato = Mathf.Max(1f, Vibrato);
				_elasticity = Mathf.Clamp01(Elasticity);
			}

			public override void OnEnter(in PlaybackBoundaryContext context)
			{
				EnsureBaseValue();

				if (!Additive)
					BindingCollection.TryWrite(Property, _baseValue);
			}

			public override void OnSample(in PlaybackSampleContext context)
			{
				EnsureBaseValue();

				var offsetValue = SampleOffsetValue(context.NormalizedTime);
				
				ValueContainer outputValue;
				if (Additive && BindingCollection.TryRead(Property, out var currentValue))
					outputValue = ValueContainer.Add(currentValue, offsetValue);
				else
					outputValue = ValueContainer.Add(_baseValue, offsetValue);

				BindingCollection.TryWrite(Property, outputValue);
			}

			public override void OnExit(in PlaybackBoundaryContext context)
			{
				EnsureBaseValue();
				BindingCollection.TryWrite(Property, _baseValue);
			}

			private void EnsureBaseValue()
			{
				if (_baseValueInitialized)
					return;

				if (!BindingCollection.TryRead(Property, out _baseValue))
					_baseValue = ValueContainer.FromDefault(Property.Kind);

				_baseValueInitialized = true;
			}

			private ValueContainer SampleOffsetValue(float normalizedTime)
			{
				var waveFactor = Mathf.Sin(normalizedTime * _vibrato * Mathf.PI) * ComputeDecay(normalizedTime);
				var strength = ResolveStrengthForProperty();

				return Property.Kind switch
				{
					ValueKind.Int => ValueContainer.FromInt(Mathf.RoundToInt(strength.IntValue * waveFactor)),
					ValueKind.Float => ValueContainer.FromFloat(strength.FloatValue * waveFactor),
					ValueKind.Vector2 => ValueContainer.FromVector2(strength.Vector2Value * waveFactor),
					ValueKind.Vector3 => ValueContainer.FromVector3(strength.Vector3Value * waveFactor),
					ValueKind.Vector4 => ValueContainer.FromVector4(strength.Vector4Value * waveFactor),
					ValueKind.Color => ValueContainer.FromColor(new Color(
						strength.ColorValue.r * waveFactor,
						strength.ColorValue.g * waveFactor,
						strength.ColorValue.b * waveFactor,
						strength.ColorValue.a * waveFactor)),
					ValueKind.Quaternion => ValueContainer.FromQuaternion(Quaternion.Euler(strength.QuaternionValue.eulerAngles * waveFactor)),
					_ => ValueContainer.FromDefault(Property.Kind)
				}			;
			}

			private ValueContainer ResolveStrengthForProperty()
			{
				if (Strength.Kind == Property.Kind)
					return Strength;

				return ValueContainer.FromDefault(Property.Kind);
			}

			private float ComputeDecay(float normalizedTime)
			{
				float decayFactor = (1f - _elasticity) * 10f;
				return Mathf.Max(0f, 1f - normalizedTime) * Mathf.Exp(-decayFactor * normalizedTime);
			}
		}
	}

	public static class PropertyPuncherExtensions
	{
		public static PropertyPuncher PunchPosition(
			this SeqMake _,
			Transform target,
			Vector3 strength,
			float duration,
			int vibrato = 10,
			float elasticity = 1f
		)
		{
			return new PropertyPuncher
			{
				Property = BindableProperty.CreateThreeComponent(target, "m_LocalPosition", ValueKind.Vector3, "x", "y", "z"),
				Strength = ValueContainer.FromVector3(strength),
				Duration = duration,
				Vibrato = vibrato,
				Elasticity = elasticity,
				Additive = true
			};
		}

		public static PropertyPuncher PunchScale(
			this SeqMake _,
			Transform target,
			Vector3 strength,
			float duration,
			int vibrato = 10,
			float elasticity = 1f
		)
		{
			return new PropertyPuncher
			{
				Property = BindableProperty.CreateThreeComponent(target, "m_LocalScale", ValueKind.Vector3, "x", "y", "z"),
				Strength = ValueContainer.FromVector3(strength),
				Duration = duration,
				Vibrato = vibrato,
				Elasticity = elasticity,
				Additive = true
			};
		}

		public static PropertyPuncher PunchRotation(
			this SeqMake _,
			Transform target,
			Vector3 strength,
			float duration,
			int vibrato = 10,
			float elasticity = 1f
		)
		{
			return new PropertyPuncher
			{
				Property = BindableProperty.CreateFourComponent(target, "m_LocalRotation", ValueKind.Quaternion, "x", "y", "z", "w"),
				Strength = ValueContainer.FromQuaternion(Quaternion.Euler(strength)),
				Duration = duration,
				Vibrato = vibrato,
				Elasticity = elasticity,
				Additive = true
			};
		}
	}
}
