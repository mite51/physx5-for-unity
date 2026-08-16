using System;
using System.Collections.Generic;
using UNDPWR.Diagnostics;

namespace UNDPWR.Rollback
{
    /// <summary>
    /// Stores predicted, locally speculative, and server-authoritative input for every player.
    /// </summary>
    public sealed class InputBuffer
    {
        private readonly SimInputFrame[] _frames;
        private readonly uint[] _playerIds;
        private int _newestTick = -1;
        private int _predictedThrough = -1;

        public int Capacity { get { return _frames.Length; } }
        public int PlayerCount { get { return _playerIds.Length; } }

        /// <summary>The newest unbroken tick finalized by the authoritative server.</summary>
        public int ConfirmedThrough { get; private set; }

        public InputBuffer(IList<uint> playerIds, int capacity)
        {
            if (playerIds == null)
            {
                throw new ArgumentNullException("playerIds");
            }
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException("capacity");
            }
            _playerIds = new uint[playerIds.Count];
            for (int i = 0; i < playerIds.Count; ++i)
            {
                _playerIds[i] = playerIds[i];
            }
            Array.Sort(_playerIds);
            for (int i = 1; i < _playerIds.Length; ++i)
            {
                if (_playerIds[i] == _playerIds[i - 1])
                {
                    throw new ArgumentException("Player IDs must be unique.", "playerIds");
                }
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

        public uint[] CopyPlayerIds()
        {
            uint[] copy = new uint[_playerIds.Length];
            Array.Copy(_playerIds, copy, copy.Length);
            return copy;
        }

        /// <summary>Copies the command baseline held immediately after a rebuild tick.</summary>
        public void CopyBaseline(int tick, out SimInput[] inputs, out uint[] sequences)
        {
            inputs = new SimInput[_playerIds.Length];
            sequences = new uint[_playerIds.Length];
            SimInputFrame frame = _frames[Index(tick)];
            for (int slot = 0; slot < _playerIds.Length; ++slot)
            {
                if (frame.Tick == tick
                    && frame[slot].Provenance == SimInputProvenance.Authoritative)
                {
                    inputs[slot] = frame[slot];
                    sequences[slot] = frame[slot].Sequence;
                }
                else
                {
                    inputs[slot] = SimInput.Neutral(_playerIds[slot], tick);
                    inputs[slot].Provenance = SimInputProvenance.Authoritative;
                }
            }
        }

        public int SlotOf(uint playerId)
        {
            int index = Array.BinarySearch(_playerIds, playerId);
            return index < 0 ? -1 : index;
        }

        /// <summary>Records a local proposal before the server assigns it.</summary>
        /// <returns>The earliest simulated tick dirtied by the change, or -1.</returns>
        public int SubmitSpeculative(SimInput input, uint sequence)
        {
            int slot;
            if (!Validate(input, out slot))
            {
                return -1;
            }
            EnsureFrame(input.Tick);
            SimInputFrame frame = _frames[Index(input.Tick)];
            SimInput existing = frame[slot];
            if (existing.Provenance == SimInputProvenance.Authoritative)
            {
                return -1;
            }
            if (existing.Provenance == SimInputProvenance.Speculative
                && existing.Sequence == sequence && existing.SameCommandAs(input))
            {
                return -1;
            }

            input.Provenance = SimInputProvenance.Speculative;
            input.Sequence = sequence;
            frame[slot] = input;
            return DirtyTick(input.Tick, existing, input);
        }

        /// <summary>Moves a speculative proposal after the server retimes it.</summary>
        public int RetimeSpeculative(uint playerId, uint sequence, int fromTick, int toTick)
        {
            int slot = SlotOf(playerId);
            if (slot < 0)
            {
                return -1;
            }
            SimInput command = SimInput.Neutral(playerId, fromTick);
            SimInputFrame oldFrame = _frames[Index(fromTick)];
            if (oldFrame.Tick != fromTick
                || oldFrame[slot].Provenance != SimInputProvenance.Speculative
                || oldFrame[slot].Sequence != sequence)
            {
                return -1;
            }
            command = oldFrame[slot];
            int dirty = ClearSpeculative(playerId, sequence, fromTick);
            command.PlayerId = playerId;
            command.Tick = toTick;
            int submitted = SubmitSpeculative(command, sequence);
            if (dirty < 0 || (submitted >= 0 && submitted < dirty))
            {
                dirty = submitted;
            }
            return dirty;
        }

        /// <summary>Removes a rejected or retimed speculative proposal.</summary>
        public int ClearSpeculative(uint playerId, uint sequence, int tick)
        {
            int slot = SlotOf(playerId);
            if (slot < 0 || tick < 0)
            {
                return -1;
            }
            SimInputFrame frame = _frames[Index(tick)];
            if (frame.Tick != tick)
            {
                return -1;
            }
            SimInput existing = frame[slot];
            if (existing.Provenance != SimInputProvenance.Speculative
                || existing.Sequence != sequence)
            {
                return -1;
            }
            SimInput predicted = PredictForSlot(slot, tick);
            predicted.Tick = tick;
            predicted.Provenance = SimInputProvenance.Predicted;
            predicted.Sequence = 0;
            frame[slot] = predicted;
            return DirtyTick(tick, existing, predicted);
        }

        /// <summary>Records one command finalized by the server.</summary>
        public int SubmitAuthoritative(SimInput input, uint sequence)
        {
            int slot;
            if (!Validate(input, out slot))
            {
                return -1;
            }
            EnsureFrame(input.Tick);
            SimInputFrame frame = _frames[Index(input.Tick)];
            SimInput existing = frame[slot];

            input.Provenance = SimInputProvenance.Authoritative;
            input.Sequence = sequence;
            frame[slot] = input;
            RecomputeConfirmedFrontier();
            return DirtyTick(input.Tick, existing, input);
        }

        /// <summary>Atomically records every player command for one authoritative tick.</summary>
        public int SubmitAuthoritativeFrame(int tick, IList<SimInput> inputs, IList<uint> sequences)
        {
            if (inputs == null || sequences == null
                || inputs.Count != _playerIds.Length || sequences.Count != _playerIds.Length)
            {
                throw new ArgumentException("A canonical frame must contain exactly one input and sequence per player.");
            }
            bool[] seen = new bool[_playerIds.Length];
            int dirty = -1;
            for (int i = 0; i < inputs.Count; ++i)
            {
                SimInput input = inputs[i];
                if (input.Tick != tick)
                {
                    throw new ArgumentException("Every canonical input must use the frame tick.");
                }
                int slot = SlotOf(input.PlayerId);
                if (slot < 0 || seen[slot])
                {
                    throw new ArgumentException("A canonical frame contains an unknown or duplicate player.");
                }
                seen[slot] = true;
                int changed = SubmitAuthoritative(input, sequences[i]);
                if (changed >= 0 && (dirty < 0 || changed < dirty))
                {
                    dirty = changed;
                }
            }
            return dirty;
        }

        /// <summary>Returns a complete frame, predicting missing slots by holding prior input.</summary>
        public SimInputFrame GetOrPredict(int tick)
        {
            if (tick < 0)
            {
                throw new ArgumentOutOfRangeException("tick");
            }
            EnsureFrame(tick);
            SimInputFrame frame = _frames[Index(tick)];
            if (tick > _predictedThrough)
            {
                _predictedThrough = tick;
            }
            for (int slot = 0; slot < _playerIds.Length; ++slot)
            {
                if (frame[slot].Provenance != SimInputProvenance.Predicted)
                {
                    continue;
                }
                SimInput predicted = PredictForSlot(slot, tick);
                predicted.Tick = tick;
                predicted.Provenance = SimInputProvenance.Predicted;
                predicted.Sequence = 0;
                frame[slot] = predicted;
            }
            return frame;
        }

        public int OldestTick
        {
            get
            {
                if (_newestTick < 0) return -1;
                int oldest = _newestTick - _frames.Length + 1;
                return oldest < 0 ? 0 : oldest;
            }
        }

        /// <summary>Clears commands while preserving an already-confirmed snapshot frontier.</summary>
        public void ResetAfterConfirmed(int confirmedTick)
        {
            for (int i = 0; i < _frames.Length; ++i)
            {
                _frames[i].Reset(-1);
            }
            _newestTick = confirmedTick;
            _predictedThrough = confirmedTick;
            ConfirmedThrough = confirmedTick;
        }

        /// <summary>Seeds held commands from the authoritative rebuild payload.</summary>
        public void SeedBaseline(int confirmedTick, IList<SimInput> inputs, IList<uint> sequences)
        {
            if (inputs == null || sequences == null
                || inputs.Count != _playerIds.Length || sequences.Count != _playerIds.Length)
            {
                throw new ArgumentException("A rebuild baseline must contain one command per player.");
            }
            EnsureFrame(confirmedTick);
            SimInputFrame frame = _frames[Index(confirmedTick)];
            bool[] seen = new bool[_playerIds.Length];
            for (int i = 0; i < inputs.Count; ++i)
            {
                int slot = SlotOf(inputs[i].PlayerId);
                if (slot < 0 || seen[slot])
                {
                    throw new ArgumentException("A rebuild baseline contains an unknown or duplicate player.");
                }
                SimInput input = inputs[i];
                input.Tick = confirmedTick;
                input.Provenance = SimInputProvenance.Authoritative;
                input.Sequence = sequences[i];
                frame[slot] = input;
                seen[slot] = true;
            }
            _newestTick = confirmedTick;
            _predictedThrough = confirmedTick;
            ConfirmedThrough = confirmedTick;
        }

        private SimInput PredictForSlot(int slot, int tick)
        {
            int oldest = OldestTick;
            if (oldest < 0)
            {
                oldest = 0;
            }
            for (int previous = tick - 1; previous >= oldest; --previous)
            {
                SimInputFrame frame = _frames[Index(previous)];
                if (frame.Tick != previous)
                {
                    continue;
                }
                SimInput command = frame[slot];
                if (command.Provenance != SimInputProvenance.Predicted)
                {
                    return command;
                }
            }
            return SimInput.Neutral(_playerIds[slot], tick);
        }

        private int DirtyTick(int tick, SimInput existing, SimInput replacement)
        {
            if (tick > _predictedThrough || existing.SameCommandAs(replacement))
            {
                return -1;
            }
            return tick;
        }

        private bool Validate(SimInput input, out int slot)
        {
            slot = SlotOf(input.PlayerId);
            if (slot < 0)
            {
                SimLog.Warning("Input for unknown player " + input.PlayerId + " discarded.");
                return false;
            }
            if (input.Tick < 0)
            {
                SimLog.Warning("Input with a negative tick was discarded.");
                return false;
            }
            if (_newestTick >= 0 && input.Tick < OldestTick)
            {
                SimLog.Warning(string.Format(
                    "Input tick {0} is older than retained tick {1}; a server rebuild is required.",
                    input.Tick, OldestTick));
                return false;
            }
            return true;
        }

        private void EnsureFrame(int tick)
        {
            SimInputFrame frame = _frames[Index(tick)];
            if (frame.Tick != tick)
            {
                frame.Reset(tick);
            }
            if (tick > _newestTick)
            {
                _newestTick = tick;
            }
        }

        private void RecomputeConfirmedFrontier()
        {
            int candidate = ConfirmedThrough + 1;
            while (candidate <= _newestTick)
            {
                SimInputFrame frame = _frames[Index(candidate)];
                if (frame.Tick != candidate || !frame.IsComplete)
                {
                    break;
                }
                candidate += 1;
            }
            ConfirmedThrough = candidate - 1;
        }

        private int Index(int tick)
        {
            int index = tick % _frames.Length;
            return index < 0 ? index + _frames.Length : index;
        }
    }
}
