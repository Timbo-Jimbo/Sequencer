# Timbo Jimbo - Sequencer

A timeline-based animation sequencer for Unity. 

https://github.com/user-attachments/assets/9afb1039-1d5b-4b89-9c2c-cabced7b008b

This package is heavily inspired by [Animation Sequencer](https://github.com/brunomikoski/Animation-Sequencer). You should check it out! 

Animation Sequencer stepped up the game when it came to editor-authoring of sequence (Tweens, custom logic, etc). This package attempts to take it a step further by providing a visual timeline editor. Important to note that while conceptually similar, this package is *not* backed by an existing Tweening library like Animation Sequencer is.

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

Everything in a sequence is a `Segment`. A segment describes *what* happens and *when* via a `SegmentPlan`. The package ships with several built-in segment types (see below), and you can author your own.

## `Sequence`

`Sequence` is the root container. It has a `Name`, a start time, and a list of child `Segment`s — its duration is computed from them. Sequences can be nested inside other sequences to compose complex hierarchies.

Property binding roots are resolved automatically from the properties each segment drives — there is nothing to configure.

## `SequenceProvider` and `SequenceRef`

`SequenceProvider` is a `MonoBehaviour` that holds a list of named `Sequence`s you author in the Unity inspector.

`SequenceRef` is a serializable reference to a named sequence on a provider — the intended way for scripts to reference sequences:

```csharp
[SerializeField] private SequenceRef _intro;

void Start()
{
    // fire-and-forget: self-driving player, started immediately
    var player = _intro.Play();

    // or create one without starting it:
    var idle = _intro.CreatePlayer();
}
```

## `SequencePlayer`

`SequencePlayer` is the runtime handle for a playing sequence. Any `Segment` can be turned into a player via the `Play()` / `CreatePlayer()` extension methods (or `SequencePlayer.Create`).

```csharp
var player = mySequence.Play(loop: false, speed: 1f); // self-driving, starts immediately

// control playback:
player.Play();      // (re)start from the beginning
player.Pause();
player.Resume();
player.Stop();      // reset to start, restore initial values
player.Seek(2.5f);  // jump to absolute time 2.5s
player.SeekNormalized(0.5f);

// query state:
Debug.Log(player.State);    // Idle / Playing / Paused / Completed / Stopped
Debug.Log(player.Playhead);
Debug.Log(player.Duration);

// events:
player.Completed += p => Debug.Log("done!");

// awaitable:
await player;

// always dispose when done:
player.Dispose();
```

Players created via `Play()` are *self-driving* — they tick themselves each frame while playing. To drive one manually, use `CreatePlayer()` and call `player.Tick(Time.deltaTime)` yourself.

By default, `SequencePlayer` captures the initial property values when created and restores them on `Dispose`. Pass `restoreValuesOnDispose: false` to opt out.

# Built-in Segments

These are available out of the box and discoverable in the editor timeline UI:

- `PropertyTweener`
- `PropertySetter`
- `PropertyShaker`
- `PropertyPuncher`
- `CustomTweener`
- `CallbackGate`
- `PauseGate`
- `InsertSequenceProvider`
- `FindAndInsertSequenceProviders`

For how to compose these in code, see the **Code-First Authoring API** section below.

# Code-First Authoring API (`Seq.Schedule` + `Seq.Make`)

If you prefer to author sequences purely in code, this is the primary API surface.

`Seq.Schedule` and `Seq.Make` are lightweight static entry points (think of them like namespaces/folders for extension methods):

- `Seq.Schedule` focuses on *arrangement* (when segments run relative to each other)
- `Seq.Make` focuses on *construction* (creating segment instances and helpers)

Most of the API is provided via extension methods hanging off those two entry points.

`Seq.Schedule` provides composable arrangement helpers that let you describe timing relationships without manually assigning start times.

```csharp
// Run all at the same time (default for segments in a Sequence)
Seq.Schedule.Together(tweenA, tweenB, tweenC)

// Run one after the other
Seq.Schedule.OneAfterAnother(tweenA, tweenB, tweenC)

// Stagger by a fixed delay
Seq.Schedule.Stagger(0.1f, tweenA, tweenB, tweenC)

// Stagger by a proportion of each segment's duration
Seq.Schedule.ProportionalStagger(0.5f, tweenA, tweenB, tweenC)

// Delay before a segment
Seq.Schedule.Wait(0.5f, thenDoThisTween)

// Conditional inclusion
Seq.Schedule.If(someCondition, thenSegment, otherwiseSegment)

// Pause playback
Seq.Schedule.Pause(1f)
Seq.Schedule.PauseUntil(() => someCondition)
Seq.Schedule.PauseWhile(() => someCondition)
Seq.Schedule.PauseUntilClick(someButton)

// Fully custom arrangement logic
Seq.Schedule.CustomArrangement(ctx => ctx.Index * 0.2f, tweenA, tweenB, tweenC)
```

These all return a `Segment` that can be added to any `Sequence.Segments` list or nested further.

## Composed Example (`Seq.Schedule` + `Seq.Make`)

```csharp
_player = Seq.Schedule.Together(
    Seq.Schedule.Stagger(
        seconds: 0.25f,
        segments: found.Select(p => (Segment)p.Sequence)
    ),
    Seq.Make.TweenScale(
        target: SearchTarget.transform,
        start: TweenStart.Absolute(Vector3.zero),
        end: TweenEnd.Absolute(Vector3.one),
        duration: 2f,
        ease: EaseType.OutCubic
    ),
    Seq.Make.TweenRotation(
        target: SearchTarget.transform,
        start: TweenStart.Absolute(Quaternion.Euler(0, 180, 0)),
        end: TweenEnd.Absolute(Quaternion.Euler(0, 0, 0)),
        duration: 2f,
        ease: EaseType.OutCubic
    ),
    Seq.Schedule.If(
        isTrue: CustomTweenerLog,
        then: Seq.Make.TweenCustom(
            onSample: (float t) => Debug.Log($"Custom tween sample: {t}"),
            duration: 2f,
            ease: EaseType.OutCubic
        )
    ),
    Seq.Schedule.Wait(
        seconds: 1f,
        then: Seq.Schedule.Together(
            Seq.Make.Log("Waiting for space key press..."),
            Seq.Schedule.PauseUntil(
                returnsTrue: () => Keyboard.current.spaceKey.wasPressedThisFrame
            ),
            Seq.Make.Log("Space key pressed, resuming sequence.")
        )
    )
).Play();
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

        public override void OnSample(in PlaybackSampleContext context)
        {
            // context.NormalizedTime gives normalized time [0..1]
        }
    }
}
```

`[AddSegmentMenu("...")]` controls where your segment appears in the timeline's Add Segment menu. An empty string hides it from the menu.

For the code-first API, the recommended pattern is to expose your custom segments through extension methods on `Seq.Make` and/or `Seq.Schedule`, so they compose naturally with the built-in helpers.

You can see this pattern in the built-in implementations:

- `PauseGateExtensions` in `Runtime/Segments/PauseGate.cs`
- `PropertyTweenerExtensions` in `Runtime/Segments/PropertyTweener.cs`

# Editor Extensibility

The timeline editor is extensible too.

## Inspectors

Customize how a segment is drawn in the inspector panel by extending `SegmentInspector`:

```csharp
[CustomSegmentInspector(typeof(MySegment))]
public class MySegmentInspector : SegmentInspector
{
    public override void OnInspectorGUI(SerializedProperty segmentProperty)
    {
        // draw whatever you like - or fall back to the default:
        DrawDefaultFields(segmentProperty);
    }
}
```

## Block Editors

Customize how a segment's block is rendered on the timeline (label, contents, colors, lane grouping) by extending `SegmentBlockEditor`:

```csharp
[CustomSegmentBlockEditor(typeof(MySegment))]
public class MySegmentBlockEditor : SegmentBlockEditor
{
    public override void OnBlockGUI(Segment segment, VisualElement block)
    {
        DefaultBlockGUI(segment, block);
        // ...add extra UI Toolkit elements to the block
    }

    protected override int GetBlockColorSeed(Segment segment) => 42; // tint variation
}
```

## Converters

Add entries to the timeline's right-click **Convert To/...** menu by extending `SegmentConverter`. A converter consumes a selection of segments and produces replacements (inputs are deleted, outputs inserted, all in one undo group):

```csharp
[SegmentConverter]
public class MyConverter : SegmentConverter
{
    public override string MenuName => "My Converter";

    public override bool CanConvert(IReadOnlyList<Segment> segments)
        => segments.Count == 1 && segments[0] is MySegment;

    public override List<Segment> Convert(IReadOnlyList<Segment> segments)
        => new() { /* brand new segment instances */ };
}
```

The built-in converters (property family interchange, pack/unpack sequences) live in `Editor/Converters/BuiltInSegmentConverters.cs`.

## Recorders & Drag/Drop Resolvers

Two more advanced extension points exist:

- **Recorders** (`SegmentRecorder` + `[CustomSegmentRecorder]`) — let a segment type participate in the timeline's record mode. See `Editor/Recorders/PropertyTweenerRecorder.cs`.
- **Drag/Drop Resolvers** (`ITimelineDragDropSegmentResolver`) — turn objects dragged onto the timeline into segments. See `Editor/DragDrop/`.

Refer to the built-in implementations for these.

# AI Usage Disclosure

While the broader architecture and serialized data structures were designed by a human, this package was heavily authored with the aid of an LLM. The LLM was used extensively in Runtime logic, Editor tooling and Documentation.
