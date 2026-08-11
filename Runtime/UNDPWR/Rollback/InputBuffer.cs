using System;
using System.Collections.Generic;
using UNDPWR.Diagnostics;

namespace UNDPWR.Rollback
{
    /// <summary>
    /// Holds recent and predicted inputs for every player, and reports the newest tick
    /// whose inputs are all known.
    /// </summary>
    /// <remarks>
    /// The buffer is where prediction happens. A tick that has not received a player's
    /// input yet gets one anyway, by repeating that player's last known input, which is
    /// right far more often than it is wrong because players hold buttons down for many
    /// ticks at a time. When the real input eventually arrives and matches the guess,
    /// nothing happens; when it differs, the engine is told to roll back.
    ///
    /// <para>Like <see cref="UNDPWR.Core.SnapshotRing"/> this is a fixed-size ring
    /// allocated once, because growing it mid-session would allocate during exactly the
    /// frames that are already busiest.</para>
    /// </remarks>
    public sealed class InputBuffer
    {
        private readonly SimInputFrame[] _frames;
        private readonly uint[] _playerIds;
        private readonly SimInput[] _lastKnown;
        private readonly int[] _lastKnownTick;

        private int _newestTick = -1;
        private int _predictedThrough = -1;

        /// <summary>How many ticks the buffer retains.</summary>
        public int Capacity { get { return _frames.Length; } }

        /// <summary>How many players the buffer tracks.</summary>
        public int PlayerCount { get { return _playerIds.Length; } }

        /// <summary>
        /// The newest tick for which every player's input has actually been received.
        /// </summary>
        /// <remarks>
        /// Everything up to here can be simulated without guessing, so this is the
        /// frontier the confirmed timeline advances to. -1 means nothing is confirmed
        /// yet.
        /// </remarks>
        public int ConfirmedThrough { get; private set; }

        /// <summary>
        /// Creates a buffer.
        /// </summary>
        /// <param name="playerIds">
        /// Every player in the session. Sorted ascending on construction so that the slot
        /// order is identical on every peer regardless of join order.
        /// </param>
        /// <param name="capacity">How many ticks to retain.</param>
        public InputBuffer(IList<uint> playerIds, int capacity)
        {
            if (playerIds == null)
            {
                throw new ArgumentNullException("playerIds");
            }
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException("capacity", "An input buffer needs at least one slot.");
            }

            // Slot order must not depend on join order, or two peers iterate the same
            // players in different sequences and apply their forces in different orders.
            _playerIds = new uint[playerIds.Count];
            for (int i = 0; i < playerIds.Count; ++i)
            {
                _playerIds[i] = playerIds[i];
            }
            Array.Sort(_playerIds);

            _lastKnown = new SimInput[_playerIds.Length];
            _lastKnownTick = new int[_playerIds.Length];
            for (int i = 0; i < _playerIds.Length; ++i)
            {
                _lastKnown[i] = SimInput.Neutral(_playerIds[i], -1);
                _lastKnownTick[i] = -1;
            }

            _frames = new SimInputFrame[capacity];
            for (int i = 0; i < capacity; ++i)
            {
                _frames[i] = new SimInputFrame(_playerIds.Length);
                for (int slot = 0; slot < _playerIds.Length; ++slot)
                {
                    _frames[i][slot] = SimInput.Neutral(_playerIds[slot], -1);
                }
            }

            ConfirmedThrough = -1;
        }

        /// <summary>
        /// Copies the sorted player-ID set this buffer tracks, for a rebuild that needs to
        /// compare or reuse the current roster.
        /// </summary>
        public uint[] CopyPlayerIds()
        {
            uint[] copy = new uint[_playerIds.Length];
            Array.Copy(_playerIds, copy, _playerIds.Length);
            return copy;
        }

        /// <summary>The slot a player occupies, or -1 when the player is not in the session.</summary>
        public int SlotOf(uint playerId)
        {
            int index = Array.BinarySearch(_playerIds, playerId);
            return index < 0 ? -1 : index;
        }

