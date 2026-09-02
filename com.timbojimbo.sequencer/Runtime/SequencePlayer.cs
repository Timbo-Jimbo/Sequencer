using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Sequencer.Segments;
using UnityEngine;
using UnityEngine.Pool;

namespace TimboJimbo.Sequencer
{
    public enum SequencePlaybackState
    {
        /// <summary>Created but never started. Holds the initial frame.</summary>
        Idle,
        Playing,
        Paused,
        /// <summary>Reached the end of the playback range (non-looping). Holds the final frame.</summary>
        Completed,
        /// <summary>Explicitly stopped. Reset to the start, initial values restored.</summary>
        Stopped
    }

    /// <summary>
    /// A playable instance of a <see cref="Sequence"/>.
    ///
    /// Responsibilities are split into focused units:
    ///   - <see cref="SequenceCompiler"/> / <see cref="CompiledSequence"/> : build-time artifacts (bindings, playbacks, timeline).
    ///   - <see cref="BindingSet"/>     : property-binding lifecycle (bulk-write scope, restore values).
    ///   - <see cref="KeyframeTimeline"/>: the sorted enter/exit keyframe model.
    ///   - <see cref="PlaybackRuntime"/> : the time-stepping engine.
    ///   - <see cref="SequencePlayer"/>: the public control surface, state machine, and events.
    /// </summary>
    public sealed class SequencePlayer : IDisposable
    {
        private const float TimeEpsilon = 0.00001f;

        private readonly bool _isPreview;
        private readonly bool _restoreValuesOnDispose;
        private readonly bool _autoDrive;
        private readonly CompiledSequence _compiled;
        private readonly PlaybackRuntime _runtime;

        private SequencePlaybackState _state;
        private PlaybackRange _playbackRange;
        private float _speed = 1f;
        private bool _loop;
        private bool _isDisposed;

        /// <summary>Raised when playback starts from the beginning (via <see cref="Play"/>).</summary>
        public event Action<SequencePlayer> Started;
        /// <summary>Raised when playback is paused.</summary>
        public event Action<SequencePlayer> Paused;
        /// <summary>Raised when playback resumes from a paused state.</summary>
        public event Action<SequencePlayer> Resumed;
        /// <summary>Raised when playback is explicitly stopped.</summary>
        public event Action<SequencePlayer> Stopped;
        /// <summary>Raised when the playhead reaches the end of the range and looping is disabled.</summary>
        public event Action<SequencePlayer> Completed;
        /// <summary>Raised each time a looping sequence wraps back to the start.</summary>
        public event Action<SequencePlayer> Looped;

        public float Duration => _compiled.Duration;
        public float Playhead => _runtime.Playhead;
        public float NormalizedTime => Duration > TimeEpsilon ? Mathf.Clamp01(Playhead / Duration) : 0f;

        public SequencePlaybackState State => _state;
        public bool IsPlaying => _state == SequencePlaybackState.Playing;
        public bool IsPaused => _state == SequencePlaybackState.Paused;
        public bool IsStopped => _state == SequencePlaybackState.Stopped;
        public bool IsComplete => _state == SequencePlaybackState.Completed;
        public bool IsPreview => _isPreview;

        /// <summary>When true, the player ticks itself via the <see cref="SequenceDriver"/> while playing.</summary>
        public bool IsAutoDriven => _autoDrive;

        public PlaybackRange PlaybackRange => _playbackRange;

        /// <summary>Time scale applied to <see cref="Tick"/>. Clamped to >= 0. Reverse playback is not supported.</summary>
        public float Speed
        {
            get => _speed;
            set => _speed = Mathf.Max(0f, value);
        }

        /// <summary>When true, the sequence wraps back to the range start instead of completing.</summary>
        public bool Loop
        {
            get => _loop;
            set => _loop = value;
        }

        public static SequencePlayer Create<TSeg>(TSeg root, bool isPreview = false, bool restoreValuesOnDispose = true, bool autoDrive = false)
            where TSeg : Segment
        {
            var sequence = AsSequence(root);
            return new SequencePlayer(sequence, PlaybackRange.FullTimeline(), isPreview, restoreValuesOnDispose, autoDrive);
        }

