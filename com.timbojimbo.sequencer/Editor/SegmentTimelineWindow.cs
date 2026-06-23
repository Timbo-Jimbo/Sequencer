using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using TimboJimbo.PropertyBindings;
using TimboJimboEditor.PropertyBindings.Utility;
using UnityEditor.SceneManagement;
using TimboJimbo.Sequencer;
using TimboJimbo.Sequencer.Segments;

namespace TimboJimboEditor.Sequencer
{
    public sealed class SegmentTimelineWindow : EditorWindow
    {
        private SequenceProvider _provider;
        private SegmentPlan _rootPlan;
        private SegmentPlan _activeEditRoot;

        private Label _providerLabel;
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
        private bool IsPreviewing => _previewSession != null;

        private struct ClipboardEntry
        {
            public string TypeName;
            public string Json;
            public float StartTime;
            public float EndTime;
        }

        private static readonly List<ClipboardEntry> _clipboard = new();
        private bool IsRecording => _editTracker != null;
        private IReadOnlyList<SegmentPlan> SelectedSegments => _canvas?.SelectedPlans ?? Array.Empty<SegmentPlan>();

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

            var split = new TwoPaneSplitView(0, 700, TwoPaneSplitViewOrientation.Horizontal)
            {
                style = { flexGrow = 1f }
            };

            var leftPane = new VisualElement { style = { flexGrow = 1f } };
            _canvas = new SegmentTimelineCanvas();
            _canvas.SelectionChanged += OnCanvasSelectionChanged;
            _canvas.TimeAdjustmentCommitted += OnTimeAdjustmentCommitted;
            _canvas.DeleteRequested += OnDeleteRequested;
            _canvas.AddRequested += OnAddRequested;
            _canvas.SeekRequested += OnSeekRequested;
            _canvas.CopyRequested += OnCopyRequested;
            _canvas.PasteRequested += OnPasteRequested;
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
                _canvas.SetView(null, null, null);
                _canvas.SetPreviewActive(false);
                _canvas.SetTime(_displayTime);
                RebuildInspector();
                UpdateTimeUi();
                return;
            }

            _rootPlan = _provider.GetPlan();

            if (_rootPlan?.Segment is not Sequence)
            {
                _activeEditRoot = null;
                _canvas.SetView(null, null, null);
                _canvas.SetPreviewActive(false);
                _canvas.SetTime(_displayTime);
                RebuildInspector();
                UpdateTimeUi();
                return;
            }

            _activeEditRoot = _rootPlan;

            var activeLayer = _activeEditRoot.Children;
            List<SegmentPlan> selectedPlans = null;

            if (preserveSelection)
            {
                var currentSelection = SelectedSegments;
                if (currentSelection.Count > 0)
                {
                    selectedPlans = new List<SegmentPlan>(currentSelection.Count);
                    for (int i = 0; i < currentSelection.Count; i++)
                    {
                        var selectedSegment = currentSelection[i]?.Segment;
                        if (selectedSegment == null)
                            continue;

                        for (int j = 0; j < activeLayer.Count; j++)
                        {
                            if (ReferenceEquals(activeLayer[j].Segment, selectedSegment))
                            {
                                selectedPlans.Add(activeLayer[j]);
                                break;
                            }
                        }
                    }

                    if (selectedPlans.Count == 0 && activeLayer.Count > 0)
                        selectedPlans.Add(activeLayer[0]);
                }
            }
            else if (activeLayer.Count > 0)
            {
                selectedPlans = new List<SegmentPlan>(1) { activeLayer[0] };
            }

            _canvas.SetView(_activeEditRoot, activeLayer, selectedPlans);

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

