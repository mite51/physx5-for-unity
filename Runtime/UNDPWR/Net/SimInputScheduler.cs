using System;
using System.Collections.Generic;
using UNDPWR.Rollback;

namespace UNDPWR.Net
{
    /// <summary>
    /// Owns the server's immutable input timeline.
    /// </summary>
    /// <remarks>
    /// A proposal may be moved forward, but a finalized tick is never rewritten. When no new
    /// command is scheduled for a player, the last canonical command is held.
    /// </remarks>
    public sealed class SimInputScheduler
    {
        private const uint DecisionHistory = 256;
        private struct Scheduled
        {
            public uint Sequence;
            public int RequestedTick;
            public SimInput Input;
        }

        private readonly uint[] _playerIds;
        private readonly Dictionary<int, Dictionary<uint, Scheduled>> _pending =
            new Dictionary<int, Dictionary<uint, Scheduled>>();
        private readonly Dictionary<uint, uint> _lastSequence = new Dictionary<uint, uint>();
        private readonly Dictionary<ulong, SimInputDecision> _decisions =
            new Dictionary<ulong, SimInputDecision>();
        private readonly Dictionary<uint, Scheduled> _lastCanonical =
            new Dictionary<uint, Scheduled>();
        private readonly int _maxFutureTicks;

        /// <summary>The newest tick whose canonical frame has been finalized.</summary>
        public int CurrentTick { get; private set; }

        /// <summary>The scheduling horizon accepted by the server.</summary>
        public int MaxFutureTicks { get { return _maxFutureTicks; } }

        public SimInputScheduler(IList<uint> playerIds, int startTick, int maxFutureTicks)
        {
            if (playerIds == null)
            {
                throw new ArgumentNullException("playerIds");
            }
            if (maxFutureTicks < 1)
            {
                throw new ArgumentOutOfRangeException("maxFutureTicks");
            }
            _playerIds = new uint[playerIds.Count];
            for (int i = 0; i < playerIds.Count; ++i)
            {
                _playerIds[i] = playerIds[i];
            }
            Array.Sort(_playerIds);
            for (int i = 0; i < _playerIds.Length; ++i)
            {
                if (i > 0 && _playerIds[i] == _playerIds[i - 1])
                {
                    throw new ArgumentException("Player IDs must be unique.", "playerIds");
                }
                Scheduled seed = new Scheduled();
                seed.Input = SimInput.Neutral(_playerIds[i], startTick);
                _lastCanonical.Add(_playerIds[i], seed);
                _lastSequence.Add(_playerIds[i], 0);
            }
            CurrentTick = startTick;
            _maxFutureTicks = maxFutureTicks;
        }

