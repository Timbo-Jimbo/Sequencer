using System;
using TimboJimbo.Sequencer;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Sequencer
{
    public sealed class SegmentPreviewSession : IDisposable
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
}
