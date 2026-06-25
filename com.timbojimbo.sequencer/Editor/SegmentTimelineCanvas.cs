using System;
using System.Collections.Generic;
using TimboJimboEditor.Sequencer.Blocks;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UIElements;

namespace TimboJimboEditor.Sequencer
{
    public sealed class SegmentTimelineCanvas : VisualElement
    {
        private enum MarqueeMode { Replace, Additive, Subtractive }

        private const float RulerHeight = 24f;
        private const float LaneTop = RulerHeight + 8f;
        private const float LaneHeight = 48f;
        private const float LaneGap = 6f;
        private const float HorizontalPadding = 12f;
        private const float MinDurationPx = 12f;
        private const float MinZoom = 10f;
        private const float MaxZoom = 1600f;
        private const float ResizeHandlePx = 8f;
        private const float OutlinePadding = 1f;
        private const float SnapThresholdPx = 20f;
        private const float TransformDragThresholdPx = 8f;

        private static readonly Color PreviewAccent = new Color(0.173f, 0.471f, 0.922f, 1.000f);

        private enum DragKind { None, ResizeLeft, ResizeRight, Move }
        private enum PointerSessionMode { BackgroundPress, BlockPress, SelectionPress, Marquee, Transform, Scrub, Pan }

        public Action<IReadOnlyList<SegmentSelectionModel>> SelectionChanged;
        public Action<IReadOnlyList<(SegmentSelectionModel model, float start, float duration)>> TimeAdjustmentCommitted;
        public Action<IReadOnlyList<SegmentSelectionModel>> DeleteRequested;
        public Action<Type, float> AddRequested;
        public Action<float> SeekRequested;
        public Action CopyRequested;
        public Action PasteRequested;
        public bool Snap = false;
        public IReadOnlyList<SegmentSelectionModel> SelectedModels => _selection.EffectiveSelection;

        private readonly List<PlanBlock> _blocks = new();
        private readonly List<SegmentSelectionModel> _models = new();
        private readonly SelectionState _selection = new();
        private readonly List<SegmentSnapCandidate> _segmentSnapCandidates = new();
        private readonly List<Label> _rulerLabels = new();
        private readonly VisualElement _contentRoot;
        private readonly VisualElement _playhead;
        private readonly VisualElement _snapGuide;
        private readonly VisualElement _selectionOutline;
        private readonly VisualElement _marqueeBox;

        private float _time;
        private bool _previewActive;
        private float _pixelsPerSecond = 120f;
        private float _viewStart;
        private bool _viewWasEverFramed;

        private SelectionTransformOperation _selectionTransform;
        private PointerSession _pointerSession;
        private bool IsDraggingSelection => _pointerSession?.Mode == PointerSessionMode.Transform && _selectionTransform != null;

        private readonly struct SegmentSnapCandidate
        {
            public readonly float Time;

            public SegmentSnapCandidate(float time)
            {
                Time = time;
            }
        }

        private sealed class SkipTransitionsScope : IDisposable
        {
            private Dictionary<VisualElement, CachedTransitions> _cache;

            public SkipTransitionsScope()
            {
                _cache = DictionaryPool<VisualElement, CachedTransitions>.Get();
            }

            public void Add(VisualElement element)
            {
                if (element == null || _cache.ContainsKey(element))
                    return;

                var data = default(CachedTransitions);

                var p = element.style.transitionProperty;
                if (p.value != null && p.value.Count > 0)
                {
                    data.Properties = ListPool<StylePropertyName>.Get();
                    data.Properties.AddRange(p.value);
                }

                var d = element.style.transitionDuration;
                if (d.value != null && d.value.Count > 0)
                {
                    data.Durations = ListPool<TimeValue>.Get();
                    data.Durations.AddRange(d.value);
                }

                var e = element.style.transitionTimingFunction;
                if (e.value != null && e.value.Count > 0)
                {
                    data.EasingFunctions = ListPool<EasingFunction>.Get();
                    data.EasingFunctions.AddRange(e.value);
                }

                element.style.transitionProperty = null;
                element.style.transitionDuration = null;
                element.style.transitionTimingFunction = null;

                _cache[element] = data;
            }

            public void AddRange(IEnumerable<VisualElement> elements)
            {
                foreach (var el in elements)
                    Add(el);
            }

            public void Dispose()
            {
                foreach (var kvp in _cache)
                {
                    var el = kvp.Key;
                    var data = kvp.Value;

                    el.style.transitionProperty = data.Properties ?? default;
                    el.style.transitionDuration = data.Durations ?? default;
                    el.style.transitionTimingFunction = data.EasingFunctions ?? default;

                    if (data.Properties != null) ListPool<StylePropertyName>.Release(data.Properties);
                    if (data.Durations != null) ListPool<TimeValue>.Release(data.Durations);
                    if (data.EasingFunctions != null) ListPool<EasingFunction>.Release(data.EasingFunctions);
                }

                DictionaryPool<VisualElement, CachedTransitions>.Release(_cache);
                _cache = null;
            }

            private struct CachedTransitions
            {
                public List<StylePropertyName> Properties;
                public List<TimeValue> Durations;
                public List<EasingFunction> EasingFunctions;
            }
        }

        private sealed class PlanBlock
        {
            public SegmentSelectionModel Model;
            public VisualElement SelectionHighlight;
            public VisualElement Root;
            public Rect LayoutRect;
        }

        private sealed class PointerSession
        {
            public PointerSessionMode Mode;
            public int PointerId;
            public Vector2 PointerStartWorld;
            public Vector2 PointerStartLocal;
            public Vector2 PanStartPointer;
            public float PanStartView;
            public PlanBlock PressedBlock;
            public bool Shift;
            public bool Action;
            public bool BlockWasSelected;
            public MarqueeMode MarqueeMode;
        }

        private sealed class SelectionState
        {
            private readonly List<SegmentSelectionModel> _committed = new(); 
            private readonly List<SegmentSelectionModel> _effective = new();     
            private readonly List<SegmentSelectionModel> _marqueeBase = new();
            private readonly List<SegmentSelectionModel> _marqueeCurrent = new();
            private bool _marqueeActive;
            private MarqueeMode _marqueeMode;

            public IReadOnlyList<SegmentSelectionModel> EffectiveSelection => _effective;

            public bool IsSelected(SegmentSelectionModel model)
            {
                if (model == null)
                    return false;

                return _effective.Contains(model);
            }

            public void SetCommittedSelection(IReadOnlyList<SegmentSelectionModel> selectedModels)
            {
                _committed.Clear();

                if (selectedModels != null)
                {
                    for (int i = 0; i < selectedModels.Count; i++)
                        AddUnique(_committed, selectedModels[i]);
                }

                RebuildEffectiveSelection();
            }

            public void Clear()
            {
                _committed.Clear();
                _marqueeBase.Clear();
                _marqueeCurrent.Clear();
                _marqueeActive = false;
                _effective.Clear();
            }

            public List<SegmentSelectionModel> BuildSingleSelection(SegmentSelectionModel clicked)
            {
                var list = new List<SegmentSelectionModel>();
                if (clicked != null)
                    list.Add(clicked);
                return list;
            }

            public List<SegmentSelectionModel> BuildToggleSelection(SegmentSelectionModel clicked)
            {
                var list = new List<SegmentSelectionModel>(_committed);
                Toggle(list, clicked);
                return list;
            }

