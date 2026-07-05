using TimboJimbo.Sequencer.Segments;
using UnityEngine;

namespace TimboJimbo.Sequencer
{
    /// <summary>
    /// Convenience helpers for turning a <see cref="Segment"/> directly into a
    /// <see cref="SequencePlayer"/>, either created idle (<c>CreatePlayer</c>) or started and
    /// self-driving (<c>Play</c>).
    /// </summary>
    public static class SegmentExtensions
    {
        /// <summary>Creates a player for the sequence without starting it.</summary>
        public static SequencePlayer CreatePlayer<TSeg>(this TSeg sequence, bool isPreview = false, bool restoreValuesOnDispose = true, bool autoDrive = false)
            where TSeg : Segment
        {
            return SequencePlayer.Create(sequence, isPreview, restoreValuesOnDispose, autoDrive);
        }

        /// <summary>Creates a player for the sequence over a playback range, without starting it.</summary>
        public static SequencePlayer CreatePlayer<TSeg>(this TSeg sequence, PlaybackRange playbackRange, bool isPreview = false, bool restoreValuesOnDispose = true, bool autoDrive = false)
            where TSeg : Segment
        {
            return SequencePlayer.Create(sequence, playbackRange, isPreview, restoreValuesOnDispose, autoDrive);
        }

        /// <summary>Creates a self-driving player for the sequence and starts it.</summary>
        public static SequencePlayer Play<TSeg>(this TSeg sequence, bool loop = false, float speed = 1f, bool restoreValuesOnDispose = true)
            where TSeg : Segment
        {
            var player = SequencePlayer.Create(sequence, isPreview: false, restoreValuesOnDispose, autoDrive: true);
            player.Loop = loop;
            player.Speed = speed;
            player.Play();
            return player;
        }

        /// <summary>Creates a self-driving player for the sequence over a range and starts it.</summary>
        public static SequencePlayer Play<TSeg>(this TSeg sequence, PlaybackRange playbackRange, bool loop = false, float speed = 1f, bool restoreValuesOnDispose = true)
            where TSeg : Segment
        {
            var player = SequencePlayer.Create(sequence, playbackRange, isPreview: false, restoreValuesOnDispose, autoDrive: true);
            player.Loop = loop;
            player.Speed = speed;
            player.Play();
            return player;
        }
    }
}
