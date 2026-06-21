# Timbo Jimbo - Sequencer

A timeline-based animation sequencer for Unity. Build sequences of tweens, callbacks, and pauses, arrange them in time, and play them back at runtime — driven by [Property Bindings](https://github.com/Timbo-Jimbo/PropertyBindings) under the hood.

> [!WARNING]
> This package is new - use at your own risk! :)

# Installation

This package is available on [OpenUPM](https://openupm.com/packages/com.timbojimbo.sequencer)

1. Add the Scoped Registry:
   - Open **Edit > Project Settings > Package Manager**
   - Add a new Scoped Registry (or append the missing scope if you already have one):
     - Name: `OpenUPM`
     - URL: `https://package.openupm.com/`
     - Scope(s): `com.timbojimbo`
2. Install the package
   - Open **Window > Package Manager**
   - Click Add and select **Add package by name...**
   - Paste name: `com.timbojimbo.sequencer`

Done!

<details>
<summary>Install from GitHub instead (Not Recommended)</summary>

You can also add it directly from GitHub on Unity 2019.4+. Note that you won't be able to receive updates through Package Manager this way, you'll have to update manually.

- First follow the installation instructions for the [Property Bindings package](https://github.com/Timbo-Jimbo/PropertyBindings)
- Once installed, open **Window > Package Manager**
- Click Add and select **Add package from git URL...**
- Paste `https://github.com/Timbo-Jimbo/Sequencer.git?path=com.timbojimbo.sequencer`
</details>

# Core Concepts

## `Segment`

Everything in a sequence is a `Segment`. A segment has a start time, a duration, and a way to play back. The package ships with several built-in segment types (see below), and you can author your own.

## `Sequence`

`Sequence` is the root container. It holds a list of child `Segment`s and computes its own duration from them. A `Sequence` also has a `BindingRoot` — the `GameObject` from which all `BindableProperty` paths in its children are resolved.

Sequences can be nested: a `Sequence` inside another `Sequence` gets its own `BindingRoot` and start time, so you can compose complex hierarchies.

## `SequenceProvider`

`SequenceProvider` is a `MonoBehaviour` wrapper that lets you author a `Sequence` in the Unity inspector and create instances of it at runtime.

```csharp
[SerializeField] private SequenceProvider _intro;

void Start()
{
    var instance = _intro.CreateInstance();
    // call instance.Tick(Time.deltaTime) each frame to drive it
}
```

## `SequenceInstance`

`SequenceInstance` is the runtime handle for a playing sequence. Obtain one from `SequenceProvider.CreateInstance` or `SequenceInstance.Create`.

```csharp
var instance = SequenceInstance.Create(mySequence);

// each frame:
instance.Tick(Time.deltaTime);

// control playback:
instance.Pause();
instance.Resume();
instance.Stop();
instance.Scrub(2.5f); // jump to absolute time 2.5s

// query state:
Debug.Log(instance.Playhead);
Debug.Log(instance.Duration);
Debug.Log(instance.IsPaused);

// always dispose when done:
instance.Dispose();
```

By default, `SequenceInstance` captures the initial property values when created and restores them on `Dispose`. Pass `restoreValuesOnDispose: false` to opt out.

# Built-in Segments

These are available out of the box and discoverable in the editor timeline UI:

- `PropertyTweener`
- `CallbackGate`
- `PauseGate`
- `InsertSequenceProvider`
- `FindAndInsertSequenceProviders`
- `CustomTweener`

For how to compose these in code, see the **Code-First Authoring API** section below.

# Code-First Authoring API (`Seg.Schedule` + `Seg.Make`)

If you prefer to author sequences purely in code, this is the primary API surface.

`Seg.Schedule` and `Seg.Make` are lightweight static entry points (think of them like namespaces/folders for extension methods):

- `Seg.Schedule` focuses on *arrangement* (when segments run relative to each other)
- `Seg.Make` focuses on *construction* (creating segment instances and helpers)

Most of the API is provided via extension methods hanging off those two entry points.

`Seg.Schedule` provides composable arrangement helpers that let you describe timing relationships without manually assigning start times.

```csharp
// Run all at the same time (default for segments in a Sequence)
Seg.Schedule.Together(tweenA, tweenB, tweenC)

// Run one after the other
Seg.Schedule.OneAfterAnother(tweenA, tweenB, tweenC)

// Stagger by a fixed delay
Seg.Schedule.Stagger(0.1f, tweenA, tweenB, tweenC)

// Stagger by a proportion of each segment's duration
Seg.Schedule.ProportionalStagger(0.5f, tweenA, tweenB, tweenC)

// Delay before a segment
Seg.Schedule.Wait(0.5f, thenDoThisTween)

// Conditional inclusion
Seg.Schedule.If(someCondition, thenSegment, otherwiseSegment)
```

These all return a `Segment` that can be added to any `Sequence.Segments` list or nested further.

## Composed Example (`Seg.Schedule` + `Seg.Make`)

```csharp
_instance = SequenceInstance.Create(
    Seg.Schedule.Together(
        Seg.Schedule.Stagger(
            seconds: 0.25f,
            segments: found.Select(p => (Segment)p.Sequence)
        ),
        Seg.Make.TweenGroup(
            withBindingRoot: SearchTarget,
            groupContent: tweenGroup => Seg.Schedule.Together(
                tweenGroup.Scale(
                    target: SearchTarget.transform,
                    start: TweenStart.Absolute(Vector3.zero),
                    end: TweenEnd.Absolute(Vector3.one),
                    duration: 2f,
                    ease: EaseType.OutCubic
                ),
                tweenGroup.Rotation(
                    target: SearchTarget.transform,
                    start: TweenStart.Absolute(Quaternion.Euler(0, 180, 0)),
                    end: TweenEnd.Absolute(Quaternion.Euler(0, 0, 0)),
                    duration: 2f,
                    ease: EaseType.OutCubic
                ),
                Seg.Schedule.If(
                    isTrue: CustomTweenerLog,
                    then: tweenGroup.Custom(
                        onSample: (t, ctx) =>
                        {
                            Debug.Log($"Custom tween sample: {t} | ({ctx.NormalizedTime * ctx.Duration}/{ctx.Duration})");
                        },
                        duration: 2f,
                        ease: EaseType.OutCubic
                    )
                )
            )
        ),
        Seg.Schedule.Wait(
            seconds: 1f,
            then: Seg.Schedule.Together(
                Seg.Make.Log("Waiting for space key press..."),
                Seg.Schedule.PauseUntil(
                    returnsTrue: () => Keyboard.current.spaceKey.wasPressedThisFrame
                ),
                Seg.Make.Log("Space key pressed, resuming sequence.")
            )
        )
    )
);
```

> [!TIP]
> This example uses `Keyboard.current` from the Input System package (`using UnityEngine.InputSystem;`).

# Authoring Sequences in the Inspector

Open **Window > Segment Timeline** to get a timeline view for a selected `SequenceProvider`. From there you can:

- Add segments via right-click (any type tagged with `[AddSegmentMenu]`)
- Drag segment start times and durations on the timeline
- Preview the sequence in Edit Mode by scrubbing the playhead

# Custom Segments

Implement `Segment` and, optionally, `IPlaybackBuilder` to define your own segment type.

```csharp
[Serializable]
[AddSegmentMenu("My Segments/My Custom Segment")]
public class MySegment : Segment, IStartTimeConfigurable, IDurationConfigurable, IPlaybackBuilder
{
    public float StartTime;
    public float Duration = 1f;

    public void SetStartTime(float t) => StartTime = t;
    public float GetStartTime() => StartTime;
    public void SetDuration(float d) => Duration = d;
    public float GetDuration() => Duration;

    public override SegmentPlan GetPlan(SegmentPlan parent)
    {
        return new SegmentPlan(this, parent)
        {
            Timing = { RelativeStartTime = StartTime, RelativeDuration = Duration }
        };
    }

    public SegmentPlayback BuildPlayback(in PlaybackBuildContext context)
    {
        return new Playback(context);
    }

    private class Playback : SegmentPlayback
    {
        public Playback(in PlaybackBuildContext context) : base(in context) { }

        public override void OnTick(in PlaybackTickContext context)
        {
            // context.Progress01 gives normalized time [0..1]
        }
    }
}
```

`[AddSegmentMenu("...")]` controls where your segment appears in the timeline's Add Segment menu. An empty string hides it from the menu.

For the code-first API, the recommended pattern is to expose your custom segments through extension methods on `Seg.Make` and/or `Seg.Schedule`, so they compose naturally with the built-in helpers.

You can see this pattern in the built-in implementations:

- `PauseGateExtensions` in `Runtime/Segments/PauseGate.cs`
- `PropertyTweenerExtensions` in `Runtime/Segments/PropertyTweener.cs`

# AI Usage Disclosure

The overall architecture and serialized data structures were designed by a human. An LLM was used in some Runtime logic, and used extensively in Editor Tooling and Documentation.