            public List<SegmentSelectionModel> BuildRangeSelection(SegmentSelectionModel clicked, IReadOnlyList<PlanBlock> blocks)
            {
                var list = new List<SegmentSelectionModel>();
                if (clicked == null)
                    return list;

                if (_committed.Count == 0)
                {
                    list.Add(clicked);
                    return list;
                }

                var anchor = _committed[^1];
                if (!TryGetBlockByModel(anchor, blocks, out var anchorBlock) ||
                    !TryGetBlockByModel(clicked, blocks, out var clickedBlock))
                {
                    list.Add(clicked);
                    return list;
                }

                var marquee = BuildMarqueeFromLayoutRects(anchorBlock.LayoutRect, clickedBlock.LayoutRect);

                list.AddRange(_committed);
                for (int i = 0; i < blocks.Count; i++)
                {
                    var candidate = blocks[i];
                    if (candidate == null || candidate.Model == null)
                        continue;

                    if (candidate.LayoutRect.Overlaps(marquee))
                        AddUnique(list, candidate.Model);
                }

                return list;
            }

            public void BeginMarquee(MarqueeMode mode)
            {
                _marqueeBase.Clear();
                _marqueeCurrent.Clear();
                _marqueeActive = true;
                _marqueeMode = mode;

                if (mode != MarqueeMode.Replace)
                    AddUnique(_marqueeBase, _committed);

                RebuildEffectiveSelection();
            }

            public void UpdateMarquee(IReadOnlyList<SegmentSelectionModel> marqueeHits)
            {
                if (!_marqueeActive)
                    return;

                _marqueeCurrent.Clear();
                if (marqueeHits != null)
                {
                    for (int i = 0; i < marqueeHits.Count; i++)
                    {
                        var hit = marqueeHits[i];
                        if (hit == null)
                            continue;

                        if (_marqueeMode == MarqueeMode.Additive && _marqueeBase.Contains(hit))
                            continue;

                        if (_marqueeMode == MarqueeMode.Subtractive && !_marqueeBase.Contains(hit))
                            continue;

                        AddUnique(_marqueeCurrent, hit);
                    }
                }

                RebuildEffectiveSelection();
            }

            public List<SegmentSelectionModel> BuildCommittedMarqueeSelection()
            {
                if (!_marqueeActive)
                    return new List<SegmentSelectionModel>(_committed);

                var list = new List<SegmentSelectionModel>();
                BuildMarqueeResult(list);
                return list;
            }

            public void EndMarquee()
            {
                _marqueeBase.Clear();
                _marqueeCurrent.Clear();
                _marqueeActive = false;
                RebuildEffectiveSelection();
            }

            private void RebuildEffectiveSelection()
            {
                _effective.Clear();

                if (_marqueeActive)
                {
                    BuildMarqueeResult(_effective);
                    return;
                }

                AddUnique(_effective, _committed);
            }

            private void BuildMarqueeResult(List<SegmentSelectionModel> output)
            {
                switch (_marqueeMode)
                {
                    case MarqueeMode.Replace:
                        AddUnique(output, _marqueeCurrent);
                        break;

                    case MarqueeMode.Additive:
                        AddUnique(output, _marqueeBase);
                        AddUnique(output, _marqueeCurrent);
                        break;

                    case MarqueeMode.Subtractive:
                        for (int i = 0; i < _marqueeBase.Count; i++)
                        {
                            var candidate = _marqueeBase[i];
                            if (!_marqueeCurrent.Contains(candidate))
                                AddUnique(output, candidate);
                        }
                        break;
                }
            }

            private static void Toggle(List<SegmentSelectionModel> list, SegmentSelectionModel model)
            {
                if (model == null)
                    return;

                int existingIndex = list.FindIndex(p => ReferenceEquals(p, model));
                if (existingIndex >= 0)
                    list.RemoveAt(existingIndex);
                else
                    list.Add(model);
            }

            private static void AddUnique(List<SegmentSelectionModel> list, SegmentSelectionModel model)
            {
                if (model == null || list.Contains(model))
                    return;

                list.Add(model);
            }

            private static void AddUnique(List<SegmentSelectionModel> list, IReadOnlyList<SegmentSelectionModel> models)
            {
                if (models == null)
                    return;

                for (int i = 0; i < models.Count; i++)
                    AddUnique(list, models[i]);
            }

            private static bool TryGetBlockByModel(SegmentSelectionModel model, IReadOnlyList<PlanBlock> blocks, out PlanBlock block)
            {
                for (int i = 0; i < blocks.Count; i++)
                {
                    var candidate = blocks[i];
                    if (candidate != null && ReferenceEquals(candidate.Model, model))
                    {
                        block = candidate;
                        return true;
                    }
                }

                block = null;
                return false;
            }

            private static Rect BuildMarqueeFromLayoutRects(Rect a, Rect b)
            {
                var min = Vector2.Min(a.min, b.min);
                var max = Vector2.Max(a.max, b.max);
                return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }
        }

        private sealed class SelectionTransformOperation
        {
            private readonly struct Entry
            {
                public readonly SegmentSelectionModel Model;
                public readonly float Start;
                public readonly float Duration;
                public readonly bool CanAdjustStart;
                public readonly bool CanAdjustDuration;

                public Entry(SegmentSelectionModel model)
                {
                    Model = model;
                    Start = model.StartTime;
                    Duration = model.Duration;
                    CanAdjustStart = model.CanAdjustStartTime;
                    CanAdjustDuration = model.CanAdjustDuration;
                }
            }

            private readonly struct GhostTiming
            {
                public readonly float Start;
                public readonly float Duration;

                public GhostTiming(float start, float duration)
                {
                    Start = start;
                    Duration = duration;
                }
            }

            public readonly DragKind Kind;
            public readonly int PointerId;
            public readonly Vector2 PointerStart;
            public readonly float InitialSelectionStart;
            public readonly float InitialSelectionDuration;

            private readonly List<Entry> _entries;
            private readonly Dictionary<SegmentSelectionModel, GhostTiming> _ghostByModel;
            private bool _hasChanges;

            public bool HasChanges => _hasChanges;

            public SelectionTransformOperation(
                DragKind kind,
                int pointerId,
                Vector2 pointerStart,
                float selectionStart,
                float selectionDuration,
                IReadOnlyList<SegmentSelectionModel> selectedModels)
            {
                Kind = kind;
                PointerId = pointerId;
                PointerStart = pointerStart;
                InitialSelectionStart = selectionStart;
                InitialSelectionDuration = Mathf.Max(selectionDuration, 0.0001f);

                _entries = new List<Entry>();
                _ghostByModel = new Dictionary<SegmentSelectionModel, GhostTiming>();
                _hasChanges = false;

                if (selectedModels == null)
                    return;

                for (int i = 0; i < selectedModels.Count; i++)
                {
                    var model = selectedModels[i];
                    if (model == null)
                        continue;

                    var entry = new Entry(model);
                    _entries.Add(entry);
                    _ghostByModel[model] = new GhostTiming(entry.Start, entry.Duration);
                }
            }

