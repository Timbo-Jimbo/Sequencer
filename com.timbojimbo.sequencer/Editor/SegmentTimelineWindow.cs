using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using TimboJimbo.PropertyBindings;
using TimboJimboEditor;
using TimboJimboEditor.PropertyBindings.Utility;
using UnityEditor.SceneManagement;

public sealed class SegmentTimelineWindow : EditorWindow
{
    private SequenceProvider _provider;
    private SegmentPlan _rootPlan;
    private SegmentPlan _activeEditRoot;
    private SegmentPlan _selected;

    private Label _providerLabel;
    private Label _infoLabel;
    private HelpBox _diagnostic;
    private SegmentTimelineCanvas _canvas;
    private VisualElement _inspectorHost;
    private ToolbarToggle _playToggle;
    private Label _previewIndicator;
    private VisualElement _canvasBorderOverlay;

    private SerializedObject _serializedProvider;

    private SegmentPreviewSession _previewSession;
    private float _displayTime;
    private double _lastTickTime;

    private ToolbarToggle _recordToggle;
    private Label _recordingIndicator;
    private UserEditTracker _editTracker;
    private PropertyBindingCollection _recordCollection;
    private List<BindableProperty> _recordableProperties;
    private readonly Dictionary<BindableProperty, ValueContainer> _recordSnapshotValues = new();

    private sealed class RecordedEdit
    {
        public Segment Segment;
        public bool Created;
        public string OriginalStateJson;
    }

    private readonly Dictionary<BindableProperty, RecordedEdit> _recordedEdits = new();

    private static IReadOnlyList<Type> _addableTypesCache;

    private bool IsPreviewing => _previewSession != null;
    private bool IsRecording => _editTracker != null;

    [MenuItem("Window/Segment Timeline")]
    public static void OpenFromMenu() => Open(Selection.activeGameObject != null
        ? Selection.activeGameObject.GetComponentInParent<SequenceProvider>()
        : null);

    public static void Open(SequenceProvider provider)
    {
        var window = GetWindow<SegmentTimelineWindow>("Segment Timeline");
        if (provider != null)
            window.SetProvider(provider);
    }

    private void OnEnable()
    {
        BuildUi();

        Selection.selectionChanged += OnSelectionChanged;
        Undo.undoRedoPerformed += OnUndoRedo;
        EditorApplication.update += OnEditorUpdate;
        OnSelectionChanged();
        
        EditorSceneManager.sceneSaving += OnSceneSaving;
        PrefabStage.prefabSaving += OnPrefabSaving;
    }

    private void OnDisable()
    {
        StopRecording(commit: false);
        Selection.selectionChanged -= OnSelectionChanged;
        Undo.undoRedoPerformed -= OnUndoRedo;
        EditorApplication.update -= OnEditorUpdate;
        EditorSceneManager.sceneSaving -= OnSceneSaving;
        PrefabStage.prefabSaving -= OnPrefabSaving;
        DisposePreviewSession();
        _serializedProvider?.Dispose();
        _serializedProvider = null;
    }

