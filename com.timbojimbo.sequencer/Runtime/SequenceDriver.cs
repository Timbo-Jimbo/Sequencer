using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace TimboJimbo.Sequencer
{
    /// <summary>
    /// Central frame updater for self-driven <see cref="SequencePlayer"/>s (created with
    /// <c>autoDrive: true</c>). Players register themselves while playing and unregister when
    /// they stop, so this only ever ticks players that need it.
    ///
    /// Play mode is driven by an injected <see cref="PlayerLoop"/> system; edit-mode preview is
    /// driven by <c>EditorApplication.update</c>.
    /// </summary>
    public static class SequenceDriver
    {
        private static readonly List<SequencePlayer> _players = new();
        private static readonly List<SequencePlayer> _tickBuffer = new();

        public static void Register(SequencePlayer player)
        {
            if (player != null && !_players.Contains(player))
                _players.Add(player);
        }

        public static void Unregister(SequencePlayer player)
        {
            _players.Remove(player);
        }

        private static void Tick(float deltaTime)
        {
            if (deltaTime <= 0f || _players.Count == 0)
                return;

            // Snapshot so players can register/unregister (e.g. from a paused/completed event)
            // without mutating the collection mid-iteration.
            _tickBuffer.Clear();
            _tickBuffer.AddRange(_players);

            for (int i = 0; i < _tickBuffer.Count; i++)
                _tickBuffer[i].Tick(deltaTime);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InstallPlayerLoop()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();

            if (TryInsertInto(ref loop, typeof(Update)))
                PlayerLoop.SetPlayerLoop(loop);
        }

        private static bool TryInsertInto(ref PlayerLoopSystem system, Type phase)
        {
            if (system.type == phase)
            {
                var subSystems = system.subSystemList != null
                    ? new List<PlayerLoopSystem>(system.subSystemList)
                    : new List<PlayerLoopSystem>();

                if (subSystems.Exists(s => s.type == typeof(SequenceDriver)))
                    return true;

                subSystems.Add(new PlayerLoopSystem
                {
                    type = typeof(SequenceDriver),
                    updateDelegate = RuntimeTick
                });

                system.subSystemList = subSystems.ToArray();
                return true;
            }

            if (system.subSystemList == null)
                return false;

            for (int i = 0; i < system.subSystemList.Length; i++)
            {
                var child = system.subSystemList[i];
                if (TryInsertInto(ref child, phase))
                {
                    system.subSystemList[i] = child;
                    return true;
                }
            }

            return false;
        }

        private static void RuntimeTick()
        {
            Tick(Time.deltaTime);
        }

#if UNITY_EDITOR
        private static double _lastEditorTime;

        [UnityEditor.InitializeOnLoadMethod]
        private static void InstallEditorLoop()
        {
            UnityEditor.EditorApplication.update -= EditorTick;
            UnityEditor.EditorApplication.update += EditorTick;
        }

        private static void EditorTick()
        {
            var now = UnityEditor.EditorApplication.timeSinceStartup;

            if (_lastEditorTime <= 0d)
            {
                _lastEditorTime = now;
                return;
            }

            var deltaTime = (float)(now - _lastEditorTime);
            _lastEditorTime = now;

            // During play mode the PlayerLoop drives playback instead.
            if (UnityEditor.EditorApplication.isPlaying)
                return;

            // Clamp to avoid a large catch-up step after the editor was unfocused/paused.
            Tick(Mathf.Min(deltaTime, 0.1f));
        }
#endif
    }
}