            public void UpdateGhost(float dt)
            {
                if (_entries.Count == 0)
                    return;

                float initialStart = InitialSelectionStart;
                float initialDuration = Mathf.Max(InitialSelectionDuration, 0.0001f);
                float initialEnd = initialStart + initialDuration;

                float selectionStart = initialStart;
                float selectionEnd = initialEnd;

                switch (Kind)
                {
                    case DragKind.Move:
                        selectionStart = Mathf.Max(0f, initialStart + dt);
                        selectionEnd = selectionStart + initialDuration;
                        break;

                    case DragKind.ResizeLeft:
                        selectionStart = Mathf.Max(0f, initialStart + dt);
                        selectionStart = Mathf.Min(selectionStart, initialEnd - 0.01f);
                        break;

                    case DragKind.ResizeRight:
                        selectionEnd = Mathf.Max(initialStart + 0.01f, initialEnd + dt);
                        break;
                }

                float scaledDuration = Mathf.Max(selectionEnd - selectionStart, 0.0001f);
                float scale = scaledDuration / initialDuration;
                _hasChanges = false;

                for (int i = 0; i < _entries.Count; i++)
                {
                    var entry = _entries[i];
                    float newStart = entry.Start;
                    float newDuration = entry.Duration;

                    if (Kind == DragKind.Move)
                    {
                        if (entry.CanAdjustStart)
                        {
                            float shift = selectionStart - initialStart;
                            newStart = Mathf.Max(0f, entry.Start + shift);
                        }
                    }
                    else
                    {
                        float entryEnd = entry.Start + entry.Duration;
                        float mappedStart = selectionStart + (entry.Start - initialStart) * scale;
                        float mappedEnd = selectionStart + (entryEnd - initialStart) * scale;

                        if (entry.CanAdjustStart && entry.CanAdjustDuration)
                        {
                            newStart = Mathf.Max(0f, mappedStart);
                            newDuration = Mathf.Max(0.01f, mappedEnd - newStart);
                        }
                        else if (entry.CanAdjustStart)
                        {
                            newStart = Mathf.Max(0f, mappedStart);
                            newDuration = entry.Duration;
                        }
                        else if (entry.CanAdjustDuration)
                        {
                            newStart = entry.Start;
                            newDuration = Mathf.Max(0.01f, mappedEnd - newStart);
                        }
                    }

                    _ghostByModel[entry.Model] = new GhostTiming(newStart, newDuration);

                    if (Mathf.Abs(newStart - entry.Start) > 0.0001f || Mathf.Abs(newDuration - entry.Duration) > 0.0001f)
                        _hasChanges = true;
                }
            }

            public bool TryGetGhost(SegmentSelectionModel model, out float start, out float duration)
            {
                if (model != null && _ghostByModel.TryGetValue(model, out var ghost))
                {
                    start = ghost.Start;
                    duration = ghost.Duration;
                    return true;
                }

                start = 0f;
                duration = 0f;
                return false;
            }

            public void GetCommittedChanges(List<(SegmentSelectionModel model, float start, float duration)> output)
            {
                output.Clear();
                if (!_hasChanges)
                    return;

                for (int i = 0; i < _entries.Count; i++)
                {
                    var entry = _entries[i];
                    if (!_ghostByModel.TryGetValue(entry.Model, out var ghost))
                        continue;

                    if (Mathf.Abs(ghost.Start - entry.Start) <= 0.0001f && Mathf.Abs(ghost.Duration - entry.Duration) <= 0.0001f)
                        continue;

                    output.Add((entry.Model, ghost.Start, ghost.Duration));
                }
            }
        }

        public SegmentTimelineCanvas()
        {
            style.flexGrow = 1;
            style.backgroundColor = new Color(0.145f, 0.145f, 0.145f);
            style.overflow = Overflow.Hidden;
            focusable = true;
            
            _selectionOutline = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    display = DisplayStyle.None,
                    left = 0f,
                    top = 0f,
                    width = 0f,
                    height = 0f,
                    borderTopWidth = 2f,
                    borderBottomWidth = 2f,
                    borderLeftWidth = 2f,
                    borderRightWidth = 2f,
                    borderTopLeftRadius = 4f + OutlinePadding,
                    borderTopRightRadius = 4f + OutlinePadding,
                    borderBottomLeftRadius = 4f + OutlinePadding,
                    borderBottomRightRadius = 4f + OutlinePadding,
                    borderTopColor = new Color(0.35f, 0.65f, 1f, 0.45f),
                    borderBottomColor = new Color(0.35f, 0.65f, 1f, 0.45f),
                    borderLeftColor = new Color(0.35f, 0.65f, 1f, 0.45f),
                    borderRightColor = new Color(0.35f, 0.65f, 1f, 0.45f),
                    backgroundColor = new Color(0.35f, 0.65f, 1f, 0.15f),
                    transitionProperty = new List<StylePropertyName>() { "left", "top", "width", "height" },
                    transitionDuration = new List<TimeValue>() { TimeValue.Milliseconds(125) },
                    transitionTimingFunction = new List<EasingFunction>() { new EasingFunction(EasingMode.EaseOutCubic) },
                },
                pickingMode = PickingMode.Ignore,
            };
            Add(_selectionOutline);