        /// <summary>
        /// Records a received input.
        /// </summary>
        /// <returns>
        /// The tick a misprediction touched, or -1 when nothing needs correcting — because
        /// the guess already matched, because the tick is too old to matter, or because the
        /// tick has not been simulated yet and so was never guessed at. This is advisory:
        /// the shipped <see cref="RollbackEngine"/> rewinds on a fixed schedule and ignores
        /// it, so that its operation sequence does not depend on network timing. It is
        /// returned for diagnostics and for a caller that wants to drive conditional
        /// rollback.
        /// </returns>
        public int Submit(SimInput input)
        {
            int slot = SlotOf(input.PlayerId);
            if (slot < 0)
            {
                SimLog.Warning(string.Format("Input for unknown player {0} discarded", input.PlayerId));
                return -1;
            }
            if (input.Tick < 0)
            {
                SimLog.Warning(string.Format("Input for player {0} has a negative tick and was discarded", input.PlayerId));
                return -1;
            }

            // An input older than the buffer cannot be honoured: the state it would apply
            // to has already been overwritten. The peer that sent it is further behind
            // than the session is configured to tolerate.
            if (_newestTick >= 0 && input.Tick < OldestTick)
            {
                SimLog.Warning(string.Format(
                    "Input for player {0} at tick {1} arrived after that tick left the buffer (oldest is {2}). " +
                    "That peer is beyond the session's latency budget; it needs a resynchronisation.",
                    input.PlayerId, input.Tick, OldestTick));
                return -1;
            }

            EnsureFrame(input.Tick);

            SimInputFrame frame = _frames[Index(input.Tick)];
            SimInput existing = frame[slot];

            input.IsPredicted = false;
            frame[slot] = input;

            if (input.Tick > _lastKnownTick[slot])
            {
                _lastKnown[slot] = input;
                _lastKnownTick[slot] = input.Tick;
            }

            RecomputeConfirmedFrontier();

            // An empty slot and a slot holding a guess look the same -- EnsureFrame marks a
            // recycled frame predicted -- so "was this tick ever handed to a step?" has to be
            // asked separately. It is the whole question here: a tick nobody simulated has
            // nothing to correct, however far the arriving command is from the neutral value
            // sitting in the slot. Local input stamped ahead by SimConfig.LocalInputDelay
            // lands this way every single tick, and reporting those as mispredictions would
            // hand a conditional-rollback caller a rewind on every frame.
            if (input.Tick > _predictedThrough)
            {
                return -1;
            }

            // A prediction that turned out to be right costs nothing, which is the normal
            // case: players hold inputs steady for many ticks at a time.
            if (!existing.IsPredicted || existing.SameCommandAs(input))
            {
                return -1;
            }

            SimLog.Verbose(string.Format("Mispredicted player {0} at tick {1}; rollback required",
                input.PlayerId, input.Tick));
            return input.Tick;
        }

        /// <summary>
        /// Returns the inputs for a tick, filling in predictions for anything not yet
        /// received.
        /// </summary>
        /// <remarks>
        /// Never fails and never returns a partial frame. A tick beyond what has been
        /// received is entirely predicted, which is what lets the simulation keep running
        /// while the network catches up.
        /// </remarks>
        public SimInputFrame GetOrPredict(int tick)
        {
            if (tick < 0)
            {
                throw new ArgumentOutOfRangeException("tick", "Tick numbers start at zero.");
            }

            EnsureFrame(tick);
            SimInputFrame frame = _frames[Index(tick)];

            // Serving a frame is the moment a tick becomes one that can be mispredicted.
            if (tick > _predictedThrough)
            {
                _predictedThrough = tick;
            }

            for (int slot = 0; slot < _playerIds.Length; ++slot)
            {
                if (!frame[slot].IsPredicted)
                {
                    continue;
                }

                // Repeat the player's last known input. Simple, and right most of the
                // time, because inputs are held far more often than they change.
                SimInput predicted = _lastKnown[slot];
                predicted.Tick = tick;
                predicted.IsPredicted = true;
                frame[slot] = predicted;
            }

            return frame;
        }

        /// <summary>The oldest tick still in the buffer, or -1 when it is empty.</summary>
        public int OldestTick
        {
            get
            {
                if (_newestTick < 0) return -1;
                int oldest = _newestTick - _frames.Length + 1;
                return oldest < 0 ? 0 : oldest;
            }
        }

        /// <summary>
        /// Discards everything, for a synchronised rebuild that replaces the timeline.
        /// </summary>
        /// <param name="resumeTick">The tick the rebuilt session resumes from.</param>
        public void Reset(int resumeTick)
        {
            for (int i = 0; i < _frames.Length; ++i)
            {
                _frames[i].Reset(-1);
            }
            for (int slot = 0; slot < _playerIds.Length; ++slot)
            {
                _lastKnown[slot] = SimInput.Neutral(_playerIds[slot], resumeTick);
                _lastKnownTick[slot] = -1;
            }

            _newestTick = resumeTick - 1;
            _predictedThrough = resumeTick - 1;
            ConfirmedThrough = resumeTick - 1;
            SimLog.Info(string.Format("Input buffer reset; resuming at tick {0}", resumeTick));
        }

        private void EnsureFrame(int tick)
        {
            SimInputFrame frame = _frames[Index(tick)];
            if (frame.Tick != tick)
            {
                // The slot belongs to an older tick; recycle it.
                frame.Reset(tick);
            }
            if (tick > _newestTick)
            {
                _newestTick = tick;
            }
        }

        private void RecomputeConfirmedFrontier()
        {
            // Walk forward from the current frontier for as long as every player's input
            // is present. Starting from the frontier rather than from the oldest tick
            // keeps this O(1) amortised, since the frontier only ever moves forward.
            int candidate = ConfirmedThrough + 1;
            int limit = _newestTick;

            while (candidate <= limit)
            {
                SimInputFrame frame = _frames[Index(candidate)];
                if (frame.Tick != candidate || !frame.IsComplete)
                {
                    break;
                }
                ++candidate;
            }

            ConfirmedThrough = candidate - 1;
        }

        private int Index(int tick)
        {
            return tick % _frames.Length;
        }
    }
}
