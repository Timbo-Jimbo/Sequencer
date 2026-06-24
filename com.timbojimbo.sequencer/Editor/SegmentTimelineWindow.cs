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
        public struct ClipboardEntry
        {
            public string TypeName;
            public string Json;
            public float StartTime;
            public float EndTime;
        }

        private static readonly List<ClipboardEntry> _clipboard = new();

        private TimelineSessionState _sessionState;
        private Label _providerLabel;
        private SegmentTimelineCanvas _canvas;
        private ToolbarToggle _playToggle;
        private Label _previewIndicator;
        private VisualElement _canvasBorderOverlay;

        private SerializedObject _serializedProvider;
        private bool _isSyncingSelection;

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
        private bool IsRecording => _editTracker != null;
        private SequenceProvider Provider => _sessionState?.Provider;

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

        internal static void NotifyProviderChanged(SequenceProvider provider)
        {
            if (provider == null)
                return;

            var windows = Resources.FindObjectsOfTypeAll<SegmentTimelineWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                var window = windows[i];
                if (window == null || !ReferenceEquals(window.Provider, provider))
                    continue;

                window.RefreshPlan();
            }
        }

        private void OnEnable()
        {
            _sessionState = new TimelineSessionState();
            _sessionState.SessionRefreshed += OnSessionRefreshed;

            BuildUi();

            Selection.selectionChanged += OnSelectionChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.update += OnEditorUpdate;
            
            EditorSceneManager.sceneSaving += OnSceneSaving;
            PrefabStage.prefabSaving += OnPrefabSaving;

            OnSelectionChanged();
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
            
            _sessionState.SessionRefreshed -= OnSessionRefreshed;
            _sessionState.Dispose();
            _sessionState = null;

            _serializedProvider?.Dispose();
            _serializedProvider = null;
        }

        private void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path)
        {
            if (IsRecording) StopRecording(commit: true);
            if (IsPreviewing) StopPreview();
        }

        private void OnPrefabSaving(GameObject prefab)
        {
            if (IsRecording) StopRecording(commit: true);
            if (IsPreviewing) StopPreview();
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

            _canvas = new SegmentTimelineCanvas();
            _canvas.SelectionChanged += OnCanvasSelectionChanged;
            _canvas.TimeAdjustmentCommitted += OnTimeAdjustmentCommitted;
            _canvas.DeleteRequested += OnDeleteRequested;
            _canvas.AddRequested += OnAddRequested;
            _canvas.SeekRequested += OnSeekRequested;
            _canvas.CopyRequested += OnCopyRequested;
            _canvas.PasteRequested += OnPasteRequested;
            rootVisualElement.Add(_canvas);

            _canvasBorderOverlay = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    top = 0, left = 0, right = 0, bottom = 0,
                    borderTopWidth = 0, borderBottomWidth = 0, borderLeftWidth = 0, borderRightWidth = 0,
                    borderTopColor = Color.clear, borderBottomColor = Color.clear, borderLeftColor = Color.clear, borderRightColor = Color.clear,
                },
                pickingMode = PickingMode.Ignore,
            };
            _canvas.Add(_canvasBorderOverlay);
            UpdatePreviewVisuals();
        }

        private void OnSelectionChanged()
        {
            if (_isSyncingSelection)
                return;

            var selectedModels = Selection.objects.OfType<SegmentSelectionModel>().ToList();
            var modelProvider = selectedModels.FirstOrDefault(m => m != null && m.Handle.Provider != null)?.Handle.Provider;

            var selectedGo = Selection.activeGameObject;
            var selectedGoProvider = selectedGo != null ? selectedGo.GetComponentInParent<SequenceProvider>() : null;

            var provider = selectedGoProvider ?? modelProvider;

            if (Provider == null || (provider != null && !ReferenceEquals(Provider, provider)))
                SetProvider(provider);

            SyncCanvasSelection();
        }

        private void OnUndoRedo()
        {
            if (Provider == null)
                return;

            _sessionState.Refresh();
        }

        private void SetProvider(SequenceProvider provider)
        {
            if (ReferenceEquals(Provider, provider) && Provider != null)
                return;

            StopRecording(commit: false);
            DisposePreviewSession();

            _sessionState.Bind(provider);
            _providerLabel.text = Provider != null ? $"{Provider.gameObject.name}" : "No Selected Provider";

            _serializedProvider?.Dispose();
            _serializedProvider = Provider != null ? new SerializedObject(Provider) : null;

            _displayTime = 0f;
            _playToggle?.SetValueWithoutNotify(false);
            
            RefreshPlan();
            UpdatePreviewVisuals();
        }

        private void RefreshPlan()
        {
            if (Provider == null)
            {
                _canvas.SetView(null, null);
                _canvas.SetPreviewActive(false);
                _canvas.SetTime(_displayTime);
                UpdatePreviewVisuals();
                return;
            }

            _sessionState.Refresh();
        }

        private void OnSessionRefreshed()
        {
            if (Provider == null)
            {
                _canvas.SetView(null, null);
                _canvas.SetPreviewActive(false);
                _canvas.SetTime(_displayTime);
                return;
            }

            // Keep selection perfectly in sync
            var currentSelectedModels = Selection.objects.OfType<SegmentSelectionModel>()
                .Where(m => ReferenceEquals(m.Handle.Provider, Provider))
                .ToList();

            _canvas.SetView(_sessionState.Models, currentSelectedModels);

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
        }

        private void SyncCanvasSelection()
        {
            if (Provider == null || _canvas == null)
                return;

            var activeSelected = Selection.objects.OfType<SegmentSelectionModel>()
                .Where(m => ReferenceEquals(m.Handle.Provider, Provider))
                .ToList();

            _canvas.SetSelection(activeSelected);
        }

        private void OnCanvasSelectionChanged(IReadOnlyList<SegmentSelectionModel> selected)
        {
            if (_isSyncingSelection)
                return;

            _isSyncingSelection = true;
            try
            {
                Selection.objects = selected.Cast<UnityEngine.Object>().ToArray();
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }

        private void OnTimeAdjustmentCommitted(SegmentSelectionModel model, float newStart, float newDuration)
        {
            if (model == null)
                return;

            model.StartTime = newStart;
            model.Duration = newDuration;
            model.CommitToProvider();
        }

        private void OnDeleteRequested(IReadOnlyList<SegmentSelectionModel> selectedModels)
        {
            _sessionState.DeleteSegments(selectedModels);
        }

        private void OnAddRequested(Type type, float time)
        {
            _sessionState.AddSegment(type, time);
        }

        private void OnCopyRequested()
        {
            var selected = _canvas.SelectedModels;
            if (selected.Count == 0)
                return;

            _clipboard.Clear();
            for (int i = 0; i < selected.Count; i++)
            {
                var model = selected[i];
                _clipboard.Add(new ClipboardEntry
                {
                    TypeName = model.Segment.GetType().AssemblyQualifiedName,
                    Json = JsonUtility.ToJson(model.Segment),
                    StartTime = model.StartTime,
                    EndTime = model.EndTime,
                });
            }
        }

        private void OnPasteRequested()
        {
            var pasted = _sessionState.TryPaste(_clipboard, _displayTime, IsPreviewing);
            if (pasted == null || pasted.Count == 0)
                return;

            // Re-select newly pasted models
            var pastedModels = _sessionState.Models
                .Where(m => pasted.Contains(m.Segment))
                .ToList();

            if (pastedModels.Count > 0)
            {
                Selection.objects = pastedModels.Cast<UnityEngine.Object>().ToArray();
                SyncCanvasSelection();
            }
        }

        private void EnsurePreviewSession()
        {
            if (Provider == null || IsPreviewing)
                return;

            _previewSession = SegmentPreviewSession.Acquire(Provider);
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
            Repaint();
        }

        private void OnSessionDisposed()
        {
            _previewSession = null;
            _playToggle.SetValueWithoutNotify(false);
            _canvas.SetPreviewActive(false);
            UpdatePreviewVisuals();
        }

        private void SetPlaying(bool playing)
        {
            if (!playing)
            {
                if (IsPreviewing)
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

            if (_displayTime >= _previewSession.Duration - 0.0001f)
            {
                _previewSession.SetPlaying(false);
                _playToggle.SetValueWithoutNotify(false);
            }
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

        private const float DefaultRecordDuration = 1f;
        private const float NearZeroRecordingEnd = 0.1f;

        private void StartRecording()
        {
            if (!IsPreviewing) EnsurePreviewSession();

            if (IsRecording) return;

            if (Provider == null || !IsPreviewing)
            {
                _recordToggle?.SetValueWithoutNotify(false);
                return;
            }

            _recordableProperties = new List<BindableProperty>();
            BindablePropertyUtility.GetBindableProperties(Provider.gameObject, _recordableProperties, recursive: true);

            _recordSnapshotValues.Clear();
            _recordCollection?.Dispose();
            _recordCollection = PropertyBindingCollection.Bind(Provider.gameObject, _recordableProperties);
            for (int i = 0; i < _recordableProperties.Count; i++)
            {
                var property = _recordableProperties[i];
                if (_recordCollection.TryRead(property, out var value))
                    _recordSnapshotValues[property] = value;
            }

            _recordedEdits.Clear();
            _editTracker = new UserEditTracker(filterOut: bp =>
                bp.Target is SequenceProvider || !_recordSnapshotValues.ContainsKey(bp));
            _editTracker.StartDetecting(OnRecordedUserEdit);
            UpdateRecordLabel();
            UpdatePreviewVisuals();
        }

        private void StopRecording(bool commit)
        {
            if (!IsRecording) return;

            _editTracker.StopDetecting();
            _editTracker = null;

            if (!commit)
                RestoreRecordingSnapshot();
            else
                Undo.RecordObject(Provider, "Apply Recorded Edits");

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
            if (Provider == null || !IsPreviewing)
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
                                Provider.Sequence.Segments.Remove(removed.Segment);
                            }
                        }
                        else
                        {
                            if (removed.OriginalStateJson != null)
                                JsonUtility.FromJsonOverwrite(removed.OriginalStateJson, removed.Segment);
                        }
                        _sessionState.Refresh();
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

            Segment consumingSegment = null;
            SegmentRecorder consumingRecorder = null;

            foreach (var model in _sessionState.Models)
            {
                var recorder = SegmentRecorderRegistry.GetRecorderFor(model.Segment.GetType());
                if (recorder != null && recorder.CanConsume(model.Segment, edit.BindableProperty, cursor))
                {
                    consumingSegment = model.Segment;
                    consumingRecorder = recorder;
                    break;
                }
            }

            if (consumingSegment != null && consumingRecorder != null)
            {
                bool alreadyRecorded =
                    _recordedEdits.TryGetValue(edit.BindableProperty, out var prior) &&
                    ReferenceEquals(prior.Segment, consumingSegment);
                if (!alreadyRecorded)
                {
                    _recordedEdits[edit.BindableProperty] = new RecordedEdit
                    {
                        Segment = consumingSegment,
                        Created = false,
                        OriginalStateJson = JsonUtility.ToJson(consumingSegment)
                    };
                }
                consumingRecorder.Consume(consumingSegment, edit.BindableProperty, edit.LatestValue, cursor);
                
                // Save model changes back to provider
                var associatedModel = _sessionState.Models.Find(m => ReferenceEquals(m.Segment, consumingSegment));
                if (associatedModel != null)
                {
                    associatedModel.CommitToProvider();
                    Selection.objects = new[] { associatedModel };
                    SyncCanvasSelection();
                }
                return;
            }

            if (_recordSnapshotValues.TryGetValue(edit.BindableProperty, out var pristine))
                _recordCollection.TryWrite(edit.BindableProperty, pristine);

            SegmentRecorder creatorRecorder = null;
            foreach (var recorder in SegmentRecorderRegistry.GetAllRecorders())
            {
                if (recorder.CanCreateFor(edit.BindableProperty))
                {
                    creatorRecorder = recorder;
                    break;
                }
            }

            if (creatorRecorder == null) return;

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

            if (newSegment == null) return;

            Undo.RecordObject(Provider, "Record Segment Edit");
            Provider.Sequence.Segments.Add(newSegment);

            _recordedEdits[edit.BindableProperty] = new RecordedEdit { Segment = newSegment, Created = true };
            _sessionState.Refresh();

            var newModel = _sessionState.Models.Find(m => ReferenceEquals(m.Segment, newSegment));
            if (newModel != null)
            {
                Selection.objects = new[] { newModel };
                SyncCanvasSelection();
            }
        }

        private void RestoreRecordingSnapshot()
        {
            if (_recordCollection == null || _recordSnapshotValues.Count == 0) return;

            using (_recordCollection.BulkWriteScope())
            {
                foreach (var pair in _recordSnapshotValues)
                    _recordCollection.TryWrite(pair.Key, pair.Value);
            }
        }

        private void UpdateRecordLabel()
        {
            if (_recordToggle != null) _recordToggle.text = "● Rec";
        }
    }
}