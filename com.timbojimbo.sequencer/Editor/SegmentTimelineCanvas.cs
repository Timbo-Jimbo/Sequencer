using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UIElements;

public sealed class SegmentTimelineCanvas : VisualElement
{
    private const float RulerHeight = 24f;
    private const float LaneTop = RulerHeight + 8f;
    private const float LaneHeight = 48f;
    private const float LaneGap = 6f;
    private const float HorizontalPadding = 12f;
    private const float MinDurationPx = 12f;
    private const float MinZoom = 10f;
    private const float MaxZoom = 1600f;
    private const float ResizeHandlePx = 8f;
    private static readonly Color PreviewAccent = new Color(0.173f, 0.471f, 0.922f, 1.000f);

    private enum DragKind { None, ResizeLeft, ResizeRight, Move }

    public Action<SegmentPlan> SegmentSelected;
    public Action<SegmentPlan, float, float> TimeAdjustmentCommitted;
    public Action<SegmentPlan> DeleteRequested;
    public Action<Type, float> AddRequested;
    public Action<float> SeekRequested;
    public bool Snap = true;

    private readonly List<PlanBlock> _blocks = new();
    private readonly List<SegmentPlan> _activeLayer = new();
    private readonly List<Label> _rulerLabels = new();
    private readonly VisualElement _contentRoot;
    private readonly VisualElement _playhead;

    private SegmentPlan _activeRoot;
    private SegmentPlan _selected;
    private float _time;
    private bool _previewActive;
    private float _pixelsPerSecond = 120f;
    private float _viewStart;
    private bool _viewWasEverFramed;
    private bool _panning;
    private Vector2 _panStartPointer;
    private float _panStartView;

    private DragKind _dragKind;
    private int _dragPointerId = -1;
    private Vector2 _dragStartPointer;
    private float _dragOriginalStart;
    private float _dragOriginalDuration;
    private bool _dragChanged;
    private PlanBlock _dragBlock;
    private float _dragGhostStart;
    private float _dragGhostDuration;

    private bool _scrubbingTimeline;
    private int _scrubPointerId = -1;
    private bool IsDraggingBlock => _dragKind != DragKind.None && _dragBlock != null;

    private sealed class TransitionScope : System.IDisposable
    {
        private Dictionary<VisualElement, CachedTransitions> _cache;

        public TransitionScope()
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
        public SegmentPlan Plan;
        public VisualElement Root;
    }