    private void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path)
    {
        if (IsRecording)
            StopRecording(commit: true);
        
        if (IsPreviewing)
            StopPreview();
    }

    private void OnPrefabSaving(GameObject prefab)
    {
        if (IsRecording)
            StopRecording(commit: true);
        
        if (IsPreviewing)
            StopPreview();
    }

    private void BuildUi()
    {
        rootVisualElement.Clear();

        var toolbar = new Toolbar();
        _providerLabel = new Label("(no provider)")
        {
            style =
            {
                unityTextAlign = TextAnchor.MiddleLeft,
                marginLeft = 6,
                marginRight = 12,
            }
        };
        toolbar.Add(_providerLabel);

        _playToggle = new ToolbarToggle { text = "Play" };
        _playToggle.RegisterValueChangedCallback(evt => SetPlaying(evt.newValue));
        toolbar.Add(_playToggle);

        toolbar.Add(new ToolbarButton(StopPreview) { text = "Stop" });

        _previewIndicator = new Label("Previewing")
        {
            style =
            {
                unityTextAlign = TextAnchor.MiddleLeft,
                marginLeft = 8,
                paddingLeft = 6,
                paddingRight = 6,
                backgroundColor = new Color(0.173f, 0.471f, 0.922f, 1.000f),
                display = DisplayStyle.None,
            },
        };

        if (SegmentRecorderRegistry.HasAnyRecorders())
        {
            _recordToggle = new ToolbarToggle { text = "● Rec" };
            _recordToggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue) StartRecording();
                else StopRecording(commit: true);
            });
            toolbar.Add(_recordToggle);

            _recordingIndicator = new Label("Recording")
            {
                style =
                {
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginLeft = 8,
                    paddingLeft = 6,
                    paddingRight = 6,
                    backgroundColor = new Color(0.8f, 0.2f, 0.2f, 1f),
                    display = DisplayStyle.None,
                },
            };
        }
        toolbar.Add(_previewIndicator);
        toolbar.Add(_recordingIndicator);


        rootVisualElement.Add(toolbar);

        _diagnostic = new HelpBox(string.Empty, HelpBoxMessageType.Info)
        {
            style = { display = DisplayStyle.None }
        };
        rootVisualElement.Add(_diagnostic);

        var split = new TwoPaneSplitView(0, 700, TwoPaneSplitViewOrientation.Horizontal)
        {
            style = { flexGrow = 1f }
        };

        var leftPane = new VisualElement { style = { flexGrow = 1f } };
        _canvas = new SegmentTimelineCanvas();
        _canvas.SegmentSelected += OnSegmentSelected;
        _canvas.TimeAdjustmentCommitted += OnTimeAdjustmentCommitted;
        _canvas.DeleteRequested += OnDeleteRequested;
        _canvas.AddRequested += OnAddRequested;
        _canvas.SeekRequested += OnSeekRequested;
        leftPane.Add(_canvas);

        _canvasBorderOverlay = new VisualElement
        {
            style =
            {
                position = Position.Absolute,
                top = 0,
                left = 0,
                right = 0,
                bottom = 0,
                borderTopWidth = 0,
                borderBottomWidth = 0,
                borderLeftWidth = 0,
                borderRightWidth = 0,
                borderTopColor = Color.clear,
                borderBottomColor = Color.clear,
                borderLeftColor = Color.clear,
                borderRightColor = Color.clear,
            },
            pickingMode = PickingMode.Ignore,
        };
        _canvas.Add(_canvasBorderOverlay);

        _infoLabel = new Label
        {
            style =
            {
                marginLeft = 8,
                marginBottom = 6,
                color = new Color(0.7f, 0.7f, 0.7f),
            }
        };
        leftPane.Add(_infoLabel);

        split.Add(leftPane);

        _inspectorHost = new ScrollView { style = { minWidth = 280 } };
        split.Add(_inspectorHost);

        rootVisualElement.Add(split);
        UpdatePreviewVisuals();
    }

    private void OnSelectionChanged()
    {
        var selectedGo = Selection.activeGameObject;
        var provider = selectedGo != null
            ? selectedGo.GetComponentInParent<SequenceProvider>()
            : null;

        if (_provider == null || provider != null)
            SetProvider(provider);
    }

    private void OnUndoRedo()
    {
        if (_provider == null)
            return;

        RefreshPlan(preserveSelection: true);
    }

    private void SetProvider(SequenceProvider provider)
    {
        if (ReferenceEquals(_provider, provider) && _rootPlan != null)
            return;

        StopRecording(commit: false);
        DisposePreviewSession();

        _provider = provider;
        _providerLabel.text = _provider != null
            ? $"{_provider.gameObject.name}"
            : "No Selected Provider";

        _serializedProvider?.Dispose();
        _serializedProvider = _provider != null ? new SerializedObject(_provider) : null;

        _selected = null;
        _activeEditRoot = null;
        _displayTime = 0f;
        _playToggle?.SetValueWithoutNotify(false);
        RefreshPlan(preserveSelection: false);
        UpdatePreviewVisuals();
    }

    private void RefreshPlan(bool preserveSelection)
    {
        if (_provider == null)
        {
            _rootPlan = null;
            _activeEditRoot = null;
            _selected = null;
            _infoLabel.text = "Select a SequenceSegmentProvider to begin.";
            _canvas.SetView(null, null, null);
            _canvas.SetPreviewActive(false);
            _canvas.SetTime(_displayTime);
            RebuildInspector();
            UpdateTimeUi();
            SetDiagnostic("No provider selected.", HelpBoxMessageType.Info, show: true);
            return;
        }

        _rootPlan = _provider.GetPlan();

        if (_rootPlan?.Segment is not Sequence)
        {
            _activeEditRoot = null;
            _selected = null;
            _canvas.SetView(null, null, null);
            _canvas.SetPreviewActive(false);
            _canvas.SetTime(_displayTime);
            RebuildInspector();
            UpdateTimeUi();
            SetDiagnostic("Root plan is invalid or not a SequenceSegment.", HelpBoxMessageType.Error, show: true);
            return;
        }

        _activeEditRoot = _rootPlan;

        var activeLayer = _activeEditRoot.Children;
        var rebuildInspector = false;

        if (preserveSelection && _selected != null)
        {
            bool stillExists = false;
            for (int i = 0; i < activeLayer.Count; i++)
            {
                if (ReferenceEquals(activeLayer[i].Segment, _selected.Segment))
                {
                    _selected = activeLayer[i];
                    stillExists = true;
                    break;
                }
            }

            if (!stillExists)
            {
                _selected = null;
                rebuildInspector = true;
            }
        }
        else if (!preserveSelection)
        {
            _selected = activeLayer.Count > 0 ? activeLayer[0] : null;
            rebuildInspector = true;
        }

        _canvas.SetView(_activeEditRoot, activeLayer, _selected);
        _infoLabel.text =
            "Editing one layer only: direct children of opened SequenceSegment. Nested SequenceSegment content is preview-only.";

        if (IsPreviewing)
        {
            _previewSession.Rebuild();
            _displayTime = Mathf.Min(_displayTime, _previewSession.Duration);
            _previewSession.Seek(_displayTime);
            if (_playToggle.value)
                _previewSession.SetPlaying(true);
        }

        _canvas.SetPreviewActive(IsPreviewing);
        _canvas.SetTime(_displayTime);
        UpdatePreviewVisuals();
        UpdateTimeUi();
        
        if (rebuildInspector)
            RebuildInspector();
    }

    private void OnSegmentSelected(SegmentPlan plan)
    {
        _selected = plan;
        RebuildInspector();
    }

    private void RebuildInspector()
    {
        _inspectorHost.Clear();

        if (_provider == null)
        {
            _inspectorHost.Add(new HelpBox("No provider selected.", HelpBoxMessageType.Info));
            return;
        }

        if (_selected == null)
        {
            _inspectorHost.Add(new HelpBox("Select a segment block to inspect.", HelpBoxMessageType.Info));
            return;
        }

        string selectedPath = SegmentLocator.FindPath(_provider.Sequence, _selected.Segment, "Sequence");
        if (selectedPath == null)
        {
            _inspectorHost.Add(new HelpBox("Could not locate serialized path for selected segment.", HelpBoxMessageType.Warning));
            return;
        }

        _serializedProvider.Update();
        var segmentProp = _serializedProvider.FindProperty(selectedPath);
        if (segmentProp == null)
        {
            _inspectorHost.Add(new HelpBox("Segment serialized property is no longer valid.", HelpBoxMessageType.Warning));
            return;
        }

        var editor = SegmentEditorRegistry.GetEditor(_selected.Segment);
        var capturedSegment = _selected.Segment;

        var container = new IMGUIContainer(() =>
        {
            _serializedProvider.Update();

            editor.OnInspectorGUI(capturedSegment, segmentProp);

            if (_serializedProvider.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(_provider);
                PrefabUtility.RecordPrefabInstancePropertyModifications(_provider);
                RefreshPlan(preserveSelection: true);
            }
        });
        container.style.marginLeft = 4;
        container.style.marginRight = 4;
        _inspectorHost.Add(container);
    }

    private void OnTimeAdjustmentCommitted(SegmentPlan plan, float newStart, float newDuration)
    {
        if (_provider == null || plan?.Segment == null)
            return;

        var segment = plan.Segment;
        var movable = segment as IStartTimeConfigurable;
        var resizable = segment as IDurationConfigurable;

        if (movable == null && resizable == null)
            return;

        Undo.RecordObject(_provider, "Adjust Segment Timing");

        if (movable != null)
            movable.SetStartTime(Mathf.Max(0f, newStart));

        if (resizable != null)
            resizable.SetDuration(Mathf.Max(0.01f, newDuration));

        CommitAuthoringChange();
    }

    private void OnDeleteRequested(SegmentPlan plan)
    {
        if (_provider == null || _activeEditRoot?.Segment is not Sequence root || plan?.Segment == null)
            return;

        int index = root.Segments.IndexOf(plan.Segment);
        if (index < 0)
            return;

        Undo.RecordObject(_provider, "Delete Segment");
        root.Segments.RemoveAt(index);
        CommitAuthoringChange();

        if (ReferenceEquals(_selected, plan))
        {
            _selected = null;
            RebuildInspector();
        }
    }

    private void OnAddRequested(Type type, float time)
    {
        if (_provider == null || _activeEditRoot?.Segment is not Sequence root)
            return;

        Segment created;
        try
        {
            created = (Segment)Activator.CreateInstance(type);
        }
        catch (Exception e)
        {
            Debug.LogError($"Could not create segment {type.Name}: {e.Message}");
            return;
        }

        if (created is IStartTimeConfigurable timeConfig)
            timeConfig.SetStartTime(Mathf.Max(0f, time));

        Undo.RecordObject(_provider, $"Add {type.Name}");
        root.Segments.Add(created);
        CommitAuthoringChange();
    }

    private void CommitAuthoringChange()
    {
        EditorUtility.SetDirty(_provider);
        PrefabUtility.RecordPrefabInstancePropertyModifications(_provider);
        RefreshPlan(preserveSelection: true);
    }

    private void EnsurePreviewSession()
    {
        if (_provider == null || IsPreviewing)
            return;

        _previewSession = SegmentPreviewSession.Acquire(_provider);
        _previewSession.Rebuilt += OnSessionRebuilt;
        _previewSession.Disposed += OnSessionDisposed;
        _displayTime = Mathf.Min(_displayTime, _previewSession.Duration);
        _previewSession.Seek(_displayTime);
        UpdatePreviewVisuals();
    }

    private void DisposePreviewSession()
    {
        if (!IsPreviewing)
            return;

        _previewSession.Rebuilt -= OnSessionRebuilt;
        _previewSession.Disposed -= OnSessionDisposed;
        _previewSession.Dispose();
        _previewSession = null;
        UpdatePreviewVisuals();
    }

    private void OnSessionRebuilt()
    {
        _canvas.SetTime(_displayTime);
        UpdateTimeUi();
        Repaint();
    }

    private void OnSessionDisposed()
    {
        _previewSession = null;
        _playToggle.SetValueWithoutNotify(false);
        _canvas.SetPreviewActive(false);
        UpdatePreviewVisuals();
        UpdateTimeUi();
    }

    private void SetPlaying(bool playing)
    {
        if (!playing)
        {
            if(IsPreviewing)
                _previewSession.SetPlaying(false);
            
            return;
        }

        EnsurePreviewSession();

        if (!IsPreviewing)
        {
            _playToggle.SetValueWithoutNotify(false);
            return;
        }

        if (_displayTime >= _previewSession.Duration - 0.0001f)
            _displayTime = 0f;

        _previewSession.Seek(_displayTime);
        _previewSession.SetPlaying(true);
        _lastTickTime = EditorApplication.timeSinceStartup;
    }

    private void StopPreview()
    {
        StopRecording(commit: false);
        _playToggle.SetValueWithoutNotify(false);
        DisposePreviewSession();
        _displayTime = 0f;
        _canvas.SetTime(_displayTime);
        UpdateTimeUi();
    }

    private void OnSeekRequested(float time)
    {
        SeekDisplayTime(time);
    }

    private void SeekDisplayTime(float time)
    {
        _displayTime = Mathf.Max(0f, time);
        EnsurePreviewSession();
        _previewSession.Seek(_displayTime);
        _playToggle.SetValueWithoutNotify(false);
        _previewSession.SetPlaying(false);
        _canvas.SetTime(_displayTime);
        UpdateTimeUi();
    }

    private void OnEditorUpdate()
    {
        if (!_playToggle.value || !IsPreviewing)
            return;

        double now = EditorApplication.timeSinceStartup;
        float dt = Mathf.Min((float)(now - _lastTickTime), 0.1f);
        _lastTickTime = now;

        _previewSession.Tick(dt);
        _displayTime = _previewSession.Time;
        _canvas.SetTime(_displayTime);
        UpdateTimeUi();

        if (_displayTime >= _previewSession.Duration - 0.0001f)
        {
            _previewSession.SetPlaying(false);
            _playToggle.SetValueWithoutNotify(false);
        }
    }

    private void UpdateTimeUi()
    {
        float duration = IsPreviewing
            ? Mathf.Max(_previewSession.Duration, 0.01f)
            : Mathf.Max(_rootPlan?.Timing.AbsoluteDuration ?? 0f, 0.01f);

        _displayTime = Mathf.Clamp(_displayTime, 0f, duration);
    }

    private void UpdatePreviewVisuals()
    {
        bool previewing = IsPreviewing;
        bool recording = IsRecording;

        if (_previewIndicator != null)
            _previewIndicator.style.display = (previewing && !recording) ? DisplayStyle.Flex : DisplayStyle.None;

        if (_recordingIndicator != null)
            _recordingIndicator.style.display = recording ? DisplayStyle.Flex : DisplayStyle.None;

        if (_canvas != null)
            _canvas.SetPreviewActive(previewing);

        if (_canvasBorderOverlay != null)
        {
            Color borderColor = recording
                ? new Color(0.8f, 0.2f, 0.2f, 1f)
                : previewing
                    ? new Color(0.173f, 0.471f, 0.922f, 1.000f)
                    : Color.clear;
            float borderWidth = (previewing || recording) ? 1.5f : 0f;

            _canvasBorderOverlay.style.borderTopColor = borderColor;
            _canvasBorderOverlay.style.borderBottomColor = borderColor;
            _canvasBorderOverlay.style.borderLeftColor = borderColor;
            _canvasBorderOverlay.style.borderRightColor = borderColor;
            _canvasBorderOverlay.style.borderTopWidth = borderWidth;
            _canvasBorderOverlay.style.borderBottomWidth = borderWidth;
            _canvasBorderOverlay.style.borderLeftWidth = borderWidth;
            _canvasBorderOverlay.style.borderRightWidth = borderWidth;
        }
    }

    private void SetDiagnostic(string message, HelpBoxMessageType type, bool show)
    {
        _diagnostic.messageType = type;
        _diagnostic.text = message;
        _diagnostic.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // ======================================================================
    //  Recording. While armed, a UserEditTracker watches Undo-recorded scene
    //  edits (inspector tweaks, gizmo drags, …) and resolves them to
    //  BindableProperties. Existing segments are updated via their matching
    //  SegmentRecorder; new segments are created via the highest-priority
    //  SegmentRecorder that claims the property. Only the active layer
    //  (direct children of the opened SequenceSegment) is touched.
    // ======================================================================

    private const float DefaultRecordDuration = 1f;
    private const float NearZeroRecordingEnd = 0.1f;

    private void StartRecording()
    {
        if (!IsPreviewing)
            EnsurePreviewSession();

        if (IsRecording)
            return;

        if (_provider == null || !IsPreviewing)
        {
            _recordToggle?.SetValueWithoutNotify(false);
            return;
        }

        _recordableProperties = new List<BindableProperty>();
        BindablePropertyUtility.GetBindableProperties(_provider.gameObject, _recordableProperties, recursive: true);

        _recordSnapshotValues.Clear();
        _recordCollection?.Dispose();
        _recordCollection = PropertyBindingCollection.Bind(_provider.gameObject, _recordableProperties);
        for (int i = 0; i < _recordableProperties.Count; i++)
        {
            var property = _recordableProperties[i];
            if (_recordCollection.TryRead(property, out var value))
                _recordSnapshotValues[property] = value;
        }

        _recordedEdits.Clear();
        _editTracker = new UserEditTracker(filterOut: bp =>
            bp.Target is SequenceProvider ||
            !_recordSnapshotValues.ContainsKey(bp));
        _editTracker.StartDetecting(OnRecordedUserEdit);
        UpdateRecordLabel();
        UpdatePreviewVisuals();
    }

    private void StopRecording(bool commit)
    {
        if (!IsRecording)
            return;

        _editTracker.StopDetecting();
        _editTracker = null;

        if (!commit)
            RestoreRecordingSnapshot();
        else
            Undo.RecordObject(_provider, "Apply Recorded Edits");

        _recordCollection?.Dispose();
        _recordCollection = null;
        _recordableProperties = null;
        _recordSnapshotValues.Clear();
        _recordedEdits.Clear();

        if (_recordToggle != null)
        {
            _recordToggle.SetValueWithoutNotify(false);
            UpdateRecordLabel();
        }

        UpdatePreviewVisuals();
    }

    private void OnRecordedUserEdit(EditType editType, BindablePropertyValueEdit edit)
    {
        if (_provider == null || !IsPreviewing)
            return;

        switch (editType)
        {
            case EditType.Added:
            case EditType.Modified:
                OnRecordedEdit(edit);
                break;

            case EditType.Removed:
                if (_recordedEdits.TryGetValue(edit.BindableProperty, out var removed))
                {
                    _recordedEdits.Remove(edit.BindableProperty);
                    if (removed.Created)
                    {
                        if (removed.Segment is IStartTimeConfigurable)
                        {
                            if (_activeEditRoot?.Segment is Sequence activeSeq)
                                activeSeq.Segments.Remove(removed.Segment);
                        }
                    }
                    else
                    {
                        if (removed.OriginalStateJson != null)
                            JsonUtility.FromJsonOverwrite(removed.OriginalStateJson, removed.Segment);
                    }
                    CommitAuthoringChange();
                }
                break;
        }

        UpdateRecordLabel();
    }

    private void OnRecordedEdit(BindablePropertyValueEdit edit)
    {
        float cursor = _displayTime;
        if (cursor <= NearZeroRecordingEnd + 0.02f)
        {
            cursor = NearZeroRecordingEnd;
            SeekDisplayTime(cursor);
        }

        // Active layer only: check existing segments for consumption
        Segment consumingPlan = null;
        SegmentRecorder consumingRecorder = null;

        foreach (var childPlan in _activeEditRoot.Children)
        {
            var recorder = SegmentRecorderRegistry.GetRecorderFor(childPlan.Segment.GetType());
            if (recorder != null && recorder.CanConsume(childPlan.Segment, edit.BindableProperty, cursor))
            {
                consumingPlan = childPlan.Segment;
                consumingRecorder = recorder;
                break;
            }
        }

        if (consumingPlan != null && consumingRecorder != null)
        {
            bool alreadyRecorded =
                _recordedEdits.TryGetValue(edit.BindableProperty, out var prior) &&
                ReferenceEquals(prior.Segment, consumingPlan);
            if (!alreadyRecorded)
            {
                _recordedEdits[edit.BindableProperty] = new RecordedEdit
                {
                    Segment = consumingPlan,
                    Created = false,
                    OriginalStateJson = JsonUtility.ToJson(consumingPlan)
                };
            }
            consumingRecorder.Consume(consumingPlan, edit.BindableProperty, edit.LatestValue, cursor);
            CommitAuthoringChange();

            // Re-find the plan that contains this segment for selection
            var selectedPlan = _activeEditRoot.Children.Find(p => ReferenceEquals(p.Segment, consumingPlan));
            if (selectedPlan != null)
                OnSegmentSelected(selectedPlan);
            return;
        }

        // Restore pristine value before creating a new segment
        if (_recordSnapshotValues.TryGetValue(edit.BindableProperty, out var pristine))
            _recordCollection.TryWrite(edit.BindableProperty, pristine);

        // Find the highest-priority creator recorder
        SegmentRecorder creatorRecorder = null;
        foreach (var recorder in SegmentRecorderRegistry.GetAllRecorders())
        {
            if (recorder.CanCreateFor(edit.BindableProperty))
            {
                creatorRecorder = recorder;
                break;
            }
        }

        if (creatorRecorder == null)
            return;

        Segment newSegment;
        try
        {
            newSegment = creatorRecorder.CreateSegment(edit.BindableProperty, edit.LatestValue, cursor);
        }
        catch (Exception e)
        {
            Debug.LogError($"Could not create segment via recorder {creatorRecorder.GetType().Name}: {e.Message}");
            return;
        }

        if (newSegment == null)
            return;

        Undo.RecordObject(_provider, "Record Segment Edit");
        if (_activeEditRoot?.Segment is Sequence activeSequence)
        {
            activeSequence.Segments.Add(newSegment);
        }
        else
        {
            Debug.LogError("Active edit root is not a SequenceSegment; cannot add new segment.");
            return;
        }

        _recordedEdits[edit.BindableProperty] = new RecordedEdit { Segment = newSegment, Created = true };
        CommitAuthoringChange();

        var newPlan = _activeEditRoot.Children.Find(p => ReferenceEquals(p.Segment, newSegment));
        if (newPlan != null)
            OnSegmentSelected(newPlan);
    }

    private void RestoreRecordingSnapshot()
    {
        if (_recordCollection == null || _recordSnapshotValues.Count == 0)
            return;

        using var writer = _recordCollection.StartBulkWriteScope();
        foreach (var pair in _recordSnapshotValues)
            writer.TryWrite(pair.Key, pair.Value);
    }

    private void UpdateRecordLabel()
    {
        if (_recordToggle != null)
            _recordToggle.text = "● Rec";
    }

    private sealed class SegmentPreviewSession : IDisposable
    {
        public SequenceProvider Provider { get; }
        public SequenceInstance Instance { get; private set; }
        public float Time { get; private set; }
        public float Duration => Instance != null ? Instance.Duration : 0f;
        public bool IsDisposed { get; private set; }

        public event Action Rebuilt;
        public event Action Disposed;

        private SegmentPreviewSession(SequenceProvider provider)
        {
            Provider = provider;
        }

        public static SegmentPreviewSession Acquire(SequenceProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            var session = new SegmentPreviewSession(provider);
            session.Rebuild();
            return session;
        }

        public void Rebuild()
        {
            ThrowIfDisposed();

            float preservedTime = Time;
            DisposeInstance();

            if (Provider == null)
                return;

            Instance = Provider.CreateInstance(isPreview: true);
            Time = Mathf.Clamp(preservedTime, 0f, Duration);
            Instance.Scrub(Time);
            Rebuilt?.Invoke();
            SceneView.RepaintAll();
        }

        public void Seek(float time)
        {
            ThrowIfDisposed();
            if (Instance == null)
                return;

            Time = Mathf.Clamp(time, 0f, Duration);
            Instance.Scrub(Time);
            SceneView.RepaintAll();
        }

        public void SetPlaying(bool playing)
        {
            ThrowIfDisposed();
            if (Instance == null)
                return;

            if (playing)
                Instance.Resume();
            else
                Instance.Pause();
        }

        public void Tick(float dt)
        {
            ThrowIfDisposed();
            if (Instance == null || Instance.IsPaused || Instance.IsStopped)
                return;

            Instance.Tick(dt);
            Time = Mathf.Clamp(Instance.Playhead, 0f, Duration);
            SceneView.RepaintAll();
        }

        public void Dispose()
        {
            if (IsDisposed)
                return;

            IsDisposed = true;
            DisposeInstance();
            Disposed?.Invoke();
            SceneView.RepaintAll();
        }

        private void DisposeInstance()
        {
            Instance?.Dispose();
            Instance = null;
        }

        private void ThrowIfDisposed()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(SegmentPreviewSession));
        }
    }

    private static class SegmentLocator
    {
        public static string FindPath(Sequence sequence, Segment target, string basePath)
        {
            if (sequence == null || target == null)
                return null;

            var list = sequence.Segments;
            if (list == null)
                return null;

            for (int i = 0; i < list.Count; i++)
            {
                var candidate = list[i];
                string path = $"{basePath}.Segments.Array.data[{i}]";
                if (ReferenceEquals(candidate, target))
                    return path;

                if (candidate is Sequence nested)
                {
                    var found = FindPath(nested, target, path);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }
    }
}