        public static SequencePlayer Create<TSeg>(TSeg root, PlaybackRange playbackRange, bool isPreview = false, bool restoreValuesOnDispose = true, bool autoDrive = false)
            where TSeg : Segment
        {
            var sequence = AsSequence(root);
            return new SequencePlayer(sequence, playbackRange, isPreview, restoreValuesOnDispose, autoDrive);
        }

        private static Sequence AsSequence(Segment rootSegment)
        {
            return rootSegment is Sequence sequence
                ? sequence
                : new Sequence { Segments = new List<Segment> { rootSegment } };
        }

        private SequencePlayer(Sequence root, PlaybackRange playbackRange, bool isPreview, bool restoreValuesOnDispose, bool autoDrive)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));

            _isPreview = isPreview;
            _restoreValuesOnDispose = restoreValuesOnDispose;
            _autoDrive = autoDrive;

            _compiled = CompiledSequence.Compile(root);
            _runtime = new PlaybackRuntime(this, _compiled, TimeEpsilon);

            using (_compiled.Bindings.BulkWriteScope())
                _runtime.Initialize();

            _state = SequencePlaybackState.Idle;
            SetPlaybackRange(playbackRange, clampPlayheadToRange: false);
        }

        // --- Playback controls -------------------------------------------------

        /// <summary>Restarts playback from the start of the playback range.</summary>
        public void Play()
        {
            if (_isDisposed)
                return;

            using (_compiled.Bindings.BulkWriteScope())
            {
                _runtime.Reset();
                _runtime.Scrub(_playbackRange.GetResolvedStart());
            }

            SetState(SequencePlaybackState.Playing);
            Started?.Invoke(this);
        }

        /// <summary>
        /// Continues playback from the current playhead when paused. From any other
        /// non-playing state this is equivalent to <see cref="Play"/>.
        /// </summary>
        public void Resume()
        {
            if (_isDisposed)
                return;

            switch (_state)
            {
                case SequencePlaybackState.Playing:
                    return;
                case SequencePlaybackState.Paused:
                    EnsurePlayheadInRange();
                    SetState(SequencePlaybackState.Playing);
                    Resumed?.Invoke(this);
                    break;
                default:
                    Play();
                    break;
            }
        }

        public void Pause()
        {
            if (_isDisposed || _state != SequencePlaybackState.Playing)
                return;

            SetState(SequencePlaybackState.Paused);
            Paused?.Invoke(this);
        }

        /// <summary>Halts playback, resets to the start and restores the initial values.</summary>
        public void Stop()
        {
            if (_isDisposed || _state == SequencePlaybackState.Stopped)
                return;

            using (_compiled.Bindings.BulkWriteScope())
                _runtime.Reset();

            SetState(SequencePlaybackState.Stopped);
            Stopped?.Invoke(this);
        }

        /// <summary>Seeks to an absolute time on the timeline (clamped to <c>[0, Duration]</c>).</summary>
        public void Seek(float absolutePosition)
        {
            if (_isDisposed)
                return;

            using (_compiled.Bindings.BulkWriteScope())
                _runtime.Scrub(absolutePosition);

            // Seeking out of a finished/stopped state leaves the sequence holding the
            // seeked frame, ready to be resumed.
            if (_state == SequencePlaybackState.Completed || _state == SequencePlaybackState.Stopped)
                SetState(SequencePlaybackState.Paused);
        }

        /// <summary>Seeks using a normalized position (0..1) of the full timeline.</summary>
        public void SeekNormalized(float normalizedPosition)
        {
            Seek(Mathf.Clamp01(normalizedPosition) * Duration);
        }

        /// <summary>Advances playback by <paramref name="dt"/> seconds (scaled by <see cref="Speed"/>). Only advances while playing.</summary>
        public void Tick(float dt)
        {
            if (_isDisposed || _state != SequencePlaybackState.Playing)
                return;

            dt *= _speed;
            if (dt <= TimeEpsilon)
                return;

            using (_compiled.Bindings.BulkWriteScope())
                AdvanceWithinRange(dt);
        }

        // --- Playback range ----------------------------------------------------

        public void SetPlaybackRange(PlaybackRange playbackRange, bool clampPlayheadToRange = true)
        {
            if (_isDisposed)
                return;

            _playbackRange = playbackRange.Normalize(Duration);

            if (clampPlayheadToRange && _state == SequencePlaybackState.Playing)
                EnsurePlayheadInRange();
        }

        public void ClearPlaybackRange()
        {
            if (_isDisposed)
                return;

            SetPlaybackRange(PlaybackRange.FullTimeline(), clampPlayheadToRange: false);
        }

        //get awaiter
        public PlayCompletionAwaiter GetAwaiter() => new PlayCompletionAwaiter(this);

        public void Dispose()
        {
            if (_isDisposed)
                return;

            using (_compiled.Bindings.BulkWriteScope())
            {
                _runtime.CleanUpAll();

                if (_restoreValuesOnDispose)
                    _compiled.Bindings.RestoreInitialValues();
            }

            if (_autoDrive)
                SequenceDriver.Unregister(this);

            _isDisposed = true;
        }

        // --- Internal helpers --------------------------------------------------

        private void SetState(SequencePlaybackState state)
        {
            _state = state;

            if (!_autoDrive)
                return;

            // A self-driven player only needs ticking while it is actually playing.
            if (state == SequencePlaybackState.Playing)
                SequenceDriver.Register(this);
            else
                SequenceDriver.Unregister(this);
        }

        private void AdvanceWithinRange(float remaining)
        {
            var rangeStart = _playbackRange.GetResolvedStart();
            var rangeEnd = _playbackRange.GetResolvedEnd(Duration);

            if (Playhead < rangeStart - TimeEpsilon)
                _runtime.Scrub(rangeStart);

            while (remaining > TimeEpsilon && _state == SequencePlaybackState.Playing)
            {
                var step = Mathf.Min(remaining, Mathf.Max(0f, rangeEnd - Playhead));
                _runtime.Advance(step, SegmentEvaluationMode.Playback);
                remaining -= step;

                // A segment may have paused or stopped the sequence during a keyframe.
                if (_state != SequencePlaybackState.Playing)
                    return;

                if (Playhead < rangeEnd - TimeEpsilon)
                    continue;

                if (_loop && rangeEnd - rangeStart > TimeEpsilon)
                {
                    _runtime.Reset();
                    _runtime.Scrub(rangeStart);
                    Looped?.Invoke(this);
                }
                else
                {
                    SetState(SequencePlaybackState.Completed);
                    Completed?.Invoke(this);
                    return;
                }
            }
        }

        private void EnsurePlayheadInRange()
        {
            var rangeStart = _playbackRange.GetResolvedStart();
            var rangeEnd = _playbackRange.GetResolvedEnd(Duration);

            if (Playhead >= rangeStart - TimeEpsilon && Playhead <= rangeEnd + TimeEpsilon)
                return;

            var target = Playhead < rangeStart - TimeEpsilon ? rangeStart : rangeEnd;

            using (_compiled.Bindings.BulkWriteScope())
                _runtime.Scrub(target);
        }

        private PlaybackBoundaryContext CreateBoundaryContext(float playhead, SegmentEvaluationMode evaluationMode)
        {
            return new PlaybackBoundaryContext(this, playhead, evaluationMode, _isPreview);
        }

        private PlaybackSampleContext CreateSampleContext(SegmentPlayback playback, float playhead, SegmentEvaluationMode evaluationMode)
        {
            var localTime = Mathf.Clamp(playhead - playback.AbsoluteStartTime, 0f, playback.AbsoluteDuration);
            return new PlaybackSampleContext(this, playhead, localTime, playback.AbsoluteDuration, evaluationMode, _isPreview);
        }

        public sealed class PlayCompletionAwaiter : INotifyCompletion
        {
            public readonly SequencePlayer Player;

            public PlayCompletionAwaiter(SequencePlayer player)
            {
                Player = player;
            }

            public bool IsCompleted => Player.IsComplete || Player._isDisposed;
            public void GetResult() { }

            public void OnCompleted(Action continuation)
            {
                void Handler(SequencePlayer player)
                {
                    Player.Completed -= Handler;
                    continuation();
                }

                Player.Completed += Handler;
            }
        }

        /// <summary>
        /// The time-stepping engine. Advances the playhead over an immutable
        /// <see cref="CompiledSequence"/>, maintains the active playback set, and dispatches
        /// enter/sample/exit. It never decides range/loop/completion policy - that belongs to
        /// the owning <see cref="SequencePlayer"/>.
        /// </summary>
        private sealed class PlaybackRuntime
        {
            private readonly SequencePlayer _instance;
            private readonly CompiledSequence _compiled;
            private readonly IReadOnlyList<KeyframeTimeline.Keyframe> _keyframes;
            private readonly List<SegmentPlayback> _activePlaybacks = new();
            private readonly float _timeEpsilon;

            private int _nextKeyframeIndex;
            private float _playhead;

            public float Playhead => _playhead;
            public IReadOnlyCollection<SegmentPlayback> ActivePlaybacks => _activePlaybacks;

            public PlaybackRuntime(SequencePlayer instance, CompiledSequence compiled, float timeEpsilon)
            {
                _instance = instance;
                _compiled = compiled;
                _keyframes = compiled.Timeline.Keyframes;
                _timeEpsilon = timeEpsilon;
            }

            /// <summary>First-time setup of all playbacks. Does not restore values (already at initial state).</summary>
            public void Initialize()
            {
                SetupAll();
                _playhead = 0f;
                _nextKeyframeIndex = 0;
            }

            /// <summary>Returns the timeline to time zero, restoring initial values and re-running setup.</summary>
            public void Reset()
            {
                CleanUpAll();
                _compiled.Bindings.RestoreInitialValues();
                SetupAll();
                _playhead = 0f;
                _nextKeyframeIndex = 0;
            }

            public void Scrub(float absolutePosition)
            {
                absolutePosition = Mathf.Clamp(absolutePosition, 0f, _compiled.Duration);

                if (absolutePosition + _timeEpsilon < _playhead)
                    Reset();

                var delta = absolutePosition - _playhead;
                if (delta > _timeEpsilon)
                    Advance(delta, SegmentEvaluationMode.Scrub);
            }

            public void Advance(float dt, SegmentEvaluationMode evaluationMode)
            {
                var honorInterruptions = evaluationMode == SegmentEvaluationMode.Playback;
                var remaining = dt;

                while (remaining > _timeEpsilon)
                {
                    if (honorInterruptions && !_instance.IsPlaying)
                        break;

                    if (!TryGetNextKeyframeTime(out var nextKeyframeTime))
                    {
                        _playhead += remaining;
                        SampleActivePlaybacks(evaluationMode);
                        break;
                    }

                    var deltaToEvent = nextKeyframeTime - _playhead;

                    if (deltaToEvent > remaining + _timeEpsilon)
                    {
                        _playhead += remaining;
                        SampleActivePlaybacks(evaluationMode);
                        break;
                    }

                    if (deltaToEvent > _timeEpsilon)
                    {
                        _playhead = nextKeyframeTime;
                        remaining -= deltaToEvent;
                    }

                    SampleActivePlaybacks(evaluationMode);
                    ProcessKeyframesAtCurrentPlayhead(evaluationMode, honorInterruptions);
                }
            }

            public void CleanUpAll()
            {
                var context = new PlaybackSetupContext(_instance, _compiled.Playbacks, _instance.IsPreview);
                foreach (var playback in _compiled.Playbacks)
                    playback.CleanUp(in context);

                _activePlaybacks.Clear();
            }

            private void SetupAll()
            {
                var context = new PlaybackSetupContext(_instance, _compiled.Playbacks, _instance.IsPreview);
                foreach (var playback in _compiled.Playbacks)
                    playback.Setup(in context);

                // Cross-playback, per-property coordination runs after every playback
                // has completed its own setup (two-phase: setup, then coordinate).
                _compiled.ApplyPreExtrapolation();
            }

            private void ProcessKeyframesAtCurrentPlayhead(SegmentEvaluationMode evaluationMode, bool honorInterruptions)
            {
                var boundaryContext = _instance.CreateBoundaryContext(_playhead, evaluationMode);

                while (_nextKeyframeIndex < _keyframes.Count)
                {
                    var keyframe = _keyframes[_nextKeyframeIndex];

                    if (Mathf.Abs(keyframe.AbsoluteTime - _playhead) > _timeEpsilon)
                        break;

                    _nextKeyframeIndex++;

                    switch (keyframe.Type)
                    {
                        case KeyframeTimeline.KeyframeType.Enter:
                            keyframe.Playback.OnEnter(in boundaryContext);
                            InsertActivePlayback(keyframe.Playback);
                            break;
                        case KeyframeTimeline.KeyframeType.Exit:
                            keyframe.Playback.OnExit(in boundaryContext);
                            _activePlaybacks.Remove(keyframe.Playback);
                            break;
                    }

                    // A boundary handler may have paused/stopped the sequence - stop processing if so.
                    if (honorInterruptions && !_instance.IsPlaying)
                        return;
                }
            }

            private void InsertActivePlayback(SegmentPlayback playback)
            {
                for (int i = 0; i < _activePlaybacks.Count; i++)
                {
                    if (_activePlaybacks[i].ExecutionOrder > playback.ExecutionOrder)
                    {
                        _activePlaybacks.Insert(i, playback);
                        return;
                    }
                }

                _activePlaybacks.Add(playback);
            }

            private void SampleActivePlaybacks(SegmentEvaluationMode evaluationMode)
            {
                foreach (var playback in _activePlaybacks)
                {
                    var sampleContext = _instance.CreateSampleContext(playback, _playhead, evaluationMode);
                    playback.OnSample(in sampleContext);
                }
            }

            private bool TryGetNextKeyframeTime(out float keyframeTime)
            {
                if (_nextKeyframeIndex < _keyframes.Count)
                {
                    keyframeTime = _keyframes[_nextKeyframeIndex].AbsoluteTime;
                    return true;
                }

                keyframeTime = 0f;
                return false;
            }
        }
    }

    /// <summary>
    /// Immutable build-time artifacts for a sequence: total duration, the flattened playback
    /// list, the keyframe timeline and the resolved property bindings. Produced once via
    /// <see cref="Compile"/> and shared (read-only) with the runtime.
    /// </summary>
    internal sealed class CompiledSequence
    {
        public float Duration { get; }
        public IReadOnlyList<SegmentPlayback> Playbacks { get; }
        public KeyframeTimeline Timeline { get; }
        public BindingSet Bindings { get; }

        /// <summary>
        /// The earliest playback per unique driven property (derived from the plan's
        /// declared bindings), for playbacks that opted into pre-extrapolation.
        /// Computed once at compile time and re-applied on every setup pass.
        /// </summary>
        private readonly IReadOnlyList<PreExtrapolationTarget> _preExtrapolationTargets;

        internal readonly struct PreExtrapolationTarget
        {
            public readonly IPreExtrapolationSource Source;
            public readonly BindableProperty Property;
            public readonly PropertyBindingCollection Bindings;

            public PreExtrapolationTarget(IPreExtrapolationSource source, BindableProperty property, PropertyBindingCollection bindings)
            {
                Source = source;
                Property = property;
                Bindings = bindings;
            }
        }

        private CompiledSequence(
            float duration,
            IReadOnlyList<SegmentPlayback> playbacks,
            KeyframeTimeline timeline,
            BindingSet bindings,
            IReadOnlyList<PreExtrapolationTarget> preExtrapolationTargets)
        {
            Duration = duration;
            Playbacks = playbacks;
            Timeline = timeline;
            Bindings = bindings;
            _preExtrapolationTargets = preExtrapolationTargets;
        }

        /// <summary>Writes each pre-extrapolated property's pre-roll value. Called after every setup pass.</summary>
        public void ApplyPreExtrapolation()
        {
            for (int i = 0; i < _preExtrapolationTargets.Count; i++)
            {
                var target = _preExtrapolationTargets[i];
                if (target.Source.TryGetPreExtrapolationValue(target.Property, out var value))
                    target.Bindings.TryWrite(target.Property, value);
            }
        }

        public static CompiledSequence Compile(Sequence root)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));

            var rootPlan = root.GetPlan(null);
            var duration = Mathf.Max(0f, rootPlan.Timing.AbsoluteDuration);

            var bindings = SequenceCompiler.ResolveBindings(rootPlan, out var planToBindingRoot);
            var playbacks = SequenceCompiler.BuildPlaybacks(rootPlan, bindings, planToBindingRoot, out var propertyDrivers);
            var timeline = KeyframeTimeline.Build(playbacks);
            var preExtrapolationTargets = SequenceCompiler.ResolvePreExtrapolationTargets(propertyDrivers);

            return new CompiledSequence(duration, playbacks, timeline, bindings, preExtrapolationTargets);
        }
    }

    /// <summary>
    /// Build-time logic that turns a planned segment tree into the runtime artifacts:
    /// resolving binding roots into <see cref="PropertyBindingCollection"/>s and flattening
    /// playback builders into <see cref="SegmentPlayback"/>s.
    /// </summary>
    internal static class SequenceCompiler
    {
        public static BindingSet ResolveBindings(SegmentPlan rootPlan, out Dictionary<SegmentPlan, GameObject> planToBindingRoot)
        {
            planToBindingRoot = new Dictionary<SegmentPlan, GameObject>();

            var bindingRootToProperties = new Dictionary<Transform, HashSet<BindableProperty>>();
            var openList = new Queue<SegmentPlan>();
            openList.Enqueue(rootPlan);

            while (openList.Count > 0)
            {
                var current = openList.Dequeue();

                if (current.Bindings.Properties.Count > 0)
                {
                    var resolvedBindingRoot = default(Transform);
                    using (ListPool<Transform>.Get(out var potentialBindingRoots))
                    using (ListPool<BindableProperty>.Get(out var resolvableProperties))
                    {
                        foreach (var property in current.Bindings.Properties)
                        {
                            var propertyTransform = property.Target is Component component
                                ? component.transform
                                : property.Target is GameObject go
                                    ? go.transform
                                    : null;

                            if (propertyTransform == null)
                            {
                                Debug.LogWarning($"BindableProperty {property} has a non-GameObject related target. This property will be ignored.");
                                continue;
                            }

                            var potentialBindingRoot = propertyTransform.parent != null ? propertyTransform.parent : propertyTransform;
                            potentialBindingRoots.Add(potentialBindingRoot);
                            resolvableProperties.Add(property);
                        }

                        // nearest ancestor to all
                        var commonAncestor = default(Transform);
                        for (int i = 0; i < potentialBindingRoots.Count; i++)
                        {
                            Transform root = potentialBindingRoots[i];

                            if (commonAncestor == null)
                            {
                                commonAncestor = root;
                            }
                            else
                            {
                                while (root != null && !root.IsChildOf(commonAncestor))
                                    root = root.parent;

                                if (root != null)
                                    commonAncestor = root;
                                else
                                    Debug.LogWarning($"Could not find a common binding root for segment {current.Segment}. Some of this segments properties will not get bound correctly.");
                            }
                        }

                        resolvedBindingRoot = commonAncestor;

                        if (resolvedBindingRoot != null)
                        {
                            foreach (var (root, _) in bindingRootToProperties)
                            {
                                if (commonAncestor.IsChildOf(root))
                                {
                                    resolvedBindingRoot = root;
                                    break;
                                }
                            }
                        }

                        if (resolvedBindingRoot == null)
                        {
                            planToBindingRoot[current] = null;
                        }
                        else
                        {
                            // New/unique binding root, so we create a new entry for it.
                            if (!bindingRootToProperties.TryGetValue(resolvedBindingRoot, out var existingEntry))
                            {
                                bindingRootToProperties[resolvedBindingRoot] = resolvableProperties.ToHashSet();

                                // Absorb any existing binding roots that are descendants of this new one.
                                using (ListPool<Transform>.Get(out var rootsToAbsorb))
                                {
                                    foreach (var (root, _) in bindingRootToProperties)
                                    {
                                        if (root == resolvedBindingRoot)
                                            continue;

                                        if (root.IsChildOf(resolvedBindingRoot))
                                            rootsToAbsorb.Add(root);
                                    }

                                    foreach (var root in rootsToAbsorb)
                                    {
                                        var propertiesToAbsorb = bindingRootToProperties[root];
                                        bindingRootToProperties.Remove(root);
                                        bindingRootToProperties[resolvedBindingRoot].UnionWith(propertiesToAbsorb);
                                    }

                                    // Redirect any already-mapped plans from the absorbed roots to the new root.
                                    foreach (var plan in planToBindingRoot.Keys.ToList())
                                    {
                                        if (planToBindingRoot[plan] != null && rootsToAbsorb.Contains(planToBindingRoot[plan].transform))
                                            planToBindingRoot[plan] = resolvedBindingRoot.gameObject;
                                    }
                                }
                            }
                            else
                            {
                                existingEntry.UnionWith(resolvableProperties);
                            }

                            planToBindingRoot[current] = resolvedBindingRoot.gameObject;
                        }
                    }
                }
                else
                {
                    planToBindingRoot[current] = null;
                }

                foreach (var child in current.Children)
                    openList.Enqueue(child);
            }

            var collections = new Dictionary<GameObject, PropertyBindingCollection>();
            var restoreValues = new Dictionary<PropertyBindingCollection, Dictionary<BindableProperty, ValueContainer>>();

            foreach (var kvp in bindingRootToProperties)
            {
                var collection = PropertyBindingCollection.Bind(kvp.Key.gameObject, kvp.Value.ToList());
                collections[kvp.Key.gameObject] = collection;

                var restoreForCollection = new Dictionary<BindableProperty, ValueContainer>();
                foreach (var property in kvp.Value)
                {
                    if (collection.TryRead(property, out var valueContainer))
                        restoreForCollection[property] = valueContainer;
                }

                restoreValues[collection] = restoreForCollection;
            }

            return new BindingSet(collections, restoreValues);
        }

        public static List<SegmentPlayback> BuildPlaybacks(
            SegmentPlan rootPlan,
            BindingSet bindings,
            Dictionary<SegmentPlan, GameObject> planToBindingRoot,
            out List<(SegmentPlayback playback, BindableProperty property, PropertyBindingCollection collection)> propertyDrivers)
        {
            var playbacks = new List<SegmentPlayback>();
            propertyDrivers = new List<(SegmentPlayback, BindableProperty, PropertyBindingCollection)>();

            // Depth-first traversal.
            var openList = new Stack<SegmentPlan>();
            openList.Push(rootPlan);

            while (openList.Count > 0)
            {
                var current = openList.Pop();

                if (current.Segment is IPlaybackBuilder playbackBuilder)
                {
                    var bindingRoot = planToBindingRoot.TryGetValue(current, out var root) ? root : null;
                    var collection = bindings.GetCollection(bindingRoot);
                    var buildContext = new PlaybackBuildContext(
                        propertyBindings: collection,
                        absoluteStartTime: current.Timing.AbsoluteStartTime,
                        absoluteDuration: current.Timing.AbsoluteDuration);

                    var playback = playbackBuilder.BuildPlayback(in buildContext);
                    playbacks.Add(playback);

                    // The plan is the type-agnostic declaration of which properties a
                    // segment drives - record them so cross-playback, per-property
                    // behaviour (e.g. pre-extrapolation) can be resolved at compile time.
                    if (collection != null)
                    {
                        foreach (var property in current.Bindings.Properties)
                            propertyDrivers.Add((playback, property, collection));
                    }
                }

                for (int i = current.Children.Count - 1; i >= 0; i--)
                    openList.Push(current.Children[i]);
            }

            return playbacks;
        }

        /// <summary>
        /// Determines, per unique driven property, the earliest playback (ties broken by
        /// ExecutionOrder, then discovery order). If that playback opted into pre-extrapolation
        /// via <see cref="IPreExtrapolationSource"/>, it becomes the property's pre-roll source.
        /// The earliest playback "owns" the pre-roll even when it does not implement the
        /// interface - in that case the property simply has no pre-extrapolation.
        /// </summary>
        public static IReadOnlyList<CompiledSequence.PreExtrapolationTarget> ResolvePreExtrapolationTargets(
            List<(SegmentPlayback playback, BindableProperty property, PropertyBindingCollection collection)> propertyDrivers)
        {
            var earliestPerProperty = new Dictionary<BindableProperty, (SegmentPlayback playback, PropertyBindingCollection collection)>();

            foreach (var (playback, property, collection) in propertyDrivers)
            {
                if (!earliestPerProperty.TryGetValue(property, out var current)
                    || playback.AbsoluteStartTime < current.playback.AbsoluteStartTime
                    || (playback.AbsoluteStartTime == current.playback.AbsoluteStartTime
                        && playback.ExecutionOrder < current.playback.ExecutionOrder))
                {
                    earliestPerProperty[property] = (playback, collection);
                }
            }

            var targets = new List<CompiledSequence.PreExtrapolationTarget>();
            foreach (var (property, entry) in earliestPerProperty)
            {
                if (entry.playback is IPreExtrapolationSource source)
                    targets.Add(new CompiledSequence.PreExtrapolationTarget(source, property, entry.collection));
            }

            return targets;
        }
    }

    /// <summary>
    /// Owns the property-binding lifecycle for a compiled sequence: bulk-write scoping and
    /// restoring the captured initial values.
    /// </summary>
    internal sealed class BindingSet
    {
        private readonly Dictionary<GameObject, PropertyBindingCollection> _collections;
        private readonly Dictionary<PropertyBindingCollection, Dictionary<BindableProperty, ValueContainer>> _restoreValues;

        public BindingSet(
            Dictionary<GameObject, PropertyBindingCollection> collections,
            Dictionary<PropertyBindingCollection, Dictionary<BindableProperty, ValueContainer>> restoreValues)
        {
            _collections = collections;
            _restoreValues = restoreValues;
        }

        public PropertyBindingCollection GetCollection(GameObject bindingRoot)
        {
            return bindingRoot != null && _collections.TryGetValue(bindingRoot, out var collection)
                ? collection
                : null;
        }

        public void RestoreInitialValues()
        {
            foreach (var (collection, values) in _restoreValues)
            {
                foreach (var (property, value) in values)
                    collection.TryWrite(property, value);
            }
        }

        public Scope BulkWriteScope()
        {
            return new Scope(this);
        }

        public readonly struct Scope : IDisposable
        {
            private readonly BindingSet _bindings;

            public Scope(BindingSet bindings)
            {
                _bindings = bindings;
                foreach (var collection in bindings._collections.Values)
                    collection.StartBulkWrite();
            }

            public void Dispose()
            {
                foreach (var collection in _bindings._collections.Values)
                    collection.EndBulkWrite();
            }
        }
    }

    /// <summary>
    /// The sorted enter/exit keyframe model for a compiled sequence. Pure data: ordered by
    /// absolute time, then by playback execution order.
    /// </summary>
    internal sealed class KeyframeTimeline
    {
        public enum KeyframeType
        {
            Enter,
            Exit
        }

        public readonly struct Keyframe
        {
            public readonly SegmentPlayback Playback;
            public readonly float AbsoluteTime;
            public readonly KeyframeType Type;

            public Keyframe(SegmentPlayback playback, float absoluteTime, KeyframeType type)
            {
                Playback = playback;
                AbsoluteTime = absoluteTime;
                Type = type;
            }
        }

        public IReadOnlyList<Keyframe> Keyframes { get; }

        private KeyframeTimeline(IReadOnlyList<Keyframe> keyframes)
        {
            Keyframes = keyframes;
        }

        public static KeyframeTimeline Build(IReadOnlyList<SegmentPlayback> playbacks)
        {
            var keyframes = new List<Keyframe>(playbacks.Count * 2);

            foreach (var playback in playbacks)
            {
                keyframes.Add(new Keyframe(playback, playback.AbsoluteStartTime, KeyframeType.Enter));
                keyframes.Add(new Keyframe(playback, playback.AbsoluteEndTime, KeyframeType.Exit));
            }

            var ordered = keyframes
                .OrderBy(x => x.AbsoluteTime)
                .ThenBy(x => x.Playback.ExecutionOrder)
                .ToList();

            return new KeyframeTimeline(ordered);
        }
    }
}