            if(!preserveSelection)
                RebuildInspector();
        }

        private void OnCanvasSelectionChanged(IReadOnlyList<SegmentPlan> selected)
        {
            RebuildInspector();
        }

        private void SelectSingle(SegmentPlan plan)
        {
            _canvas?.SetSelection(plan != null
                ? new[] { plan }
                : Array.Empty<SegmentPlan>());
            RebuildInspector();
        }


        private void RebuildInspector()
        {
            _inspectorHost.Clear();

            if (_provider == null)
            {
                _inspectorHost.Add(new Label("No Selected Provider")
                {
                    style =
                    {
                        unityTextAlign = TextAnchor.MiddleCenter,
                        unityFontStyleAndWeight = FontStyle.Italic,
                        color = new Color(0.5f, 0.5f, 0.5f, 1f),
                        marginTop = 20,
                        marginLeft = 4,
                        marginRight = 4,
                        marginBottom = 20,
                    }
                });
                
                return;
            }

            var selectedSegments = SelectedSegments;

            if (selectedSegments.Count == 0)
            {
                _inspectorHost.Add(new HelpBox("Select a segment block to inspect.", HelpBoxMessageType.Info));
                return;
            }

            var segmentPropertiesByType = new Dictionary<Type, List<SerializedProperty>>();
            _serializedProvider.Update();

            foreach(var selected in selectedSegments)
            {
                string selectedPath = SegmentLocator.FindPath(_provider.Sequence, selected.Segment, "Sequence");

                if (selectedPath == null)
                    continue;

                var list = segmentPropertiesByType.TryGetValue(selected.Segment.GetType(), out var existingList)
                    ? existingList
                    : (segmentPropertiesByType[selected.Segment.GetType()] = new List<SerializedProperty>());

                list.Add(_serializedProvider.FindProperty(selectedPath));
            }

            foreach (var entry in segmentPropertiesByType)
            {
                var editor = SegmentEditorRegistry.GetEditorByType(entry.Key);

                var entryContainer = new VisualElement
                {
                    style =
                    {
                        borderBottomWidth = 1,
                        borderBottomColor = new Color(0f, 0f, 0f, 0.4f),
                    }
                };
                _inspectorHost.Add(entryContainer);

                var toolbar = new Toolbar { style = { paddingLeft = 4, paddingRight = 4, backgroundColor = new Color(1f, 1f, 1f, 0.05f) } };
                entryContainer.Add(toolbar);

                var titleText = ObjectNames.NicifyVariableName(entry.Key.Name);
                if (entry.Value.Count > 1)
                    titleText += $" ({entry.Value.Count})";

                var title = new Label(titleText)
                {
                    style =
                    {
                        unityFontStyleAndWeight = FontStyle.Bold,
                        unityTextAlign = TextAnchor.MiddleLeft,
                    }
                };
                toolbar.Add(title);

                var body = new VisualElement()
                {
                    style =
                    {
                        paddingTop = 8,
                        paddingLeft = 4,
                        paddingRight = 4,
                        paddingBottom = 8,
                    }
                };
                entryContainer.Add(body);

                var inspector = new IMGUIContainer(() =>
                {
                    _serializedProvider.Update();

                    if(entry.Value.Count == 1)
                        editor.OnInspectorGUI(entry.Value[0]);
                    else
                        editor.OnInspectorGUI(entry.Value);

                    if (_serializedProvider.ApplyModifiedProperties())
                    {
                        EditorUtility.SetDirty(_provider);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(_provider);
                        RefreshPlan(preserveSelection: true);
                    }
                });
                body.Add(inspector);


            }
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

        private void OnDeleteRequested(IReadOnlyList<SegmentPlan> plans)
        {
            if (_provider == null || _activeEditRoot?.Segment is not Sequence root || plans == null || plans.Count == 0)
                return;

            var toDelete = new List<Segment>(plans.Count);
            for (int i = 0; i < plans.Count; i++)
            {
                var segment = plans[i]?.Segment;
                if (segment != null && !toDelete.Contains(segment))
                    toDelete.Add(segment);
            }

            if (toDelete.Count == 0)
                return;

            Undo.RecordObject(_provider, "Delete Segment");
            var removedAny = false;
            for (int i = root.Segments.Count - 1; i >= 0; i--)
            {
                if (toDelete.Contains(root.Segments[i]))
                {
                    root.Segments.RemoveAt(i);
                    removedAny = true;
                }
            }

            if (removedAny)
            {
                _canvas?.SetSelection(Array.Empty<SegmentPlan>());
                RebuildInspector();
                CommitAuthoringChange();
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

        private void OnCopyRequested()
        {
            var selected = SelectedSegments;
            if (selected.Count == 0)
                return;

            _clipboard.Clear();
            for (int i = 0; i < selected.Count; i++)
            {
                var plan = selected[i];
                _clipboard.Add(new ClipboardEntry
                {
                    TypeName = plan.Segment.GetType().AssemblyQualifiedName,
                    Json = JsonUtility.ToJson(plan.Segment),
                    StartTime = plan.Timing.AbsoluteStartTime,
                    EndTime = plan.Timing.AbsoluteEndTime,
                });
            }
        }

        private void OnPasteRequested()
        {
            if (_provider == null || _activeEditRoot?.Segment is not Sequence root || _clipboard.Count == 0)
                return;

            float earliestStart = float.MaxValue;
            float latestEnd = float.MinValue;
            for (int i = 0; i < _clipboard.Count; i++)
            {
                earliestStart = Mathf.Min(earliestStart, _clipboard[i].StartTime);
                latestEnd = Mathf.Max(latestEnd, _clipboard[i].EndTime);
            }
            float clipboardDuration = latestEnd - earliestStart;
            float pasteOrigin = IsPreviewing
                ? _displayTime - clipboardDuration
                : earliestStart;

            Undo.RecordObject(_provider, "Paste Segments");
            var pastedSegments = new List<Segment>(_clipboard.Count);

            for (int i = 0; i < _clipboard.Count; i++)
            {
                var entry = _clipboard[i];
                var type = Type.GetType(entry.TypeName);
                if (type == null)
                    continue;

                Segment segment;
                try { segment = (Segment)JsonUtility.FromJson(entry.Json, type); }
                catch { continue; }

                float newStart = pasteOrigin + (entry.StartTime - earliestStart);
                if (segment is IStartTimeConfigurable timeConfig)
                    timeConfig.SetStartTime(Mathf.Max(0f, newStart));

                root.Segments.Add(segment);
                pastedSegments.Add(segment);
            }

            if (pastedSegments.Count == 0)
                return;

            CommitAuthoringChange();

            if (_activeEditRoot == null)
                return;

            var newPlans = _activeEditRoot.Children
                .Where(p => pastedSegments.Contains(p.Segment))
                .ToList();
            if (newPlans.Count > 0)
            {
                _canvas?.SetSelection(newPlans);
                RebuildInspector();
            }
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
                    SelectSingle(selectedPlan);
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
                newSegment = creatorRecorder.CreateSegment(edit.BindableProperty, edit.InitialValue, edit.LatestValue, cursor);
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
                SelectSingle(newPlan);
        }

        private void RestoreRecordingSnapshot()
        {
            if (_recordCollection == null || _recordSnapshotValues.Count == 0)
                return;

            using(_recordCollection.BulkWriteScope())
            {
                foreach (var pair in _recordSnapshotValues)
                    _recordCollection.TryWrite(pair.Key, pair.Value);
            }
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
}