    public SegmentTimelineCanvas()
    {
        style.flexGrow = 1;
        style.backgroundColor = new Color(0.145f, 0.145f, 0.145f);
        style.overflow = Overflow.Hidden;
        focusable = true;

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

    public void SetView(SegmentPlan activeRoot, IReadOnlyList<SegmentPlan> activeLayer, SegmentPlan selected)
    {
        _activeRoot = activeRoot;
        _selected = selected;

        _activeLayer.Clear();
        if (activeLayer != null)
        {
            for (int i = 0; i < activeLayer.Count; i++)
                _activeLayer.Add(activeLayer[i]);
        }

        RebuildBlocks();

        if (!_viewWasEverFramed)
            FrameAllInternal();

        RefreshLayout();
        MarkDirtyRepaint();
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

        for (int i = 0; i < _activeLayer.Count; i++)
        {
            var segmentPlan = _activeLayer[i];
            var planVisual = new PlanBlock
            {
                Plan = segmentPlan,
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
                        transitionProperty = new List<StylePropertyName>() { "left", "top", "width", "height" },
                        transitionDuration = new List<TimeValue>() { TimeValue.Milliseconds(125) },
                        transitionTimingFunction = new List<EasingFunction>() { new EasingFunction(EasingMode.EaseOutCubic) },
                    }
                }
            };

            planVisual.Root.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;

                Focus();
                _selected = planVisual.Plan;
                RefreshSelectionVisuals();
                SegmentSelected?.Invoke(planVisual.Plan);

                bool canResizeRight = planVisual.Plan.CanAdjustDuration;
                bool canResizeLeft = planVisual.Plan.CanAdjustDuration && planVisual.Plan.CanAdjustStartTime;
                bool canMove = planVisual.Plan.CanAdjustStartTime;
                float localX = planVisual.Root.WorldToLocal(evt.position).x;

                DragKind kind = DragKind.None;
                if (canResizeLeft && localX <= ResizeHandlePx)
                    kind = DragKind.ResizeLeft;
                else if (canResizeRight && localX >= planVisual.Root.contentRect.width - ResizeHandlePx)
                    kind = DragKind.ResizeRight;
                else if (canMove)
                    kind = DragKind.Move;

                if (kind != DragKind.None)
                    BeginBlockDrag(planVisual, evt, kind);

                evt.StopPropagation();
            });

            bool zeroDur = IsZeroDuration(segmentPlan);
            if (zeroDur)
            {
                planVisual.Root.style.borderTopWidth = 0f;
                planVisual.Root.style.borderBottomWidth = 0f;
                planVisual.Root.style.borderLeftWidth = 0f;
                planVisual.Root.style.borderRightWidth = 0f;
                planVisual.Root.style.backgroundColor = Color.clear;
                planVisual.Root.style.overflow = Overflow.Visible;

                var capturedPlan = segmentPlan;
                var capturedRoot = planVisual.Root;
                planVisual.Root.generateVisualContent += ctx =>
                {
                    bool sel = ReferenceEquals(capturedPlan, _selected);
                    DrawZeroDurationMarker(ctx, capturedPlan, sel, capturedRoot);
                };
            }
            else
            {
                var editor = SegmentEditorRegistry.GetEditor(segmentPlan.Segment);
                editor.OnBlockGUI(segmentPlan.Segment, planVisual.Root);
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
            bool selected = ReferenceEquals(block.Plan, _selected);

            var colors = GetBlockColors(block.Plan, selected);
            block.Root.style.backgroundColor = colors.fill;
            block.Root.style.borderTopColor = colors.border;
            block.Root.style.borderBottomColor = colors.border;
            block.Root.style.borderLeftColor = colors.border;
            block.Root.style.borderRightColor = colors.border;
        }

        MarkDirtyRepaint();
    }

    private void FrameAllInternal()
    {
        float width = resolvedStyle.width;
        if (float.IsNaN(width) || width < 10f)
            return;

        _viewWasEverFramed = true;
        float maxEnd = 1f;
        for (int i = 0; i < _activeLayer.Count; i++)
            maxEnd = Mathf.Max(maxEnd, _activeLayer[i].Timing.AbsoluteEndTime);

        float padding = maxEnd * 0.05f;
        float viewStart = -padding;
        float viewEnd = maxEnd + padding;

        float contentWidth = Mathf.Max(width - HorizontalPadding * 2f, 10f);
        _pixelsPerSecond = Mathf.Clamp(contentWidth / Mathf.Max(viewEnd - viewStart, 0.5f), MinZoom, MaxZoom);
        _viewStart = viewStart;
    }

    private void BeginBlockDrag(PlanBlock block, PointerDownEvent evt, DragKind kind)
    {
        _dragKind = kind;
        _dragPointerId = evt.pointerId;
        _dragStartPointer = evt.position;
        _dragOriginalStart = block.Plan.Timing.AbsoluteStartTime;
        _dragOriginalDuration = block.Plan.Timing.AbsoluteDuration;
        _dragChanged = false;
        _dragBlock = block;
        _dragGhostStart = block.Plan.Timing.AbsoluteStartTime;
        _dragGhostDuration = block.Plan.Timing.AbsoluteDuration;
        this.CapturePointer(evt.pointerId);
    }

    private void RefreshLayout()
    {
        float width = resolvedStyle.width;
        if (float.IsNaN(width) || width < 10f)
            return;

        using var scope = new TransitionScope();
        for (int i = 0; i < _blocks.Count; i++)
            scope.Add(_blocks[i].Root);

        LayoutBlocksInLanes();
        PositionPlayhead();

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

    private void LayoutBlocksInLanes()
    {
        var packed = TimelineEditorUtility.PackIntoLanes(
            _blocks,
            itemStart: block =>
            {
                var isZero = IsZeroDuration(block.Plan);
                var isBeingDragged = IsDraggingBlock && ReferenceEquals(block, _dragBlock);
                float referenceStart = isBeingDragged ? _dragGhostStart : block.Plan.Timing.AbsoluteStartTime;
                var start = isZero ? referenceStart - (MinDurationPx * 0.5f) / Mathf.Max(_pixelsPerSecond, 0.0001f) : referenceStart;
                start = Mathf.Max(0f, start);
                return start;
            },
            itemEnd: block =>
            {
                var isZero = IsZeroDuration(block.Plan);
                var isBeingDragged = IsDraggingBlock && ReferenceEquals(block, _dragBlock);
                float referenceEnd = isBeingDragged ? _dragGhostStart + _dragGhostDuration : block.Plan.Timing.AbsoluteEndTime;
                var end = isZero ? referenceEnd + (MinDurationPx * 0.5f) / Mathf.Max(_pixelsPerSecond, 0.0001f) : referenceEnd;
                end = Mathf.Max(0f, end);
                return end;
            }
        );

        var maxAbsoluteEnd = packed.Count > 0 ? packed.Max(entry => entry.Item.Plan.Timing.AbsoluteEndTime) : 0f;

        for (int i = 0; i < packed.Count; i++)
        {
            var item = packed[i];
            var block = item.Item;
            var isZero = IsZeroDuration(block.Plan);

            var start = (IsDraggingBlock && ReferenceEquals(block, _dragBlock)) ? _dragGhostStart : block.Plan.Timing.AbsoluteStartTime;
            var duration = (IsDraggingBlock && ReferenceEquals(block, _dragBlock)) ? _dragGhostDuration : block.Plan.Timing.AbsoluteDuration;

            var top = LaneTop + item.Lane * (LaneHeight + LaneGap);
            var left = TimeToX(start);

            var width = Mathf.Max(TimeToX(start + duration) - left, MinDurationPx);
            block.Root.style.left = left;
            block.Root.style.top = top;
            block.Root.style.width = width;
            
            // center align
            if(isZero)
                block.Root.style.left = left - (width * 0.5f);
        }
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

        // Ruler background
        painter.fillColor = new Color(0.105f, 0.105f, 0.105f);
        painter.BeginPath();
        painter.MoveTo(new Vector2(0, 0));
        painter.LineTo(new Vector2(width, 0));
        painter.LineTo(new Vector2(width, RulerHeight));
        painter.LineTo(new Vector2(0, RulerHeight));
        painter.ClosePath();
        painter.Fill();

        // Timeline body background
        painter.fillColor = new Color(0.145f, 0.145f, 0.145f);
        painter.BeginPath();
        painter.MoveTo(new Vector2(0, RulerHeight));
        painter.LineTo(new Vector2(width, RulerHeight));
        painter.LineTo(new Vector2(width, height));
        painter.LineTo(new Vector2(0, height));
        painter.ClosePath();
        painter.Fill();

        float maxEnd = 0f;
        for (int i = 0; i < _activeLayer.Count; i++)
            maxEnd = Mathf.Max(maxEnd, _activeLayer[i].Timing.AbsoluteEndTime);

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

        // Divider under ruler
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
            SeekRequested?.Invoke(XToTime(local.x));
            _scrubbingTimeline = true;
            _scrubPointerId = evt.pointerId;
            this.CapturePointer(evt.pointerId);

            _selected = null;
            RefreshSelectionVisuals();
            SegmentSelected?.Invoke(null);

            evt.StopPropagation();
            return;
        }

        if (evt.button != 1 && evt.button != 2)
            return;

        _panning = true;
        _panStartPointer = evt.position;
        _panStartView = _viewStart;
        this.CapturePointer(evt.pointerId);
        evt.StopPropagation();
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        if (_scrubbingTimeline && _scrubPointerId == evt.pointerId && this.HasPointerCapture(evt.pointerId))
        {
            var local = this.WorldToLocal(evt.position);
            SeekRequested?.Invoke(XToTime(local.x));
            evt.StopPropagation();
            return;
        }

        if (IsDraggingBlock && _dragBlock != null && _dragPointerId == evt.pointerId)
        {
            float dx = evt.position.x - _dragStartPointer.x;
            float dt = dx / Mathf.Max(_pixelsPerSecond, 0.0001f);

            switch (_dragKind)
            {
                case DragKind.ResizeLeft:
                {
                    // Left edge: move start time back by dt, increase duration by same dt (end stays fixed).
                    float newStart = _dragOriginalStart + dt;
                    newStart = Mathf.Max(0f, newStart);
                    newStart = SnapTime(newStart);
                    float startDelta = newStart - _dragOriginalStart;

                    float newDuration = Mathf.Max(0.01f, _dragOriginalDuration - startDelta);
                    newDuration = SnapTime(newDuration);
                    newDuration = Mathf.Max(0.01f, newDuration);

                    // Recompute start that matches the snapped duration.
                    newStart = _dragOriginalStart + (_dragOriginalDuration - newDuration);
                    newStart = Mathf.Max(0f, SnapTime(newStart));

                    _dragGhostStart = newStart;
                    _dragGhostDuration = newDuration;
                    _dragChanged = Mathf.Abs(newStart - _dragOriginalStart) > 0.0001f
                                || Mathf.Abs(newDuration - _dragOriginalDuration) > 0.0001f;
                    break;
                }
                case DragKind.ResizeRight:
                {
                    float duration = Mathf.Max(0.01f, _dragOriginalDuration + dt);
                    duration = SnapTime(duration);
                    duration = Mathf.Max(0.01f, duration);

                    _dragGhostDuration = duration;
                    _dragChanged = Mathf.Abs(duration - _dragOriginalDuration) > 0.0001f;
                    break;
                }
                case DragKind.Move:
                {
                    float newStart = SnapTime(_dragOriginalStart + dt);
                    _dragGhostStart = newStart;
                    _dragChanged = Mathf.Abs(newStart - _dragOriginalStart) > 0.0001f;
                    break;
                }
            }

            LayoutBlocksInLanes();

            evt.StopPropagation();
            return;
        }

        if (!_panning || !this.HasPointerCapture(evt.pointerId))
            return;

        float panDx = evt.position.x - _panStartPointer.x;
        _viewStart = _panStartView - panDx / Mathf.Max(_pixelsPerSecond, 0.0001f);
        RefreshLayout();
        evt.StopPropagation();
    }

    private void OnPointerUp(PointerUpEvent evt)
    {
        if (_scrubbingTimeline && _scrubPointerId == evt.pointerId)
        {
            var local = this.WorldToLocal(evt.position);
            SeekRequested?.Invoke(SnapTime(XToTime(local.x)));
            
            _scrubbingTimeline = false;
            _scrubPointerId = -1;
            
            if (this.HasPointerCapture(evt.pointerId))
                this.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
            return;
        }

        if (IsDraggingBlock && _dragBlock != null && _dragPointerId == evt.pointerId)
        {
            if (_dragChanged)
            {
                float dragDx = evt.position.x - _dragStartPointer.x;
                float dt = dragDx / Mathf.Max(_pixelsPerSecond, 0.0001f);

                float newStart;
                float newDuration;

                switch (_dragKind)
                {
                    case DragKind.ResizeLeft:
                    {
                        newStart = Mathf.Max(0f, _dragOriginalStart + dt);
                        newStart = SnapTime(newStart);
                        float startDelta = newStart - _dragOriginalStart;
                        newDuration = Mathf.Max(0.01f, _dragOriginalDuration - startDelta);
                        newDuration = Mathf.Max(0.01f, SnapTime(newDuration));
                        newStart = _dragOriginalStart + (_dragOriginalDuration - newDuration);
                        newStart = Mathf.Max(0f, SnapTime(newStart));
                        break;
                    }
                    case DragKind.ResizeRight:
                        newStart = _dragOriginalStart;
                        newDuration = Mathf.Max(0.01f, SnapTime(_dragOriginalDuration + dt));
                        break;
                    case DragKind.Move:
                        newStart = SnapTime(_dragOriginalStart + dt);
                        newDuration = _dragOriginalDuration;
                        break;
                    default:
                        newStart = _dragOriginalStart;
                        newDuration = _dragOriginalDuration;
                        break;
                }

                TimeAdjustmentCommitted?.Invoke(_dragBlock.Plan, newStart, newDuration);
            }

            _dragKind = DragKind.None;
            _dragPointerId = -1;
            _dragBlock = null;
            _dragChanged = false;
            if (this.HasPointerCapture(evt.pointerId))
                this.ReleasePointer(evt.pointerId);
            RefreshLayout();
            evt.StopPropagation();
            return;
        }

        if (!_panning || !this.HasPointerCapture(evt.pointerId))
            return;

        _panning = false;
        this.ReleasePointer(evt.pointerId);
        evt.StopPropagation();
    }

    private void OnKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace)
        {
            if (_selected != null)
            {
                DeleteRequested?.Invoke(_selected);
                evt.StopPropagation();
            }
            return;
        }

        if (evt.keyCode == KeyCode.F)
        {
            if (_selected != null)
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
        if (float.IsNaN(width) || width < 10f || _selected == null)
            return;

        float start = _selected.Timing.AbsoluteStartTime;
        float end = _selected.Timing.AbsoluteEndTime;
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

        if (hit != null)
        {
            evt.menu.AppendAction("Delete Segment", _ => DeleteRequested?.Invoke(hit.Plan));
            evt.menu.AppendSeparator();
        }

        var addable = AddableSegmentTypeRegistry.AddableSegmentTypes;

        if (addable == null || addable.Count == 0)
            return;

        var local = this.WorldToLocal(evt.mousePosition);
        float addTime = SnapTime(XToTime(local.x));
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

    private float SnapTime(float time)
    {
        if (!Snap)
            return time;

        float increment = MajorTickStep() * 0.25f;
        if (increment <= 0f)
            return time;
        return Mathf.Round(time / increment) * increment;
    }

    private static (Color fill, Color border) GetBlockColors(SegmentPlan segment, bool selected)
    {
        var editor = SegmentEditorRegistry.GetEditor(segment.Segment);
        return editor.GetBlockColors(segment.Segment, selected);
    }

    private static bool IsZeroDuration(SegmentPlan plan) => plan.Timing.AbsoluteDuration < 0.0001f;

    private void DrawZeroDurationMarker(MeshGenerationContext ctx, SegmentPlan plan, bool selected, VisualElement root)
    {
        var painter = ctx.painter2D;
        float w = root.resolvedStyle.width;
        float h = root.resolvedStyle.height;
        if (w <= 0f || h <= 0f)
            return;

        var colors = GetBlockColors(plan, selected);
        float cx = w * 0.5f;

        // Vertical line
        painter.strokeColor = colors.border;
        painter.lineWidth = 2f;
        painter.BeginPath();
        painter.MoveTo(new Vector2(cx, 0f));
        painter.LineTo(new Vector2(cx, h));
        painter.Stroke();

        // Top chevron — points DOWN (inward)
        float triW = 8f;
        float triH = 6f;
        painter.fillColor = colors.border;
        painter.BeginPath();
        painter.MoveTo(new Vector2(cx - triW * 0.5f, 0f));
        painter.LineTo(new Vector2(cx + triW * 0.5f, 0f));
        painter.LineTo(new Vector2(cx, triH));
        painter.ClosePath();
        painter.Fill();

        // Bottom chevron — points UP (inward)
        painter.BeginPath();
        painter.MoveTo(new Vector2(cx - triW * 0.5f, h));
        painter.LineTo(new Vector2(cx + triW * 0.5f, h));
        painter.LineTo(new Vector2(cx, h - triH));
        painter.ClosePath();
        painter.Fill();
    }
}
