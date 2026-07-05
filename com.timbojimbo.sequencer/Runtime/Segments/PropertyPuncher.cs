using System;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Sequencer.Builder;
using UnityEngine;

namespace TimboJimbo.Sequencer.Segments
{
	[Serializable]
	[AddSegmentMenu("Property Puncher")]
	public class PropertyPuncher : PropertySegment, IDurationConfigurable
	{
		public float Duration;
		public ValueContainer Strength;
		[Min(1)] public int Vibrato = 10;
		[Range(0f, 1f)] public float Elasticity = 1f;
		public bool Additive = true;

		public void SetDuration(float duration) => Duration = duration;
		public override float GetDuration() => Duration;

		protected override PropertyPlayback CreatePlayback(in PlaybackBuildContext context)
		{
			return new Playback(context)
			{
				Strength = Strength,
				Vibrato = Vibrato,
				Elasticity = Elasticity,
				Additive = Additive
			};
		}

		private sealed class Playback : OffsetPropertyPlayback
		{
			public int Vibrato;
			public float Elasticity;

			private float _vibrato;
			private float _elasticity;

			public Playback(in PlaybackBuildContext context) : base(in context)
			{
			}

			public override void Setup(in PlaybackSetupContext context)
			{
				base.Setup(context);
				_vibrato = Mathf.Max(1f, Vibrato);
				_elasticity = Mathf.Clamp01(Elasticity);
			}

			protected override ValueContainer SampleOffset(float normalizedTime, in ValueContainer strength)
			{
				var waveFactor = Mathf.Sin(normalizedTime * _vibrato * Mathf.PI) * ComputeDecay(normalizedTime);
				return ValueContainer.Scale(strength, waveFactor);
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
