using System;
using System.Collections.Generic;
using System.Linq;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Sequencer.Segments;
using UnityEngine;

namespace TimboJimbo.Sequencer
{
    public class SequenceInstance : IDisposable
    {
        private readonly Dictionary<GameObject, PropertyBindingCollection> _propertyBindingCollections = new();
        private readonly Dictionary<PropertyBindingCollection, Dictionary<BindableProperty, ValueContainer>> _restoreValues = new();
        private readonly List<SegmentPlayback> _playbacks = new();
        private readonly bool _restoreValuesOnDispose;
        private readonly bool _isPreview;

        private bool _isDisposed;
        private RuntimeState _runtime;

        private const float TimeEpsilon = 0.00001f;

        public float Playhead => _runtime != null ? _runtime.Playhead : 0f;
        public float Duration { get; }
        public bool IsPaused => _runtime != null && _runtime.IsPaused;
        public bool IsStopped => _runtime != null && _runtime.IsStopped;
        public bool IsPreview => _isPreview;

        public IReadOnlyList<SegmentPlayback> AllPlaybacks => _playbacks;
        public IReadOnlyCollection<SegmentPlayback> ActivePlaybacks => _runtime != null
            ? _runtime.ActivePlaybacks
            : Array.Empty<SegmentPlayback>();

        public static SequenceInstance Create(Sequence root, bool isPreview = false, bool restoreValuesOnDispose = true)
        {
            return new SequenceInstance(root, isPreview, restoreValuesOnDispose);
        }

        public static SequenceInstance Create(Segment rootSegment, bool isPreview = false, bool restoreValuesOnDispose = true)
        {
            var rootSequence = rootSegment is Sequence sequence
                ? sequence
                : new Sequence { Segments = new List<Segment> { rootSegment } };

            return new SequenceInstance(rootSequence, isPreview, restoreValuesOnDispose);
        }

