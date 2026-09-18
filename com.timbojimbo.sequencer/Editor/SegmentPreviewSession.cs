using System;
using TimboJimbo.Sequencer;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Sequencer
{
    public sealed class SegmentPreviewSession : IDisposable
    {
        public SequenceProvider Provider { get; }
        public string SequenceName { get; }
        public SequencePlayer Instance { get; private set; }
        public float Time { get; private set; }
        public float Duration => Instance != null ? Instance.Duration : 0f;
        public bool IsDisposed { get; private set; }
        public PlaybackRange PlaybackRange { get; private set; }

        /// <summary>Set when the last <see cref="Rebuild"/> could not compile the sequence; null when <see cref="Instance"/> is live.</summary>
        public string BuildError { get; private set; }
        public bool CanPreview => Instance != null;

        public event Action Rebuilt;
        public event Action Disposed;

        private SegmentPreviewSession(SequenceProvider provider, string sequenceName)
        {
            Provider = provider;
            SequenceName = sequenceName;
            PlaybackRange = new PlaybackRange(0f, 1f, true);
        }

        public static SegmentPreviewSession Acquire(SequenceProvider provider, string sequenceName)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            if (string.IsNullOrWhiteSpace(sequenceName))
                throw new ArgumentException("Sequence name is required.", nameof(sequenceName));

            var session = new SegmentPreviewSession(provider, sequenceName);
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

            try
            {
                Instance = Provider.CreatePlayer(SequenceName, PlaybackRange, isPreview: true);
                BuildError = null;
                Time = Mathf.Clamp(preservedTime, 0f, Duration);
                Instance.Seek(Time);
            }
            catch (Exception exception)
            {
                Instance = null;
                BuildError = DescribeBuildFailure(exception);
            }

            Rebuilt?.Invoke();
            FlushAndRepaint();
        }

        // Applying animated values dirties any driven UGUI graphics (via OnDidApplyAnimationProperties), but
        // that rebuild is deferred to the next player-loop tick. In edit-mode preview nothing pumps that loop
        // between scrubs, so the scene view would repaint against the stale mesh until an unrelated editor
        // interaction runs it. Force the canvas rebuild now so the repaint shows the current frame.
        private static void FlushAndRepaint()
        {
            Canvas.ForceUpdateCanvases();
            SceneView.RepaintAll();
        }

        private string DescribeBuildFailure(Exception exception)
        {
            var report = Provider.ValidateSequence(SequenceName);
            if (report.IsValid)
                return exception.Message;

            var lines = new System.Text.StringBuilder(exception.Message);
            int shown = 0;
            foreach (var issue in report.Issues)
            {
                if (shown++ == 3) { lines.Append("\n…"); break; }
                lines.Append('\n').Append(issue.Message);
            }
            return lines.ToString();
        }

        public void SetPlaybackRange(PlaybackRange playbackRange)
        {
            ThrowIfDisposed();

            PlaybackRange = playbackRange.Normalize(Duration);

            if (Instance != null)
                Instance.SetPlaybackRange(PlaybackRange);
        }

        public void Seek(float time)
        {
            ThrowIfDisposed();
            if (Instance == null)
                return;

            Time = Mathf.Clamp(time, 0f, Duration);
            Instance.Seek(Time);
            FlushAndRepaint();
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
            FlushAndRepaint();
        }

        public void Dispose()
        {
            if (IsDisposed)
                return;

            IsDisposed = true;
            DisposeInstance();
            Disposed?.Invoke();
            FlushAndRepaint();
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
}