        /// <summary>Validates and schedules one authenticated client proposal.</summary>
        public SimInputDecision Submit(uint senderId, ref SimInputProposal proposal)
        {
            SimInputDecision decision = new SimInputDecision();
            decision.PlayerId = senderId;
            decision.Sequence = proposal.Sequence;
            decision.RequestedTick = proposal.RequestedTick;
            decision.AssignedTick = -1;
            decision.Disposition = SimAdmissionDisposition.Rejected;

            uint previousSequence;
            if (!_lastSequence.TryGetValue(senderId, out previousSequence))
            {
                decision.Rejection = SimAdmissionRejection.UnknownPlayer;
                return decision;
            }
            if (proposal.Sequence == 0)
            {
                decision.Rejection = SimAdmissionRejection.DuplicateSequence;
                return decision;
            }
            SimInputDecision previous;
            if (_decisions.TryGetValue(DecisionKey(senderId, proposal.Sequence), out previous))
            {
                return previous;
            }
            if (proposal.Sequence <= previousSequence)
            {
                if (previousSequence - proposal.Sequence >= DecisionHistory)
                {
                    decision.Rejection = SimAdmissionRejection.DuplicateSequence;
                    return decision;
                }
            }
            if (proposal.Input.PlayerId != senderId)
            {
                decision.Rejection = SimAdmissionRejection.WrongPlayer;
                return Remember(senderId, previousSequence, decision);
            }

            int latest = CurrentTick + _maxFutureTicks;
            if (proposal.RequestedTick > latest)
            {
                decision.Rejection = SimAdmissionRejection.TooFarInFuture;
                return Remember(senderId, previousSequence, decision);
            }

            int assigned = proposal.RequestedTick <= CurrentTick
                ? CurrentTick + 1
                : proposal.RequestedTick;
            while (assigned <= latest && HasScheduled(senderId, assigned))
            {
                assigned += 1;
            }
            if (assigned > latest)
            {
                decision.Rejection = SimAdmissionRejection.TooFarInFuture;
                return Remember(senderId, previousSequence, decision);
            }

            Scheduled scheduled = new Scheduled();
            scheduled.Sequence = proposal.Sequence;
            scheduled.RequestedTick = proposal.RequestedTick;
            scheduled.Input = proposal.Input;
            scheduled.Input.PlayerId = senderId;
            scheduled.Input.Tick = assigned;

            Dictionary<uint, Scheduled> tickCommands;
            if (!_pending.TryGetValue(assigned, out tickCommands))
            {
                tickCommands = new Dictionary<uint, Scheduled>();
                _pending.Add(assigned, tickCommands);
            }
            tickCommands.Add(senderId, scheduled);
            decision.AssignedTick = assigned;
            decision.Disposition = assigned == proposal.RequestedTick
                ? SimAdmissionDisposition.Accepted
                : SimAdmissionDisposition.Retimed;
            decision.Rejection = SimAdmissionRejection.None;
            return Remember(senderId, previousSequence, decision);
        }

        private SimInputDecision Remember(uint senderId, uint previousSequence,
            SimInputDecision decision)
        {
            if (decision.Sequence > previousSequence)
            {
                _lastSequence[senderId] = decision.Sequence;
            }
            _decisions[DecisionKey(senderId, decision.Sequence)] = decision;
            if (decision.Sequence > DecisionHistory)
            {
                _decisions.Remove(DecisionKey(senderId, decision.Sequence - DecisionHistory));
            }
            return decision;
        }

        /// <summary>Finalizes and returns the next canonical frame.</summary>
        public SimCanonicalFrame FinalizeNextFrame(uint epoch)
        {
            int tick = CurrentTick + 1;
            Dictionary<uint, Scheduled> tickCommands;
            _pending.TryGetValue(tick, out tickCommands);

            SimCanonicalFrame frame = new SimCanonicalFrame();
            frame.Epoch = epoch;
            frame.Tick = tick;
            frame.Inputs = new SimCanonicalInput[_playerIds.Length];
            frame.Events = new SimAuthoritativeEvent[0];
            for (int i = 0; i < _playerIds.Length; ++i)
            {
                uint playerId = _playerIds[i];
                Scheduled command;
                if (tickCommands == null || !tickCommands.TryGetValue(playerId, out command))
                {
                    command = _lastCanonical[playerId];
                }
                command.Input.PlayerId = playerId;
                command.Input.Tick = tick;
                _lastCanonical[playerId] = command;

                frame.Inputs[i].Sequence = command.Sequence;
                frame.Inputs[i].RequestedTick = command.RequestedTick;
                frame.Inputs[i].Input = command.Input;
            }

            if (tickCommands != null)
            {
                _pending.Remove(tick);
            }
            CurrentTick = tick;
            return frame;
        }

        /// <summary>Discards the old timeline after an authoritative rebuild.</summary>
        public void Reset(int resumeTick)
        {
            _pending.Clear();
            _decisions.Clear();
            CurrentTick = resumeTick;
            for (int i = 0; i < _playerIds.Length; ++i)
            {
                uint playerId = _playerIds[i];
                Scheduled seed = _lastCanonical[playerId];
                seed.Input.Tick = resumeTick;
                _lastCanonical[playerId] = seed;
            }
        }

        private bool HasScheduled(uint playerId, int tick)
        {
            Dictionary<uint, Scheduled> commands;
            return _pending.TryGetValue(tick, out commands) && commands.ContainsKey(playerId);
        }

        private static ulong DecisionKey(uint playerId, uint sequence)
        {
            return ((ulong)playerId << 32) | sequence;
        }
    }
}