        private SequenceInstance(Sequence root, bool isPreview = false, bool restoreValuesOnDispose = true)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));

            _restoreValuesOnDispose = restoreValuesOnDispose;
            _isPreview = isPreview;

            var rootPlan = root.GetPlan(null);
            Duration = Mathf.Max(0f, rootPlan.Timing.AbsoluteDuration);

            CollectBindingsAndRestoreValues(rootPlan);
            BuildPlaybacks(rootPlan);

            var keyframes = BuildKeyframes();

            SetupAllPlaybacks();
            _runtime = new RuntimeState(
                owner: this,
                keyframes: keyframes,
                cleanUpAllPlaybacks: CleanUpAllPlaybacks,
                setupAllPlaybacks: SetupAllPlaybacks,
                restoreInitialValues: RestoreInitialValues,
                timeEpsilon: TimeEpsilon,
                duration: Duration);
        }

        public void Tick(float dt)
        {
            if (_isDisposed)
                return;

            using(BulkWriteAll.Scope(this))
                _runtime.Tick(dt);
        }

        public void Pause()
        {
            if (_isDisposed)
                return;

            using(BulkWriteAll.Scope(this))
                _runtime.Pause();
        }

        public void Resume()
        {
            if (_isDisposed)
                return;

            using(BulkWriteAll.Scope(this))
                _runtime.Resume();
        }

        public void Stop()
        {
            if (_isDisposed)
                return;

            using(BulkWriteAll.Scope(this))
                _runtime.Stop();
        }

        public void Scrub(float absolutePosition)
        {
            if (_isDisposed)
                return;

            using(BulkWriteAll.Scope(this))
                _runtime.Scrub(absolutePosition);
        }

        private void CollectBindingsAndRestoreValues(SegmentPlan rootPlan)
        {
            var bindingRootToProperties = new Dictionary<GameObject, HashSet<BindableProperty>>();
            var openList = new Queue<SegmentPlan>();
            openList.Enqueue(rootPlan);

            while (openList.Count > 0)
            {
                var current = openList.Dequeue();

                if (current.Bindings.BindingsRoot != null)
                {
                    if (!bindingRootToProperties.ContainsKey(current.Bindings.BindingsRoot))
                    {
                        bindingRootToProperties[current.Bindings.BindingsRoot] = new HashSet<BindableProperty>();
                    }

                    foreach (var property in current.Bindings.Properties)
                    {
                        bindingRootToProperties[current.Bindings.BindingsRoot].Add(property);
                    }
                }

                foreach (var child in current.Children)
                {
                    openList.Enqueue(child);
                }
            }

            foreach (var kvp in bindingRootToProperties)
            {
                var propertyBindingCollection = PropertyBindingCollection.Bind(kvp.Key, kvp.Value.ToList());
                _propertyBindingCollections[kvp.Key] = propertyBindingCollection;

                var restoreValuesForCollection = new Dictionary<BindableProperty, ValueContainer>();
                foreach (var property in kvp.Value)
                {
                    if (propertyBindingCollection.TryRead(property, out var valueContainer))
                    {
                        restoreValuesForCollection[property] = valueContainer;
                    }
                }

                _restoreValues[propertyBindingCollection] = restoreValuesForCollection;
            }
        }

        private void BuildPlaybacks(SegmentPlan rootPlan)
        {
            // Playbacks are built in a depth-first
            var openList = new Stack<SegmentPlan>();
            openList.Push(rootPlan);

            while (openList.Count > 0)
            {
                var current = openList.Pop();
                var playbackBuilder = current.Segment as IPlaybackBuilder;

                if (playbackBuilder != null)
                {
                    var playbackBuildContext = new PlaybackBuildContext(
                        propertyBindings: current.Bindings.BindingsRoot != null
                            ? _propertyBindingCollections[current.Bindings.BindingsRoot]
                            : null,
                        absoluteStartTime: current.Timing.AbsoluteStartTime,
                        absoluteDuration: current.Timing.AbsoluteDuration
                    );

                    var playback = playbackBuilder.BuildPlayback(in playbackBuildContext);
                    _playbacks.Add(playback);
                }

                for (int i = current.Children.Count - 1; i >= 0; i--)
                {
                    openList.Push(current.Children[i]);
                }
            }
        }

        private List<Keyframe> BuildKeyframes()
        {
            var keyframes = new List<Keyframe>();

            for (int i = 0; i < _playbacks.Count; i++)
            {
                SegmentPlayback playback = _playbacks[i];
                keyframes.Add(new Keyframe
                {
                    Playback = playback,
                    AbsoluteTime = playback.AbsoluteStartTime,
                    Type = KeyframeType.Enter
                });

                keyframes.Add(new Keyframe
                {
                    Playback = playback,
                    AbsoluteTime = playback.AbsoluteEndTime,
                    Type = KeyframeType.Exit
                });
            }

            // We want to preserve insertion order for playbacks with the same start time,
            //  so we use index as a tie-breaker when sorting keyframes
            return keyframes
                .Select((keyframe, index) => new { keyframe, index })
                .OrderBy(x => x.keyframe.AbsoluteTime)
                .ThenBy(x => x.index)
                .Select(x => x.keyframe)
                .ToList();
        }

        private void SetupAllPlaybacks()
        {
            using(BulkWriteAll.Scope(this))
            {
                var setupContext = new PlaybackSetupContext(this, _isPreview);
                foreach (var playback in _playbacks)
                    playback.Setup(in setupContext);
            }
        }

        private void CleanUpAllPlaybacks()
        {
            using(BulkWriteAll.Scope(this))
            {
                var setupContext = new PlaybackSetupContext(this, _isPreview);
                foreach (var playback in _playbacks)
                    playback.CleanUp(in setupContext);
            }
        }

        private void RestoreInitialValues()
        {
            using(BulkWriteAll.Scope(this))
            {
                foreach (var kvp in _restoreValues)
                {
                    var propertyBindingCollection = kvp.Key;
                    var restoreValuesForCollection = kvp.Value;

                    foreach (var restoreKvp in restoreValuesForCollection)
                    {
                        propertyBindingCollection.TryWrite(restoreKvp.Key, restoreKvp.Value);
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            CleanUpAllPlaybacks();
            _playbacks.Clear();

            if (_restoreValuesOnDispose)
                RestoreInitialValues();

            _propertyBindingCollections.Clear();
            _restoreValues.Clear();
            _runtime = null;
            _isDisposed = true;
        }

        private PlaybackBoundaryContext CreateBoundaryContext(float playhead, SegmentEvaluationMode evaluationMode)
        {
            return new PlaybackBoundaryContext(this, playhead, evaluationMode, _isPreview);
        }

        private PlaybackSampleContext CreateSampleContext(
            SegmentPlayback playback,
            float playhead,
            SegmentEvaluationMode evaluationMode)
        {
            var localTime = Mathf.Clamp(playhead - playback.AbsoluteStartTime, 0f, playback.AbsoluteDuration);
            return new PlaybackSampleContext(
                this,
                playhead,
                localTime,
                playback.AbsoluteDuration,
                evaluationMode,
                _isPreview);
        }

        private struct Keyframe
        {
            public SegmentPlayback Playback;
            public float AbsoluteTime;
            public KeyframeType Type;
        }

        private enum KeyframeType
        {
            Enter,
            Exit
        }

        private sealed class RuntimeState
        {
            private readonly SequenceInstance _owner;
            private readonly List<Keyframe> _keyframes;
            private readonly HashSet<SegmentPlayback> _activePlaybacks = new();
            private readonly Action _cleanUpAllPlaybacks;
            private readonly Action _setupAllPlaybacks;
            private readonly Action _restoreInitialValues;
            private readonly float _timeEpsilon;
            private readonly float _duration;

            private int _nextKeyframeIndex;
            private float _playhead;
            private bool _isPaused;
            private bool _isStopped;

            public float Playhead => _playhead;
            public bool IsPaused => _isPaused;
            public bool IsStopped => _isStopped;

            public IReadOnlyCollection<SegmentPlayback> ActivePlaybacks => _activePlaybacks;

            public RuntimeState(
                SequenceInstance owner,
                List<Keyframe> keyframes,
                Action cleanUpAllPlaybacks,
                Action setupAllPlaybacks,
                Action restoreInitialValues,
                float timeEpsilon,
                float duration)
            {
                _owner = owner;
                _keyframes = keyframes;
                _cleanUpAllPlaybacks = cleanUpAllPlaybacks;
                _setupAllPlaybacks = setupAllPlaybacks;
                _restoreInitialValues = restoreInitialValues;
                _timeEpsilon = timeEpsilon;
                _duration = duration;

                _nextKeyframeIndex = 0;
                _playhead = 0f;
                _isPaused = false;
                _isStopped = false;
            }

            public void Tick(float dt)
            {
                TickInternal(dt, SegmentEvaluationMode.Playback, ignorePauseAndStop: false);
            }

            public void Pause()
            {
                _isPaused = true;
            }

            public void Resume()
            {
                _isPaused = false;
                _isStopped = false;
            }

            public void Stop()
            {
                ResetRuntimeState(restoreValues: true);
                _isStopped = true;
                _isPaused = false;
            }

            public void Scrub(float absolutePosition)
            {
                absolutePosition = Mathf.Clamp(absolutePosition, 0f, _duration);

                if (absolutePosition + _timeEpsilon < _playhead)
                {
                    ResetRuntimeState(restoreValues: true);
                }

                var scrubDelta = absolutePosition - _playhead;
                if (scrubDelta > _timeEpsilon)
                {
                    TickInternal(scrubDelta, SegmentEvaluationMode.Scrub, ignorePauseAndStop: true);
                }
            }

            private void TickInternal(float dt, SegmentEvaluationMode evaluationMode, bool ignorePauseAndStop)
            {
                var remaining = dt;

                while (remaining > _timeEpsilon && (ignorePauseAndStop || (!_isPaused && !_isStopped)))
                {
                    if (!TryGetNextKeyframeTime(out var nextKeyframeTime))
                    {
                        _playhead += remaining;
                        SampleActivePlaybacks(_playhead, evaluationMode);
                        break;
                    }

                    var deltaToEvent = nextKeyframeTime - _playhead;

                    if (deltaToEvent > remaining + _timeEpsilon)
                    {
                        _playhead += remaining;
                        SampleActivePlaybacks(_playhead, evaluationMode);
                        break;
                    }

                    if (deltaToEvent > _timeEpsilon)
                    {
                        _playhead = nextKeyframeTime;
                        remaining -= deltaToEvent;
                        SampleActivePlaybacks(_playhead, evaluationMode);
                    }
                    else
                    {
                        SampleActivePlaybacks(_playhead, evaluationMode);
                    }

                    ProcessKeyframesAtCurrentPlayhead(evaluationMode);
                }
            }

            private void ProcessKeyframesAtCurrentPlayhead(SegmentEvaluationMode evaluationMode)
            {
                var boundaryContext = _owner.CreateBoundaryContext(_playhead, evaluationMode);

                while (_nextKeyframeIndex < _keyframes.Count)
                {
                    var keyframe = _keyframes[_nextKeyframeIndex];
                    
                    if (Mathf.Abs(keyframe.AbsoluteTime - _playhead) > _timeEpsilon)
                        break;

                    _nextKeyframeIndex++;

                    switch (keyframe.Type)
                    {
                        case KeyframeType.Enter:
                            keyframe.Playback.OnEnter(in boundaryContext);
                            _activePlaybacks.Add(keyframe.Playback);
                            break;
                        case KeyframeType.Exit:
                            keyframe.Playback.OnExit(in boundaryContext);
                            _activePlaybacks.Remove(keyframe.Playback);
                            break;
                    }

                    // Keyframe Enter/Exit may result in pause/stop - so lets stop processing further keyframes if that's the case
                    if (_isPaused || _isStopped)
                        return;
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

            private void SampleActivePlaybacks(float playhead, SegmentEvaluationMode evaluationMode)
            {
                foreach (var playback in _activePlaybacks)
                {
                    var sampleContext = _owner.CreateSampleContext(playback, playhead, evaluationMode);
                    playback.OnSample(in sampleContext);
                }
            }

            private void ResetRuntimeState(bool restoreValues)
            {
                _cleanUpAllPlaybacks();
                _activePlaybacks.Clear();

                if (restoreValues)
                    _restoreInitialValues();

                _setupAllPlaybacks();
                _nextKeyframeIndex = 0;
                _playhead = 0f;
            }
        }

        private struct BulkWriteAll : IDisposable
        {
            private readonly SequenceInstance _instance;

            private BulkWriteAll(SequenceInstance owner)
            {
                _instance = owner;

                foreach (var (_, collection) in _instance._propertyBindingCollections)
                    collection.StartBulkWrite();
            }

            public static BulkWriteAll Scope(SequenceInstance owner)
            {
                return new BulkWriteAll(owner);
            }

            public void Dispose()
            {
                foreach (var (_, collection) in _instance._propertyBindingCollections)
                    collection.EndBulkWrite();
            }
        }
    }
}