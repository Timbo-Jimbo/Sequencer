using System;
using TimboJimbo.PropertyBindings;
using UnityEngine;

namespace TimboJimbo.Sequencer.Segments
{
	public enum PropertyShakerSeedMode
	{
		Static,
		Randomized
	}

	[Serializable]
	[AddSegmentMenu("Property Shaker")]
	public class PropertyShaker : PropertySegment, IDurationConfigurable
	{
		public float Duration;
		public ValueContainer Strength;
		[Min(1)] public int Vibrato = 10;
		[Range(0f, 180f)] public float Randomness = 90f;
		[Range(0f, 1f)] public float FadeInFraction = 0f;
		[Range(0f, 1f)] public float FadeOutFraction = 0.2f;
		public PropertyShakerSeedMode SeedMode = PropertyShakerSeedMode.Static;
		public int Seed = 1;
		public bool Additive = true;

		public void SetDuration(float duration) => Duration = duration;
		public override float GetDuration() => Duration;

		protected override PropertyPlayback CreatePlayback(in PlaybackBuildContext context)
		{
			return new Playback(context)
			{
				Strength = Strength,
				Vibrato = Vibrato,
				Randomness = Randomness,
				FadeInFraction = FadeInFraction,
				FadeOutFraction = FadeOutFraction,
				SeedMode = SeedMode,
				Seed = Seed,
				Additive = Additive
			};
		}

		private sealed class Playback : OffsetPropertyPlayback
		{
			public int Vibrato;
			public float Randomness;
			public float FadeInFraction;
			public float FadeOutFraction;
			public PropertyShakerSeedMode SeedMode;
			public int Seed;

			private int _runtimeSeed;
			private float _noiseFrequency;
			private float _randomnessFactor;
			private float _fadeInFraction;
			private float _fadeOutFraction;

			public Playback(in PlaybackBuildContext context) : base(in context)
            {
            }

			public override void Setup(in PlaybackSetupContext context)
			{
				base.Setup(context);

				_runtimeSeed = SeedMode == PropertyShakerSeedMode.Randomized
					? Guid.NewGuid().GetHashCode()
					: Seed;

                if(context.IsPreview)
                    _runtimeSeed = HashCode.Combine(context.Sequence.GetHashCode(), AbsoluteStartTime, AbsoluteDuration);

				_noiseFrequency = Mathf.Max(1f, Vibrato);
				_randomnessFactor = Mathf.Clamp01(Randomness / 180f);
				_fadeInFraction = Mathf.Clamp01(FadeInFraction);
				_fadeOutFraction = Mathf.Clamp01(FadeOutFraction);
			}

			protected override ValueContainer SampleOffset(float normalizedTime, in ValueContainer strength)
			{
				var amplitude = ComputeAmplitude(normalizedTime);

				return Property.Kind switch
				{
					ValueKind.Int => ValueContainer.FromInt(Mathf.RoundToInt(strength.IntValue * SampleSignedNoise(0, normalizedTime) * amplitude)),
					ValueKind.Float => ValueContainer.FromFloat(strength.FloatValue * SampleSignedNoise(0, normalizedTime) * amplitude),
					ValueKind.Vector2 => ValueContainer.FromVector2(new Vector2(
						strength.Vector2Value.x * SampleSignedNoise(0, normalizedTime),
						strength.Vector2Value.y * SampleSignedNoise(1, normalizedTime)) * amplitude),
					ValueKind.Vector3 => ValueContainer.FromVector3(new Vector3(
						strength.Vector3Value.x * SampleSignedNoise(0, normalizedTime),
						strength.Vector3Value.y * SampleSignedNoise(1, normalizedTime),
						strength.Vector3Value.z * SampleSignedNoise(2, normalizedTime)) * amplitude),
					ValueKind.Vector4 => ValueContainer.FromVector4(new Vector4(
						strength.Vector4Value.x * SampleSignedNoise(0, normalizedTime),
						strength.Vector4Value.y * SampleSignedNoise(1, normalizedTime),
						strength.Vector4Value.z * SampleSignedNoise(2, normalizedTime),
						strength.Vector4Value.w * SampleSignedNoise(3, normalizedTime)) * amplitude),
					ValueKind.Color => ValueContainer.FromColor(new Color(
						strength.ColorValue.r * SampleSignedNoise(0, normalizedTime),
						strength.ColorValue.g * SampleSignedNoise(1, normalizedTime),
						strength.ColorValue.b * SampleSignedNoise(2, normalizedTime),
						strength.ColorValue.a * SampleSignedNoise(3, normalizedTime)) * amplitude),
					ValueKind.Quaternion => ValueContainer.FromQuaternion(Quaternion.Euler(new Vector3(
						strength.QuaternionValue.eulerAngles.x * SampleSignedNoise(0, normalizedTime),
						strength.QuaternionValue.eulerAngles.y * SampleSignedNoise(1, normalizedTime),
						strength.QuaternionValue.eulerAngles.z * SampleSignedNoise(2, normalizedTime)) * amplitude)),
					_ => ValueContainer.FromDefault(Property.Kind)
				};
			}

			private float SampleSignedNoise(int channel, float normalizedTime)
			{
				float channelOffsetScale = Mathf.Lerp(0f, 17.37f, _randomnessFactor);
				float timeOffsetScale = Mathf.Lerp(0f, 9.81f, _randomnessFactor);
				float channelSeedOffset = HashToUnit(_runtimeSeed + (channel + 1) * 92821) * 71.9f * _randomnessFactor;

				float x = _runtimeSeed * 0.00013f + channel * channelOffsetScale + channelSeedOffset;
				float y = normalizedTime * _noiseFrequency + channel * timeOffsetScale;
				return Mathf.PerlinNoise(x, y) * 2f - 1f;
			}

			private float ComputeAmplitude(float normalizedTime)
			{
				float t = Mathf.Clamp01(normalizedTime);
				float amplitude = 1f;

				if (_fadeInFraction > 0f)
				{
					float fadeInT = Mathf.Clamp01(t / _fadeInFraction);
					amplitude *= fadeInT;
				}

				if (_fadeOutFraction > 0f)
				{
					float fadeOutStart = 1f - _fadeOutFraction;
					if (t >= fadeOutStart)
					{
						float denom = Mathf.Max(0.0001f, _fadeOutFraction);
						float fadeOutT = Mathf.Clamp01((1f - t) / denom);
						amplitude *= fadeOutT;
					}
				}

				return amplitude;
			}

			private static float HashToUnit(int input)
			{
				unchecked
				{
					uint x = (uint)input;
					x ^= x >> 16;
					x *= 0x7feb352d;
					x ^= x >> 15;
					x *= 0x846ca68b;
					x ^= x >> 16;
					return (x & 0x00FFFFFFu) / 16777215f;
				}
			}
		}
	}
}