            _contentRoot = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = 0,
                    right = 0,
                    top = 0,
                    bottom = 0,
                }
            };
            Add(_contentRoot);

            _playhead = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    width = 2f,
                    backgroundColor = PreviewAccent,
                    display = DisplayStyle.None,
                },
            };
            Add(_playhead);

            _snapGuide = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    width = 1f,
                    top = 0f,
                    height = 0f,
                    backgroundColor = new Color(0.35f, 0.65f, 1f, 0.9f),
                    display = DisplayStyle.None,
                },
                pickingMode = PickingMode.Ignore,
            };
            Add(_snapGuide);

            _marqueeBox = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    display = DisplayStyle.None,
                    left = 0f,
                    top = 0f,
                    width = 0f,
                    height = 0f,
                    borderTopWidth = 1f,
                    borderBottomWidth = 1f,
                    borderLeftWidth = 1f,
                    borderRightWidth = 1f,
                    borderTopColor = new Color(0.35f, 0.65f, 1f, 0.9f),
                    borderBottomColor = new Color(0.35f, 0.65f, 1f, 0.9f),
                    borderLeftColor = new Color(0.35f, 0.65f, 1f, 0.9f),
                    borderRightColor = new Color(0.35f, 0.65f, 1f, 0.9f),
                    backgroundColor = new Color(0.35f, 0.65f, 1f, 0.15f),
                },
                pickingMode = PickingMode.Ignore,
            };
            Add(_marqueeBox);

            generateVisualContent += DrawRuler;
            RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (!_viewWasEverFramed)
                    FrameAllInternal();
                RefreshLayout();
            });
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            this.AddManipulator(new ContextualMenuManipulator(BuildContextMenu));
        }

        public void SetView(IReadOnlyList<SegmentSelectionModel> activeModels, IReadOnlyList<SegmentSelectionModel> selectedModels)
        {
            ResetInteractionState();
            _selection.SetCommittedSelection(selectedModels);

            _models.Clear();
            if (activeModels != null)
            {
                for (int i = 0; i < activeModels.Count; i++)
                    _models.Add(activeModels[i]);
            }

            RebuildBlocks();

            if (!_viewWasEverFramed)
                FrameAllInternal();

            RebuildSnapTimes();
            RefreshLayout();
            MarkDirtyRepaint();
        }

        public void SetSelection(IReadOnlyList<SegmentSelectionModel> selectedModels)
        {
            _selection.SetCommittedSelection(selectedModels);
            RebuildSnapTimes();
            RefreshSelectionVisuals();
        }

        public void SetTime(float time)
        {
            _time = Mathf.Max(0f, time);
            PositionPlayhead();
        }

        public void SetPreviewActive(bool active)
        {
            _previewActive = active;
            _playhead.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            PositionPlayhead();
        }

        private void RebuildBlocks()
        {
            _contentRoot.Clear();
            _blocks.Clear();

            for (int i = 0; i < _models.Count; i++)
            {
                var model = _models[i];
                var editor = SegmentBlockEditorRegistry.GetEditor(model.Segment);
                var blockColors = editor.GetBlockColors(model.Segment);

                var planVisual = new PlanBlock
                {
                    Model = model,
                    Root = new VisualElement
                    {
                        style =
                        {
                            position = Position.Absolute,
                            height = LaneHeight,
                            borderTopLeftRadius = 4f,
                            borderTopRightRadius = 4f,
                            borderBottomLeftRadius = 4f,
                            borderBottomRightRadius = 4f,
                            borderTopWidth = 1f,
                            borderBottomWidth = 1f,
                            borderLeftWidth = 1f,
                            borderRightWidth = 1f,
                            borderTopColor = blockColors.border,
                            borderBottomColor = blockColors.border,
                            borderLeftColor = blockColors.border,
                            borderRightColor = blockColors.border,
                            backgroundColor = blockColors.fill,
                            transformOrigin = new TransformOrigin(Length.Percent(50f), Length.Percent(50f)),
                            transitionProperty = new List<StylePropertyName>() { "left", "top", "width", "height", "translate" },
                            transitionDuration = new List<TimeValue>() { TimeValue.Milliseconds(125) },
                            transitionTimingFunction = new List<EasingFunction>() { new EasingFunction(EasingMode.EaseOutCubic) },
                        }
                    },
                    SelectionHighlight = new VisualElement
                    {
                        style =
                        {
                            position = Position.Absolute,
                            left = -2f,
                            top = -2f,
                            right = -2f,
                            bottom = -2f,
                            borderTopWidth = 2f,
                            borderBottomWidth = 2f,
                            borderLeftWidth = 2f,
                            borderRightWidth = 2f,
                            borderTopLeftRadius = 6f,
                            borderTopRightRadius = 6f,
                            borderBottomLeftRadius = 6f,
                            borderBottomRightRadius = 6f,
                            borderTopColor = new Color(0.35f, 0.65f, 1f, 0.9f),
                            borderBottomColor = new Color(0.35f, 0.65f, 1f, 0.9f),
                            borderLeftColor = new Color(0.35f, 0.65f, 1f, 0.9f),
                            borderRightColor = new Color(0.35f, 0.65f, 1f, 0.9f),
                            backgroundColor = new Color(0.35f, 0.65f, 1f, 0.15f),
                            display = DisplayStyle.None,
                        },
                        pickingMode = PickingMode.Ignore,
                    }
                };

                planVisual.Root.Add(planVisual.SelectionHighlight);

                planVisual.Root.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0)
                        return;

                    Focus();

                    bool shift = evt.shiftKey;
                    bool action = evt.ctrlKey || evt.commandKey;

                    bool selectedBeforeClick = _selection.IsSelected(planVisual.Model);
                    if (!selectedBeforeClick)
                    {
                        RequestBlockClickSelection(planVisual, shift, action);

                        if (shift || action)
                        {
                            evt.StopPropagation();
                            return;
                        }
                    }

                    BeginPointerSession(new PointerSession
                    {
                        Mode = PointerSessionMode.BlockPress,
                        PointerId = evt.pointerId,
                        PointerStartWorld = evt.position,
                        PointerStartLocal = this.WorldToLocal(evt.position),
                        PressedBlock = planVisual,
                        Shift = shift,
                        Action = action,
                        BlockWasSelected = selectedBeforeClick,
                    });

                    evt.StopPropagation();
                });

                bool zeroDur = IsZeroDuration(model);
                if (zeroDur)
                {
                    planVisual.Root.style.borderTopWidth = 0f;
                    planVisual.Root.style.borderBottomWidth = 0f;
                    planVisual.Root.style.borderLeftWidth = 0f;
                    planVisual.Root.style.borderRightWidth = 0f;
                    planVisual.Root.style.backgroundColor = Color.clear;
                    planVisual.Root.style.overflow = Overflow.Visible;

                    var capturedModel = model;
                    var capturedRoot = planVisual.Root;
                    planVisual.Root.generateVisualContent += ctx =>
                    {
                        DrawZeroDurationMarker(ctx, capturedModel, capturedRoot);
                    };
                }
                else
                {
                    editor.OnBlockGUI(model.Segment, planVisual.Root);
                }

                _contentRoot.Add(planVisual.Root);
                _blocks.Add(planVisual);
            }

            RefreshSelectionVisuals();
        }

        private void RefreshSelectionVisuals()
        {
            for (int i = 0; i < _blocks.Count; i++)
            {
                var block = _blocks[i];
                bool selected = _selection.IsSelected(block.Model);
                block.SelectionHighlight.style.display = selected ? DisplayStyle.Flex : DisplayStyle.None;
            }

            using(var scope = new SkipTransitionsScope())
            {
                scope.Add(_selectionOutline);

                UpdateSelectionOutline();
            }

            MarkDirtyRepaint();
        }

        private bool TryGetSelectionBounds(out Rect bounds, bool includePadding)
        {
            var selection = _selection.EffectiveSelection;
            if (selection.Count == 0)
            {
                bounds = default;
                return false;
            }

            bool foundAny = false;
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;

            for (int i = 0; i < _blocks.Count; i++)
            {
                var block = _blocks[i];
                if (!_selection.IsSelected(block.Model))
                    continue;

                var layoutRect = block.LayoutRect;
                if (layoutRect.width <= 0f || layoutRect.height <= 0f)
                    continue;

                minX = Mathf.Min(minX, layoutRect.xMin);
                minY = Mathf.Min(minY, layoutRect.yMin);
                maxX = Mathf.Max(maxX, layoutRect.xMax);
                maxY = Mathf.Max(maxY, layoutRect.yMax);
                foundAny = true;
            }

            if (!foundAny)
            {
                bounds = default;
                return false;
            }

            bounds = includePadding
                ? Rect.MinMaxRect(minX - OutlinePadding, minY - OutlinePadding, maxX + OutlinePadding, maxY + OutlinePadding)
                : Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }

        private void UpdateSelectionOutline()
        {
            if (!TryGetSelectionBounds(out var bounds, includePadding: true))
            {
                _selectionOutline.style.display = DisplayStyle.None;
                return;
            }

            _selectionOutline.style.left = bounds.xMin;
            _selectionOutline.style.top = bounds.yMin;
            _selectionOutline.style.width = bounds.width;
            _selectionOutline.style.height = bounds.height;
            _selectionOutline.style.display = DisplayStyle.Flex;
        }

        private void RequestBlockClickSelection(PlanBlock clickedBlock, bool shift, bool action)
        {
            if (clickedBlock?.Model == null)
                return;

            List<SegmentSelectionModel> targetSelection;
            if (shift)
                targetSelection = _selection.BuildRangeSelection(clickedBlock.Model, _blocks);
            else if (action)
                targetSelection = _selection.BuildToggleSelection(clickedBlock.Model);
            else
                targetSelection = _selection.BuildSingleSelection(clickedBlock.Model);

            SelectionChanged?.Invoke(targetSelection);
        }

        private void BeginPointerSession(PointerSession session)
        {
            _pointerSession = session;
            this.CapturePointer(session.PointerId);
        }

        private void EndPointerSession()
        {
            if (_pointerSession == null)
                return;

            int pointerId = _pointerSession.PointerId;
            _pointerSession = null;

            if (this.HasPointerCapture(pointerId))
                this.ReleasePointer(pointerId);
        }

        private void ResetInteractionState()
        {
            if (_selectionTransform != null)
                _selectionTransform = null;

            if (_marqueeBox.style.display != DisplayStyle.None)
                _marqueeBox.style.display = DisplayStyle.None;

            HideSnapGuide();
            EndPointerSession();
        }

        private static bool HasExceededDragThreshold(PointerSession session, Vector2 worldPosition)
        {
            if (session == null)
                return false;

            float thresholdSq = TransformDragThresholdPx * TransformDragThresholdPx;
            return (worldPosition - session.PointerStartWorld).sqrMagnitude >= thresholdSq;
        }

        private void FrameAllInternal()
        {
            float width = resolvedStyle.width;
            if (float.IsNaN(width) || width < 10f)
                return;

            _viewWasEverFramed = true;
            float maxEnd = 1f;
            for (int i = 0; i < _models.Count; i++)
                maxEnd = Mathf.Max(maxEnd, _models[i].EndTime);

            float padding = maxEnd * 0.05f;
            float viewStart = -padding;
            float viewEnd = maxEnd + padding;

            float contentWidth = Mathf.Max(width - HorizontalPadding * 2f, 10f);
            _pixelsPerSecond = Mathf.Clamp(contentWidth / Mathf.Max(viewEnd - viewStart, 0.5f), MinZoom, MaxZoom);
            _viewStart = viewStart;
        }

        private bool BeginSelectionTransformFromPointer(Vector2 worldPosition, int pointerId)
        {
            if (!TryGetSelectionTransformStart(worldPosition, out DragKind kind, out float selectionStartTime, out float selectionDurationTime))
                return false;

            _selectionTransform = new SelectionTransformOperation(
                kind,
                pointerId,
                worldPosition,
                selectionStartTime,
                selectionDurationTime,
                _selection.EffectiveSelection);
            return true;
        }

        private bool TryGetSelectionTransformStart(Vector2 worldPosition, out DragKind kind, out float selectionStartTime, out float selectionDurationTime)
        {
            kind = DragKind.None;
            selectionStartTime = 0f;
            selectionDurationTime = 0f;

            if (!TryGetSelectionBounds(out var bounds, includePadding: true))
                return false;

            if (!TryGetSelectionTimeBounds(out selectionStartTime, out selectionDurationTime))
                return false;

            var local = this.WorldToLocal(worldPosition);
            if (!bounds.Contains(local))
                return false;

            GetSelectionCapabilities(out bool canMove, out bool canResizeLeft, out bool canResizeRight);

            if (canResizeLeft && local.x <= bounds.xMin + ResizeHandlePx)
                kind = DragKind.ResizeLeft;
            else if (canResizeRight && local.x >= bounds.xMax - ResizeHandlePx)
                kind = DragKind.ResizeRight;
            else if (canMove)
                kind = DragKind.Move;

            return kind != DragKind.None;
        }

        private bool TryGetSelectionTimeBounds(out float start, out float duration)
        {
            var selection = _selection.EffectiveSelection;
            if (selection.Count == 0)
            {
                start = 0f;
                duration = 0f;
                return false;
            }

            float minStart = float.MaxValue;
            float maxEnd = float.MinValue;

            for (int i = 0; i < selection.Count; i++)
            {
                var model = selection[i];
                if (model == null)
                    continue;

                minStart = Mathf.Min(minStart, model.StartTime);
                maxEnd = Mathf.Max(maxEnd, model.EndTime);
            }

            if (minStart == float.MaxValue || maxEnd == float.MinValue)
            {
                start = 0f;
                duration = 0f;
                return false;
            }

            start = minStart;
            duration = Mathf.Max(maxEnd - minStart, 0.0001f);
            return true;
        }

        private void GetSelectionCapabilities(out bool canMove, out bool canResizeLeft, out bool canResizeRight)
        {
            canMove = false;
            canResizeLeft = false;
            canResizeRight = false;

            var selected = _selection.EffectiveSelection;
            for (int i = 0; i < selected.Count; i++)
            {
                var model = selected[i];
                if (model == null)
                    continue;

                canMove |= model.CanAdjustStartTime;
                canResizeLeft |= model.CanAdjustStartTime && model.CanAdjustDuration;
                canResizeRight |= model.CanAdjustDuration;
            }

            if(!canResizeLeft && !canResizeRight)
            {
                int adjustableStartCount = 0;
                float lastAdjustableStart = 0f;
                for (int i = 0; i < selected.Count; i++)
                {
                    var model = selected[i];
                    if (model == null || !model.CanAdjustStartTime)
                        continue;

                    if(adjustableStartCount == 0)
                    {
                        adjustableStartCount++;
                        lastAdjustableStart = model.StartTime;
                    }
                    else if(Mathf.Abs(model.StartTime - lastAdjustableStart) > 0.0001f)
                    {
                        canResizeLeft = true;
                        canResizeRight = true;
                        break;
                    }
                }
            }
        }

        private void RefreshLayout()
        {
            float width = resolvedStyle.width;
            if (float.IsNaN(width) || width < 10f)
                return;

            using var scope = new SkipTransitionsScope();
            for (int i = 0; i < _blocks.Count; i++)
                scope.Add(_blocks[i].Root);
            scope.Add(_selectionOutline);

            LayoutBlocksInLanes();
            PositionPlayhead();
            UpdateSelectionOutline();

            RebuildRulerLabels();
            MarkDirtyRepaint();
        }

        private void PositionPlayhead()
        {
            if (_playhead == null)
                return;

            _playhead.style.left = TimeToX(_time) - 1f;
            _playhead.style.top = 0f;
            _playhead.style.height = Mathf.Max(0f, resolvedStyle.height);
        }

        private void SetSnapGuide(float time)
        {
            _snapGuide.style.left = TimeToX(time);
            _snapGuide.style.top = 0f;
            _snapGuide.style.height = Mathf.Max(0f, resolvedStyle.height);
            _snapGuide.style.display = DisplayStyle.Flex;
        }

        private void HideSnapGuide()
        {
            _snapGuide.style.display = DisplayStyle.None;
        }

        private void LayoutBlocksInLanes()
        {
            var packed = LanePacker.Pack(
                items: _blocks,
                itemToInput: block =>
                {
                    var editor = SegmentBlockEditorRegistry.GetEditor(block.Model.Segment);

                    return new ()
                    {
                        Data = block,
                        Start = GetBlockStart(),
                        End = GetBlockEnd(),
                        Group = editor.GetLanePackerGroup(block.Model.Segment),
                    };

                    float GetBlockStart()
                    {
                        var isZero = IsZeroDuration(block.Model);
                        GetDisplayTiming(block.Model, out float referenceStart, out float referenceDuration);
                        var start = isZero ? referenceStart - (MinDurationPx * 0.5f) / Mathf.Max(_pixelsPerSecond, 0.0001f) : referenceStart;
                        start = Mathf.Max(0f, start);
                        return start;
                    }

                    float GetBlockEnd()
                    {
                        var isZero = IsZeroDuration(block.Model);
                        GetDisplayTiming(block.Model, out float referenceStart, out float referenceDuration);
                        float referenceEnd = referenceStart + referenceDuration;
                        var end = isZero ? referenceEnd + (MinDurationPx * 0.5f) / Mathf.Max(_pixelsPerSecond, 0.0001f) : referenceEnd;
                        end = Mathf.Max(0f, end);
                        return end;
                    }
                },
                depenetrateAndCompact: true
            );

            for (int i = 0; i < packed.Count; i++)
            {
                var item = packed[i];
                var block = item.Item;
                var isZero = IsZeroDuration(block.Model);

                GetDisplayTiming(block.Model, out float start, out float duration);

                var top = LaneTop + item.Lane * (LaneHeight + LaneGap);
                var left = TimeToX(start);

                var width = Mathf.Max(TimeToX(start + duration) - left, MinDurationPx);
                if (isZero)
                    left -= width * 0.5f;

                block.Root.style.left = left;
                block.Root.style.top = top;
                block.Root.style.width = width;
                block.LayoutRect = new Rect(left, top, width, LaneHeight);
            }

            UpdateSelectionOutline();
        }

        private void GetDisplayTiming(SegmentSelectionModel model, out float start, out float duration)
        {
            if (_selectionTransform != null && _selectionTransform.TryGetGhost(model, out start, out duration))
                return;

            start = model.StartTime;
            duration = model.Duration;
        }

        private void RebuildSnapTimes()
        {
            _segmentSnapCandidates.Clear();

            HashSet<int> distinctTimes = new HashSet<int>();

            void AddDistinct(float t)
            {
                var id = Mathf.RoundToInt(t * 10000f);
                if (!distinctTimes.Add(id))
                    return;

                _segmentSnapCandidates.Add(new SegmentSnapCandidate(t));
            }

            for (int i = 0; i < _models.Count; i++)
            {
                var model = _models[i];
                if (_selection.IsSelected(model))
                    continue;

                AddDistinct(model.StartTime);
                AddDistinct(model.EndTime);

                var plan = model.Segment.GetPlan(null);
                foreach(var childPlan in plan.Children)
                {
                    AddDistinct(childPlan.Timing.AbsoluteStartTime);
                    AddDistinct(childPlan.Timing.AbsoluteEndTime);
                }
            }
        }

        private bool TryGetSnapAdjustedDelta(DragKind kind, float rawDt, out float snappedDt, out float snappedTime)
        {
            snappedDt = rawDt;
            snappedTime = 0f;

            if (_selectionTransform == null)
                return false;

            float initialStart = _selectionTransform.InitialSelectionStart;
            float initialEnd = initialStart + _selectionTransform.InitialSelectionDuration;
            float thresholdTime = SnapThresholdPx / Mathf.Max(_pixelsPerSecond, 0.0001f);

            bool foundKeyframe = false;
            float bestDelta = 0f;
            float bestAbs = float.MaxValue;
            float bestSnapTime = 0f;
            bool bestIsSegmentSnap = false;

            void Consider(float edgeTime)
            {
                if (!TryFindNearestSegmentSnapDelta(edgeTime, thresholdTime, out var d, out var s))
                    return;

                float abs = Mathf.Abs(d);
                if (abs < bestAbs)
                {
                    bestAbs = abs;
                    bestDelta = d;
                    bestSnapTime = s;
                    bestIsSegmentSnap = true;
                    foundKeyframe = true;
                }
            }

            switch (kind)
            {
                case DragKind.Move:
                    Consider(initialStart + rawDt);
                    Consider(initialEnd + rawDt);
                    break;
                case DragKind.ResizeLeft:
                    Consider(initialStart + rawDt);
                    break;
                case DragKind.ResizeRight:
                    Consider(initialEnd + rawDt);
                    break;
            }

            if (!foundKeyframe)
            {
                float gridIncrement = MajorTickStep() * 0.25f;
                if (gridIncrement <= 0f)
                    return false;

                void ConsiderGrid(float edgeTime)
                {
                    float snapped = Mathf.Round(edgeTime / gridIncrement) * gridIncrement;
                    float d = snapped - edgeTime;
                    float abs = Mathf.Abs(d);
                    if (abs <= thresholdTime && abs < bestAbs)
                    {
                        bestAbs = abs;
                        bestDelta = d;
                        bestSnapTime = snapped;
                        bestIsSegmentSnap = false;
                    }
                }

                bestAbs = float.MaxValue;
                switch (kind)
                {
                    case DragKind.Move:
                        ConsiderGrid(initialStart + rawDt);
                        break;
                    case DragKind.ResizeLeft:
                        ConsiderGrid(initialStart + rawDt);
                        break;
                    case DragKind.ResizeRight:
                        ConsiderGrid(initialEnd + rawDt);
                        break;
                }

                if (bestAbs == float.MaxValue)
                    return false;
            }

            snappedDt = rawDt + bestDelta;
            snappedTime = bestSnapTime;

            if (foundKeyframe && bestIsSegmentSnap)
                SetSnapGuide(snappedTime);
            else
                HideSnapGuide();

            return true;
        }

        private bool TryFindNearestSegmentSnapDelta(float edgeTime, float threshold, out float delta, out float snapTime)
        {
            delta = 0f;
            snapTime = 0f;

            float bestAbs = float.MaxValue;
            for (int i = 0; i < _segmentSnapCandidates.Count; i++)
            {
                var candidate = _segmentSnapCandidates[i];
                float d = candidate.Time - edgeTime;
                float abs = Mathf.Abs(d);
                if (abs <= threshold && abs < bestAbs)
                {
                    bestAbs = abs;
                    delta = d;
                    snapTime = candidate.Time;
                }
            }

            return bestAbs != float.MaxValue;
        }
        private void RebuildRulerLabels()
        {
            float width = resolvedStyle.width;
            if (float.IsNaN(width) || width < 10f)
                return;

            float step = MajorTickStep();
            float first = Mathf.Floor((XToTime(0f)) / step) * step;

            int needed = 0;
            for (float t = first; TimeToX(t) <= width + 1f; t += step)
                needed++;

            while (_rulerLabels.Count < needed)
            {
                var label = new Label
                {
                    style =
                    {
                        position = Position.Absolute,
                        top = 2f,
                        fontSize = 10,
                        color = new Color(0.8f, 0.8f, 0.8f),
                        unityTextAlign = TextAnchor.UpperLeft,
                    },
                    pickingMode = PickingMode.Ignore,
                };
                _rulerLabels.Add(label);
                Add(label);
            }

            int used = 0;
            for (float t = first; TimeToX(t) <= width + 1f && used < _rulerLabels.Count; t += step, used++)
            {
                float x = TimeToX(t);
                var label = _rulerLabels[used];
                label.text = $"{t:0.##}";
                label.style.left = x + 2f;
                label.style.display = DisplayStyle.Flex;
            }

            for (int i = used; i < _rulerLabels.Count; i++)
                _rulerLabels[i].style.display = DisplayStyle.None;
        }

        private float TimeToX(float time) => HorizontalPadding + (time - _viewStart) * _pixelsPerSecond;
        private float XToTime(float x) => (x - HorizontalPadding) / Mathf.Max(_pixelsPerSecond, 0.0001f) + _viewStart;

        private float MajorTickStep()
        {
            float[] steps = { 0.01f, 0.02f, 0.05f, 0.1f, 0.2f, 0.5f, 1f, 2f, 5f, 10f, 20f, 30f, 60f };
            for (int i = 0; i < steps.Length; i++)
            {
                if (steps[i] * _pixelsPerSecond >= 70f)
                    return steps[i];
            }
            return steps[^1];
        }

        private void DrawRuler(MeshGenerationContext ctx)
        {
            var painter = ctx.painter2D;
            float width = resolvedStyle.width;
            float height = resolvedStyle.height;
            if (width <= 1f || height <= 1f)
                return;

            painter.fillColor = new Color(0.105f, 0.105f, 0.105f);
            painter.BeginPath();
            painter.MoveTo(new Vector2(0, 0));
            painter.LineTo(new Vector2(width, 0));
            painter.LineTo(new Vector2(width, RulerHeight));
            painter.LineTo(new Vector2(0, RulerHeight));
            painter.ClosePath();
            painter.Fill();

            painter.fillColor = new Color(0.145f, 0.145f, 0.145f);
            painter.BeginPath();
            painter.MoveTo(new Vector2(0, RulerHeight));
            painter.LineTo(new Vector2(width, RulerHeight));
            painter.LineTo(new Vector2(width, height));
            painter.LineTo(new Vector2(0, height));
            painter.ClosePath();
            painter.Fill();

            float maxEnd = 0f;
            for (int i = 0; i < _models.Count; i++)
                maxEnd = Mathf.Max(maxEnd, _models[i].EndTime);

            painter.fillColor = new Color(0.09f, 0.09f, 0.09f);
            float x0 = TimeToX(0f);
            if (x0 > 0f)
            {
                painter.BeginPath();
                painter.MoveTo(new Vector2(0, RulerHeight));
                painter.LineTo(new Vector2(x0, RulerHeight));
                painter.LineTo(new Vector2(x0, height));
                painter.LineTo(new Vector2(0, height));
                painter.ClosePath();
                painter.Fill();
            }

            float xEnd = TimeToX(maxEnd);
            if (maxEnd > 0f && xEnd < width)
            {
                painter.BeginPath();
                painter.MoveTo(new Vector2(xEnd, RulerHeight));
                painter.LineTo(new Vector2(width, RulerHeight));
                painter.LineTo(new Vector2(width, height));
                painter.LineTo(new Vector2(xEnd, height));
                painter.ClosePath();
                painter.Fill();
            }

            float major = MajorTickStep();
            float minor = major * 0.25f;
            float half = major * 0.5f;
            float firstMinor = Mathf.Floor(XToTime(0f) / minor) * minor;

            for (float t = firstMinor; TimeToX(t) <= width + 1f; t += minor)
            {
                float x = TimeToX(t);
                bool isMajor = Mathf.Abs(Mathf.Round(t / major) * major - t) < 0.0001f;
                bool isHalf = Mathf.Abs(Mathf.Round(t / half) * half - t) < 0.0001f;

                float tickBottom = isMajor ? height : isHalf ? RulerHeight : RulerHeight * 0.5f;
                painter.strokeColor = isMajor
                    ? new Color(1f, 1f, 1f, 0.16f)
                    : new Color(1f, 1f, 1f, 0.08f);
                painter.lineWidth = 1f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, 0f));
                painter.LineTo(new Vector2(x, tickBottom));
                painter.Stroke();
            }

            painter.strokeColor = new Color(0f, 0f, 0f, 0.35f);
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, RulerHeight));
            painter.LineTo(new Vector2(width, RulerHeight));
            painter.Stroke();
        }

        private void OnWheel(WheelEvent evt)
        {
            var local = this.WorldToLocal(evt.mousePosition);
            float timeAtCursor = XToTime(local.x);

            float zoomFactor = evt.delta.y > 0f ? 0.9f : 1.1f;
            _pixelsPerSecond = Mathf.Clamp(_pixelsPerSecond * zoomFactor, MinZoom, MaxZoom);
            _viewStart = timeAtCursor - (local.x - HorizontalPadding) / Mathf.Max(_pixelsPerSecond, 0.0001f);

            RefreshLayout();
            evt.StopPropagation();
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button == 0)
            {
                Focus();
                var local = this.WorldToLocal(evt.position);
                bool clickedRuler = local.y <= RulerHeight;
                if (clickedRuler)
                {
                    SeekRequested?.Invoke(XToTime(local.x));
                    BeginPointerSession(new PointerSession
                    {
                        Mode = PointerSessionMode.Scrub,
                        PointerId = evt.pointerId,
                        PointerStartWorld = evt.position,
                        PointerStartLocal = local,
                    });
                    evt.StopPropagation();
                    return;
                }

                if (TryGetSelectionTransformStart(evt.position, out _, out _, out _))
                {
                    BeginPointerSession(new PointerSession
                    {
                        Mode = PointerSessionMode.SelectionPress,
                        PointerId = evt.pointerId,
                        PointerStartWorld = evt.position,
                        PointerStartLocal = local,
                    });
                    evt.StopPropagation();
                    return;
                }

                BeginPointerSession(new PointerSession
                {
                    Mode = PointerSessionMode.BackgroundPress,
                    PointerId = evt.pointerId,
                    PointerStartWorld = evt.position,
                    PointerStartLocal = local,
                    MarqueeMode = evt.shiftKey
                        ? MarqueeMode.Additive
                        : (evt.ctrlKey || evt.commandKey)
                            ? MarqueeMode.Subtractive
                            : MarqueeMode.Replace,
                });

                evt.StopPropagation();
                return;
            }

            if (evt.button != 1 && evt.button != 2)
                return;

            BeginPointerSession(new PointerSession
            {
                Mode = PointerSessionMode.Pan,
                PointerId = evt.pointerId,
                PointerStartWorld = evt.position,
                PointerStartLocal = this.WorldToLocal(evt.position),
                PanStartPointer = evt.position,
                PanStartView = _viewStart,
            });
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_pointerSession == null || _pointerSession.PointerId != evt.pointerId || !this.HasPointerCapture(evt.pointerId))
                return;

            switch (_pointerSession.Mode)
            {
                case PointerSessionMode.BackgroundPress:
                {
                    var local = this.WorldToLocal(evt.position);

                    if ((local - _pointerSession.PointerStartLocal).sqrMagnitude < 9f)
                    {
                        evt.StopPropagation();
                        return;
                    }

                    _pointerSession.Mode = PointerSessionMode.Marquee;
                    _selection.BeginMarquee(_pointerSession.MarqueeMode);
                    _marqueeBox.style.display = DisplayStyle.Flex;

                    var rect = UpdateMarqueeBoxRect(local);
                    var hits = CollectMarqueeHits(rect);
                    _selection.UpdateMarquee(hits);
                    RefreshSelectionVisuals();

                    evt.StopPropagation();
                    return;
                }

                case PointerSessionMode.Marquee:
                {
                    var local = this.WorldToLocal(evt.position);
                    var rect = UpdateMarqueeBoxRect(local);
                    var hits = CollectMarqueeHits(rect);
                    _selection.UpdateMarquee(hits);
                    RefreshSelectionVisuals();

                    evt.StopPropagation();
                    return;
                }

                case PointerSessionMode.Scrub:
                {
                    var local = this.WorldToLocal(evt.position);
                    SeekRequested?.Invoke(XToTime(local.x));
                    evt.StopPropagation();
                    return;
                }

                case PointerSessionMode.SelectionPress:
                    if (!HasExceededDragThreshold(_pointerSession, evt.position))
                    {
                        evt.StopPropagation();
                        return;
                    }

                    if (BeginSelectionTransformFromPointer(_pointerSession.PointerStartWorld, evt.pointerId))
                    {
                        _pointerSession.Mode = PointerSessionMode.Transform;
                        RebuildSnapTimes();
                    }

                    evt.StopPropagation();
                    return;

                case PointerSessionMode.BlockPress:
                    if (!HasExceededDragThreshold(_pointerSession, evt.position))
                    {
                        evt.StopPropagation();
                        return;
                    }

                    if (_pointerSession.BlockWasSelected || (!_pointerSession.Shift && !_pointerSession.Action))
                    {
                        if (BeginSelectionTransformFromPointer(_pointerSession.PointerStartWorld, evt.pointerId))
                        {
                            _pointerSession.Mode = PointerSessionMode.Transform;
                            RebuildSnapTimes();
                        }
                    }

                    evt.StopPropagation();
                    return;

                case PointerSessionMode.Transform:
                {
                    float dx = evt.position.x - _selectionTransform.PointerStart.x;
                    float dt = dx / Mathf.Max(_pixelsPerSecond, 0.0001f);

                    bool shouldSnap = (evt.ctrlKey || evt.commandKey) || Snap;
                    if (shouldSnap && TryGetSnapAdjustedDelta(_selectionTransform.Kind, dt, out var snappedDt, out _))
                    {
                        dt = snappedDt;
                    }
                    else
                    {
                        HideSnapGuide();
                    }

                    _selectionTransform.UpdateGhost(dt);

                    LayoutBlocksInLanes();

                    evt.StopPropagation();
                    return;
                }

                case PointerSessionMode.Pan:
                {
                    float panDx = evt.position.x - _pointerSession.PanStartPointer.x;
                    _viewStart = _pointerSession.PanStartView - panDx / Mathf.Max(_pixelsPerSecond, 0.0001f);
                    RefreshLayout();
                    evt.StopPropagation();
                    return;
                }
            }
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (_pointerSession == null || _pointerSession.PointerId != evt.pointerId)
                return;

            switch (_pointerSession.Mode)
            {
                case PointerSessionMode.Marquee:
                {
                    var local = this.WorldToLocal(evt.position);
                    var rect = UpdateMarqueeBoxRect(local);
                    var hits = CollectMarqueeHits(rect);
                    _selection.UpdateMarquee(hits);
                    
                    var targetSelection = _selection.BuildCommittedMarqueeSelection();
                    _selection.EndMarquee();
                    SelectionChanged?.Invoke(targetSelection);
                    _marqueeBox.style.display = DisplayStyle.None;
                    EndPointerSession();
                    evt.StopPropagation();
                    return;
                }

                case PointerSessionMode.BackgroundPress:
                    if (_pointerSession.MarqueeMode == MarqueeMode.Replace)
                        SelectionChanged?.Invoke(Array.Empty<SegmentSelectionModel>());

                    _marqueeBox.style.display = DisplayStyle.None;
                    EndPointerSession();
                    evt.StopPropagation();
                    return;

                case PointerSessionMode.BlockPress:
                    if (_pointerSession.BlockWasSelected)
                        RequestBlockClickSelection(_pointerSession.PressedBlock, _pointerSession.Shift, _pointerSession.Action);

                    EndPointerSession();
                    evt.StopPropagation();
                    return;

                case PointerSessionMode.SelectionPress:
                    EndPointerSession();
                    evt.StopPropagation();
                    return;

                case PointerSessionMode.Scrub:
                {
                    var local = this.WorldToLocal(evt.position);
                    bool shouldSnap = (evt.ctrlKey || evt.commandKey) || Snap;
                    SeekRequested?.Invoke(SnapTime(XToTime(local.x), shouldSnap));

                    EndPointerSession();
                    evt.StopPropagation();
                    return;
                }

                case PointerSessionMode.Transform:
                {
                    if (_selectionTransform.HasChanges)
                    {
                        var changes = new List<(SegmentSelectionModel model, float start, float duration)>();
                        _selectionTransform.GetCommittedChanges(changes);
                        TimeAdjustmentCommitted?.Invoke(changes);
                    }

                    _selectionTransform = null;
                    HideSnapGuide();
                    RefreshLayout();
                    EndPointerSession();
                    evt.StopPropagation();
                    return;
                }

                case PointerSessionMode.Pan:
                {
                    EndPointerSession();
                    evt.StopPropagation();
                    return;
                }
            }
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace)
            {
                if (_selection.EffectiveSelection.Count > 0)
                {
                    DeleteRequested?.Invoke(_selection.EffectiveSelection);
                    evt.StopPropagation();
                }
                return;
            }

            if ((evt.ctrlKey || evt.commandKey) && evt.keyCode == KeyCode.C)
            {
                if (_selection.EffectiveSelection.Count > 0)
                    CopyRequested?.Invoke();
                return;
            }

            if ((evt.ctrlKey || evt.commandKey) && evt.keyCode == KeyCode.V)
            {
                PasteRequested?.Invoke();
                evt.StopPropagation();
                return;
            }

            if (evt.keyCode == KeyCode.F)
            {
                if (_selection.EffectiveSelection.Count > 0)
                    FrameSelection();
                else
                    FrameAllInternal();

                RefreshLayout();
                evt.StopPropagation();
            }
        }

        private void FrameSelection()
        {
            float width = resolvedStyle.width;
            if (float.IsNaN(width) || width < 10f || _selection.EffectiveSelection.Count == 0)
                return;

            float start = float.MaxValue;
            float end = float.MinValue;

            var selection = _selection.EffectiveSelection;
            for (int i = 0; i < selection.Count; i++)
            {
                start = Mathf.Min(start, selection[i].StartTime);
                end = Mathf.Max(end, selection[i].EndTime);
            }

            if (start == float.MaxValue || end == float.MinValue)
                return;

            float duration = end - start;

            float padding = Mathf.Max(duration * 0.5f, 0.5f);
            float viewStart = Mathf.Max(0f, start - padding);
            float viewEnd = end + padding;

            float contentWidth = Mathf.Max(width - HorizontalPadding * 2f, 10f);
            _pixelsPerSecond = Mathf.Clamp(contentWidth / Mathf.Max(viewEnd - viewStart, 0.5f), MinZoom, MaxZoom);
            _viewStart = viewStart;
        }

        private void BuildContextMenu(ContextualMenuPopulateEvent evt)
        {
            var hit = FindBlockAt(evt);

            if (hit != null || _selection.EffectiveSelection.Count > 0)
            {
                evt.menu.AppendAction(_selection.EffectiveSelection.Count > 0 ? "Delete Selection" : "Delete Segment", _ =>
                {
                    var selected = _selection.EffectiveSelection;
                    if (selected.Count > 0)
                        DeleteRequested?.Invoke(selected);
                    else
                        DeleteRequested?.Invoke(new[] { hit.Model });
                });
                evt.menu.AppendSeparator();
            }

            var addable = AddableSegmentTypeRegistry.AddableSegmentTypes;

            if (addable == null || addable.Count == 0)
                return;

            var local = this.WorldToLocal(evt.mousePosition);
            float addTime = SnapTime(XToTime(local.x), shouldSnap: false);
            for (int i = 0; i < addable.Count; i++)
            {
                var addableEntry = addable[i];
                evt.menu.AppendAction($"Add/{addableEntry.menuName}", _ => AddRequested?.Invoke(addableEntry.type, addTime));
            }
        }

        private PlanBlock FindBlockAt(IMouseEvent evt)
        {
            for (int i = _blocks.Count - 1; i >= 0; i--)
            {
                var blockLocal = _blocks[i].Root.WorldToLocal(evt.mousePosition);
                if (_blocks[i].Root.contentRect.Contains(blockLocal)) 
                    return _blocks[i];
            }
            return null;
        }

        private Rect UpdateMarqueeBoxRect(Vector2 currentLocal)
        {
            var startLocal = _pointerSession != null ? _pointerSession.PointerStartLocal : currentLocal;
            var min = Vector2.Min(startLocal, currentLocal);
            var max = Vector2.Max(startLocal, currentLocal);

            _marqueeBox.style.left = min.x;
            _marqueeBox.style.top = min.y;
            _marqueeBox.style.width = max.x - min.x;
            _marqueeBox.style.height = max.y - min.y;

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private List<SegmentSelectionModel> CollectMarqueeHits(Rect marqueeLocal)
        {
            var hits = new List<SegmentSelectionModel>();

            for (int i = 0; i < _blocks.Count; i++)
            {
                if (_blocks[i].LayoutRect.Overlaps(marqueeLocal))
                    hits.Add(_blocks[i].Model);
            }

            return hits;
        }

        private float SnapTime(float time, bool shouldSnap)
        {
            if (!shouldSnap)
                return time;

            float increment = MajorTickStep() * 0.25f;
            if (increment <= 0f)
                return time;
            return Mathf.Round(time / increment) * increment;
        }

        private static bool IsZeroDuration(SegmentSelectionModel model) => model.Duration < 0.0001f;

        private void DrawZeroDurationMarker(MeshGenerationContext ctx, SegmentSelectionModel model, VisualElement root)
        {
            var painter = ctx.painter2D;
            float w = root.resolvedStyle.width;
            float h = root.resolvedStyle.height;
            if (w <= 0f || h <= 0f)
                return;

            var editor = SegmentBlockEditorRegistry.GetEditor(model.Segment);
            var colors = editor.GetBlockColors(model.Segment);
            float cx = w * 0.5f;

            painter.strokeColor = colors.border;
            painter.lineWidth = 2f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(cx, 0f));
            painter.LineTo(new Vector2(cx, h));
            painter.Stroke();

            float triW = 8f;
            float triH = 6f;
            painter.fillColor = colors.border;
            painter.BeginPath();
            painter.MoveTo(new Vector2(cx - triW * 0.5f, 0f));
            painter.LineTo(new Vector2(cx + triW * 0.5f, 0f));
            painter.LineTo(new Vector2(cx, triH));
            painter.ClosePath();
            painter.Fill();

            painter.BeginPath();
            painter.MoveTo(new Vector2(cx - triW * 0.5f, h));
            painter.LineTo(new Vector2(cx + triW * 0.5f, h));
            painter.LineTo(new Vector2(cx, h - triH));
            painter.ClosePath();
            painter.Fill();
        }
    }
}