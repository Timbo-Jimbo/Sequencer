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
using TimboJimboEditor.Sequencer.Recorders;

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
        private Image _providerIcon;
        private Label _providerLabel;

        private PopupField<string> _sequencePopup;
        private SegmentTimelineCanvas _canvas;
        private ToolbarToggle _playToggle;
        private Label _previewIndicator;
        private VisualElement _canvasBorderOverlay;
        private VisualElement _emptyStateContainer;
        private HelpBox _previewErrorBox;

        private SerializedObject _serializedProvider;
        private bool _isSyncingSelection;

        private SegmentPreviewSession _previewSession;
        private double _lastTickTime;

        private TimelineRangeState _rangeState;
        private FloatField _loopDelayField;
        private double _loopResumeAtTime = -1d;

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
        public SequenceProvider Provider => _sessionState?.Provider;
        public string SequenceName => _sessionState?.SequenceName;
        private Sequence ActiveSequence => _sessionState?.ActiveSequence;

        private float DisplayTime => _rangeState.Playhead;
        private float ActivePlaybackRangeStart => _rangeState.RangeStart;
        private float ActivePlaybackRangeEnd => _rangeState.RangeEnd;

        private const float MinPlaybackRangeDuration = 0.01f;

        [MenuItem("Window/Segment Timeline")]
        public static void OpenFromMenu() => Open(Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponentInParent<SequenceProvider>()
            : null);

        public static void Open(SequenceProvider provider)
        {
            Open(provider, null);
        }

        public static void Open(SequenceProvider provider, string sequenceName)
        {
            var window = GetWindow<SegmentTimelineWindow>("Segment Timeline");
            if (provider != null)
                window.SetProvider(provider, sequenceName, forceReinitialize: true);
        }

        internal static bool IsSequenceContextOpen(SequenceProvider provider, string sequenceName)
        {
            if (provider == null || string.IsNullOrWhiteSpace(sequenceName))
                return false;

            var windows = Resources.FindObjectsOfTypeAll<SegmentTimelineWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                var window = windows[i];
                if (window == null)
                    continue;

                if (!ReferenceEquals(window.Provider, provider))
                    continue;

                if (string.Equals(window.SequenceName, sequenceName, StringComparison.Ordinal))
                    return true;
            }

            return false;
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

            _rangeState = CreateInstance<TimelineRangeState>();
            _rangeState.hideFlags = HideFlags.HideAndDontSave;
            _rangeState.Initialize(1f);

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

            DestroyImmediate(_rangeState);
            _rangeState = null;

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

            var providerDetailsContainer = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.FlexStart,
                    marginLeft = 4f,
                }
            };
            toolbar.Add(providerDetailsContainer);

            _providerIcon = new Image
            {
                style =
                {
                    width = 14,
                    height = 14,
                    flexShrink = 0,
                    marginRight = 4,
                    display = DisplayStyle.None
                },
                pickingMode = PickingMode.Ignore,
            };
            providerDetailsContainer.Add(_providerIcon);

            _providerLabel = new Label("(no provider)")
            {
                style =
                {
                    unityTextAlign = TextAnchor.MiddleLeft,
                }
            };
            providerDetailsContainer.Add(_providerLabel);

            _sequencePopup = new PopupField<string>(new List<string> { "(none)" }, 0)
            {
                style = { minWidth = 140f, marginRight = 4f }
            };
            _sequencePopup.RegisterValueChangedCallback(evt =>
            {
                if (!string.Equals(evt.newValue, SequenceName, StringComparison.Ordinal))
                    SetProvider(Provider, evt.newValue, forceReinitialize: true);
            });
            providerDetailsContainer.Add(_sequencePopup);

            _playToggle = new ToolbarToggle { text = "Play" };
            _playToggle.RegisterValueChangedCallback(evt => SetPlaying(evt.newValue));
            toolbar.Add(_playToggle);

            toolbar.Add(new ToolbarButton(StopPreview) { text = "Stop" });

            toolbar.Add(new ToolbarSpacer());
            toolbar.Add(new Label("Loop Delay") { style = { marginLeft = 8f, unityTextAlign = TextAnchor.MiddleLeft } });
            _loopDelayField = new FloatField { isDelayed = true, value = _rangeState.LoopDelaySeconds };
            _loopDelayField.style.width = 72f;
            _loopDelayField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(_rangeState, "Set Loop Delay");
                _rangeState.SetLoopDelay(evt.newValue);
                _loopDelayField.SetValueWithoutNotify(_rangeState.LoopDelaySeconds);
                EditorUtility.SetDirty(_rangeState);
            });
            toolbar.Add(_loopDelayField);

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
            _canvas.ConvertRequested += OnConvertRequested;
            _canvas.AddRequested += OnAddRequested;
            _canvas.DropSegmentRequested += OnDropSegmentRequested;
            _canvas.SeekRequested += OnSeekRequested;
            _canvas.PlaybackRangeChanged += OnCanvasPlaybackRangeChanged;
            _canvas.PlaybackRangeResetRequested += OnCanvasPlaybackRangeResetRequested;
            _canvas.CopyRequested += OnCopyRequested;
            _canvas.PasteRequested += OnPasteRequested;
            _canvas.StackSelectionRequested += StackSelectedSegmentsEndToEnd;
            _canvas.AlignSelectionStartsRequested += AlignSelectedSegmentsByStart;
            _canvas.AlignSelectionEndsRequested += AlignSelectedSegmentsByEnd;
            _canvas.SetDropTargetContext(Provider, SequenceName);
            rootVisualElement.Add(_canvas);

            _emptyStateContainer = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = 16f,
                    right = 16f,
                    top = 56f,
                    display = DisplayStyle.None,
                    paddingLeft = 12f,
                    paddingRight = 12f,
                    paddingTop = 10f,
                    paddingBottom = 10f,
                    backgroundColor = new Color(0.18f, 0.18f, 0.18f, 0.95f),
                    borderTopWidth = 1f,
                    borderBottomWidth = 1f,
                    borderLeftWidth = 1f,
                    borderRightWidth = 1f,
                    borderTopColor = new Color(0.35f, 0.35f, 0.35f),
                    borderBottomColor = new Color(0.35f, 0.35f, 0.35f),
                    borderLeftColor = new Color(0.35f, 0.35f, 0.35f),
                    borderRightColor = new Color(0.35f, 0.35f, 0.35f),
                }
            };
            _emptyStateContainer.Add(new Label("No sequences found on this provider. Manage sequences in the provider inspector.")
            {
                style = { marginBottom = 8f, whiteSpace = WhiteSpace.Normal }
            });
            _emptyStateContainer.Add(new Button(FocusProviderInspector) { text = "Manage Sequences in Provider Inspector" });
            rootVisualElement.Add(_emptyStateContainer);

            _previewErrorBox = new HelpBox(string.Empty, HelpBoxMessageType.Error)
            {
                style = { position = Position.Absolute, left = 16f, right = 16f, top = 56f, display = DisplayStyle.None }
            };
            rootVisualElement.Add(_previewErrorBox);

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
            RefreshSequenceControls();
            UpdatePreviewVisuals();
        }

        private void OnSelectionChanged()
        {
            if (!_isSyncingSelection)
                SyncCanvasSelection();
        }

        private void OnUndoRedo()
        {
            if (Provider == null)
                return;

            SetProvider(Provider, SequenceName, forceReinitialize: false);
        }

        private void SetProvider(SequenceProvider provider, string sequenceName = null, bool forceReinitialize = false)
        {
            bool providerSame = ReferenceEquals(Provider, provider);
            string resolvedSequenceName = TimelineSessionState.ResolveValidSequenceName(provider, sequenceName ?? SequenceName);
            bool sequenceSame = string.Equals(SequenceName, resolvedSequenceName, StringComparison.Ordinal);
            bool contextChanged = !providerSame || !sequenceSame;

            if (!forceReinitialize && providerSame && sequenceSame && Provider != null)
            {
                RefreshPlan();
                UpdatePreviewVisuals();
                return;
            }

            StopRecording(commit: false);
            DisposePreviewSession();

            _sessionState.Bind(provider, resolvedSequenceName);
            _providerIcon.image = Provider != null ? EditorGUIUtility.ObjectContent(Provider.gameObject, typeof(GameObject)).image : null;
            _providerIcon.style.display = Provider != null ? DisplayStyle.Flex : DisplayStyle.None;
            _providerLabel.text = Provider != null ? $"{Provider.gameObject.name}" : "No Selected Provider";

            _serializedProvider?.Dispose();
            _serializedProvider = Provider != null ? new SerializedObject(Provider) : null;

            _loopResumeAtTime = -1d;
            _playToggle?.SetValueWithoutNotify(false);

            _rangeState.Initialize(GetPlaybackDurationLimit());
            RefreshSequenceControls();
            _canvas?.SetDropTargetContext(Provider, SequenceName);

            if (contextChanged)
                _canvas?.RequestReframeOnNextSetView();
            
            RefreshPlan();
            UpdatePreviewVisuals();
        }

        private void RefreshSequenceControls()
        {
            var names = Provider?.Sequences?
                .Where(sequence => sequence != null)
                .Select(sequence => sequence.Name)
                .ToList() ?? new List<string>();

            if (_sequencePopup != null)
            {
                if (names.Count == 0)
                    _sequencePopup.choices = new List<string> { "(none)" };
                else
                    _sequencePopup.choices = names;

                if (names.Count > 0)
                {
                    var selected = names.Contains(SequenceName) ? SequenceName : names[0];
                    _sequencePopup.SetValueWithoutNotify(selected);
                }
                else
                {
                    _sequencePopup.SetValueWithoutNotify("(none)");
                }

                _sequencePopup.SetEnabled(Provider != null && names.Count > 0);
            }

            if (_emptyStateContainer != null)
                _emptyStateContainer.style.display = Provider != null && (Provider.Sequences == null || Provider.Sequences.Count == 0)
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
        }

        private void FocusProviderInspector()
        {
            if (Provider == null)
                return;

            Selection.activeGameObject = Provider.gameObject;
            EditorGUIUtility.PingObject(Provider.gameObject);
        }

        public void RefreshPlan()
        {
            if (Provider == null || ActiveSequence == null)
            {
                _canvas.SetView(null, null);
                _canvas.SetPreviewActive(false);
                _canvas.SetTime(DisplayTime);
                RefreshSequenceControls();
                UpdatePreviewVisuals();
                return;
            }

            _sessionState.Refresh();
        }

        private void OnSessionRefreshed()
        {
            if (Provider == null || ActiveSequence == null)
            {
                _canvas.SetView(null, null);
                _canvas.SetPreviewActive(false);
                _canvas.SetTime(DisplayTime);
                RefreshSequenceControls();
                return;
            }

            // Keep selection perfectly in sync
            var currentSelectedModels = Selection.objects.OfType<SegmentSelectionModel>()
                .Where(m => ReferenceEquals(m.Handle.Provider, Provider)
                            && string.Equals(m.Handle.SequenceName, SequenceName, StringComparison.Ordinal))
                .ToList();

            _canvas.SetView(_sessionState.Models, currentSelectedModels);

            EnsurePlaybackRange();

            if (IsPreviewing)
            {
                _previewSession.SetPlaybackRange(_rangeState.GetPlaybackRange());
                _previewSession.Rebuild();
                _rangeState.SetPlayhead(DisplayTime, isPlaying: _playToggle.value);
                _previewSession.Seek(DisplayTime);
                if (_playToggle.value)
                    _previewSession.SetPlaying(true);
            }

            _canvas.SetPreviewActive(IsPreviewing);
            _canvas.SetTime(DisplayTime);
            PushPlaybackRangeToCanvas();
            RefreshSequenceControls();
            UpdatePreviewVisuals();
        }

        private void SyncCanvasSelection()
        {
            if (Provider == null || _canvas == null)
                return;

            var activeSelected = Selection.objects.OfType<SegmentSelectionModel>()
                .Where(m => ReferenceEquals(m.Handle.Provider, Provider)
                            && string.Equals(m.Handle.SequenceName, SequenceName, StringComparison.Ordinal))
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
                if (selected != null && selected.Count > 0)
                {
                    Selection.objects = selected.Cast<UnityEngine.Object>().ToArray();
                }
                else if (Provider != null)
                {
                    Selection.activeGameObject = Provider.gameObject;
                }
                else
                {
                    Selection.objects = Array.Empty<UnityEngine.Object>();
                }

                SyncCanvasSelection();
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }

        private void OnTimeAdjustmentCommitted(IReadOnlyList<(SegmentSelectionModel model, float start, float duration)> changes)
        {
            _sessionState.UpdateProxyTimings(changes);
        }

        private void OnDeleteRequested(IReadOnlyList<SegmentSelectionModel> selectedModels)
        {
            _sessionState.DeleteSegments(selectedModels);

            bool hasRemainingSegmentSelection = Selection.objects
                .OfType<SegmentSelectionModel>()
                .Any(model => model != null
                              && ReferenceEquals(model.Handle.Provider, Provider)
                              && string.Equals(model.Handle.SequenceName, SequenceName, StringComparison.Ordinal));

            if (!hasRemainingSegmentSelection && Provider != null)
            {
                Selection.activeGameObject = Provider.gameObject;
                SyncCanvasSelection();
            }
        }

        private void OnAddRequested(Type type, float time)
        {
            _sessionState.AddSegment(type, time);
        }

        private void OnConvertRequested(IReadOnlyList<SegmentSelectionModel> selectedModels, TimboJimboEditor.Sequencer.Converters.SegmentConverter converter)
        {
            var inserted = _sessionState.ConvertSegments(selectedModels, converter);
            if (inserted == null || inserted.Count == 0)
                return;

            var insertedModels = _sessionState.Models
                .Where(m => m != null && inserted.Contains(m.Segment))
                .ToList();

            if (insertedModels.Count > 0)
            {
                Selection.objects = insertedModels.Cast<UnityEngine.Object>().ToArray();
                SyncCanvasSelection();
            }
        }

        private void OnDropSegmentRequested(Segment segment)
        {
            _sessionState.AddSegment(segment);
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
            var pasted = _sessionState.TryPaste(_clipboard, DisplayTime, IsPreviewing);
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

        private void StackSelectedSegmentsEndToEnd()
        {
            var selected = GetSelectedModelsForProvider();

            if (selected.Count < 2)
                return;

            _sessionState.StackSegmentsEndToEnd(selected);
        }

        private void AlignSelectedSegmentsByStart()
        {
            var selected = GetSelectedModelsForProvider();
            if (selected.Count < 2)
                return;

            _sessionState.AlignSegmentsByStart(selected);
        }

        private void AlignSelectedSegmentsByEnd()
        {
            var selected = GetSelectedModelsForProvider();
            if (selected.Count < 2)
                return;

            _sessionState.AlignSegmentsByEnd(selected);
        }

        private List<SegmentSelectionModel> GetSelectedModelsForProvider()
        {
            if (Provider == null)
                return new List<SegmentSelectionModel>();

            return Selection.objects
                .OfType<SegmentSelectionModel>()
                .Where(m => m != null
                            && ReferenceEquals(m.Handle.Provider, Provider)
                            && string.Equals(m.Handle.SequenceName, SequenceName, StringComparison.Ordinal))
                .ToList();
        }

        private void EnsurePreviewSession()
        {
            if (Provider == null || string.IsNullOrWhiteSpace(SequenceName) || IsPreviewing)
                return;

            _previewSession = SegmentPreviewSession.Acquire(Provider, SequenceName);
            _previewSession.Rebuilt += OnSessionRebuilt;
            _previewSession.Disposed += OnSessionDisposed;
            EnsurePlaybackRange();
            _rangeState.SetPlayhead(DisplayTime, isPlaying: false);
            _previewSession.Seek(DisplayTime);
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
            _loopResumeAtTime = -1d;
            UpdatePreviewVisuals();
        }

        private void OnSessionRebuilt()
        {
            EnsurePlaybackRange();
            _canvas.SetTime(DisplayTime);
            PushPlaybackRangeToCanvas();
            UpdatePreviewVisuals();
            Repaint();
        }

        private void OnSessionDisposed()
        {
            _previewSession = null;
            _loopResumeAtTime = -1d;
            _playToggle.SetValueWithoutNotify(false);
            _canvas.SetPreviewActive(false);
            UpdatePreviewVisuals();
        }

        private void SetPlaying(bool playing)
        {
            if (!playing)
            {
                _loopResumeAtTime = -1d;
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

            EnsurePlaybackRange();
            _rangeState.SetPlayhead(DisplayTime, isPlaying: true);
            _previewSession.Seek(DisplayTime);
            _previewSession.SetPlaying(true);
            _loopResumeAtTime = -1d;
            _lastTickTime = EditorApplication.timeSinceStartup;
        }

        private void StopPreview()
        {
            StopRecording(commit: false);
            _playToggle.SetValueWithoutNotify(false);
            _loopResumeAtTime = -1d;
            DisposePreviewSession();
            _rangeState.SetPlayhead(0f, isPlaying: false);
            _canvas.SetTime(DisplayTime);
        }

        private void OnSeekRequested(float time)
        {
            SeekDisplayTime(time);
        }

        private void SeekDisplayTime(float time)
        {
            EnsurePreviewSession();
            _rangeState.SetPlayhead(time, isPlaying: false);
            _previewSession.Seek(DisplayTime);
            _loopResumeAtTime = -1d;
            _playToggle.SetValueWithoutNotify(false);
            _previewSession.SetPlaying(false);
            _canvas.SetTime(DisplayTime);
        }

        private void OnEditorUpdate()
        {
            if (!_playToggle.value || !IsPreviewing)
                return;

            double now = EditorApplication.timeSinceStartup;

            if (_loopResumeAtTime > 0d)
            {
                if (now < _loopResumeAtTime)
                {
                    _lastTickTime = now;
                    return;
                }

                _loopResumeAtTime = -1d;
                SeekForLoop(ActivePlaybackRangeStart);
                _previewSession.SetPlaying(true);
                _lastTickTime = now;
                return;
            }

            float dt = Mathf.Min((float)(now - _lastTickTime), 0.1f);
            _lastTickTime = now;

            _previewSession.Tick(dt);
            _rangeState.SetPlayhead(_previewSession.Time, isPlaying: true);
            _canvas.SetTime(DisplayTime);

            EnsurePlaybackRange();
            if (DisplayTime < ActivePlaybackRangeStart - 0.0001f)
            {
                SeekForLoop(ActivePlaybackRangeStart);
                return;
            }

            if (DisplayTime >= ActivePlaybackRangeEnd - 0.0001f)
                BeginLoopWrap(now);
        }

        private void BeginLoopWrap(double now)
        {
            if (!IsPreviewing)
                return;

            _previewSession.SetPlaying(false);
            SeekForLoop(ActivePlaybackRangeEnd);

            if (_rangeState.LoopDelaySeconds <= 0.0001f)
            {
                SeekForLoop(ActivePlaybackRangeStart);
                _previewSession.SetPlaying(true);
                _loopResumeAtTime = -1d;
                _lastTickTime = now;
                return;
            }

            _loopResumeAtTime = now + _rangeState.LoopDelaySeconds;
        }

        private void SeekForLoop(float time)
        {
            if (!IsPreviewing)
                return;

            _rangeState.SetPlayhead(time, isPlaying: true);
            _previewSession.Seek(DisplayTime);
            _canvas.SetTime(DisplayTime);
        }

        private void OnCanvasPlaybackRangeChanged(float start, float end)
        {
            ApplyPlaybackRangeFromCanvas(start, end);
        }

        private void OnCanvasPlaybackRangeResetRequested()
        {
            Undo.RecordObject(_rangeState, "Reset Playback Range");
            _rangeState.ResetToDefault(GetPlaybackDurationLimit());
            EditorUtility.SetDirty(_rangeState);
            EnsurePlaybackRange();

            if (IsPreviewing)
                _previewSession.SetPlaybackRange(_rangeState.GetPlaybackRange());
        }

        private void ApplyPlaybackRangeFromCanvas(float start, float end)
        {
            Undo.RecordObject(_rangeState, "Set Playback Range");
            float duration = GetPlaybackDurationLimit();
            _rangeState.SetRange(start, end, duration);
            EnsurePlaybackRange();

            if (IsPreviewing)
            {
                _previewSession.SetPlaybackRange(_rangeState.GetPlaybackRange());
                _rangeState.SetPlayhead(DisplayTime, isPlaying: false);
                _previewSession.Seek(DisplayTime);
                _canvas.SetTime(DisplayTime);
            }

            EditorUtility.SetDirty(_rangeState);
        }

        private void EnsurePlaybackRange()
        {
            _rangeState.UpdateDuration(GetPlaybackDurationLimit());
            PushPlaybackRangeToCanvas();
        }

        private float GetPlaybackDurationLimit()
        {
            float duration = 0f;
            if (IsPreviewing)
                duration = Mathf.Max(duration, _previewSession.Duration);

            if (_sessionState?.Models != null)
            {
                for (int i = 0; i < _sessionState.Models.Count; i++)
                    duration = Mathf.Max(duration, _sessionState.Models[i].EndTime);
            }

            return Mathf.Max(duration, MinPlaybackRangeDuration);
        }

        private void PushPlaybackRangeToCanvas()
        {
            if (_canvas == null)
                return;

            bool visible = Provider != null && ActiveSequence != null;
            _canvas.SetPlaybackRange(_rangeState.RangeStart, _rangeState.RangeEnd, visible);
        }

        private void UpdatePreviewVisuals()
        {
            bool previewing = IsPreviewing;
            bool recording = IsRecording;

            if (_previewErrorBox != null)
            {
                string error = _previewSession?.BuildError;
                _previewErrorBox.text = error != null ? $"Preview could not compile this sequence.\n{error}" : string.Empty;
                _previewErrorBox.style.display = error != null ? DisplayStyle.Flex : DisplayStyle.None;
            }

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

            if (Provider == null || ActiveSequence == null || !IsPreviewing)
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
            _editTracker = new UserEditTracker(filterOut: bp => bp.Target is SequenceProvider || !_recordSnapshotValues.ContainsKey(bp));
            _editTracker.StartDetecting(OnRecordedUserEdit);
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
                            if (removed.Segment is IStartTimeConfigurable && ActiveSequence != null)
                            {
                                ActiveSequence.Segments.Remove(removed.Segment);
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
        }

        private void OnRecordedEdit(BindablePropertyValueEdit edit)
        {
            float cursor = DisplayTime;
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
                    cursor = model.EndTime;
                    SeekDisplayTime(cursor);
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
                    TimelineSessionState.CommitChanges(new[] { associatedModel });
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
            ActiveSequence?.Segments.Add(newSegment);

            _recordedEdits[edit.BindableProperty] = new RecordedEdit { Segment = newSegment, Created = true };
            _sessionState.Refresh();
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
    }
}