using System;
using System.Collections.Generic;
using UNDPWR.Core;
using UNDPWR.Diagnostics;
using UNDPWR.Gameplay;
using UNDPWR.Rollback;

namespace UNDPWR.Net
{
    public enum SimClientSessionState
    {
        Connecting,
        Running,
        CatchingUp,
        Resyncing,
        Disconnected
    }

    /// <summary>Predictive client for one authoritative UNDPWR server.</summary>
    public sealed class SimClientSession : IDisposable
    {
        private const uint ResolutionHistory = 512;
        private readonly RollbackEngine _engine;
        private readonly ISimTransport _transport;
        private readonly SimConfig _simulation;
        private readonly SimNetConfig _network;
        private readonly uint _localPlayerId;
        private readonly uint[] _playerIds;
        private readonly SimAdaptiveInputLead _lead;
        private readonly SimActionQueue _actions;
        private readonly SimDesyncDetector _desync = new SimDesyncDetector();
        private readonly Queue<SimInputProposal> _recentProposals = new Queue<SimInputProposal>();
        private readonly HashSet<uint> _resolvedInputs = new HashSet<uint>();
        private readonly Queue<uint> _resolvedInputOrder = new Queue<uint>();
        private readonly HashSet<ulong> _appliedEvents = new HashSet<ulong>();
        private readonly Queue<ulong> _appliedEventOrder = new Queue<ulong>();
        private uint _nextSequence = 1;
        private uint _nextEventSequence = 1;
        private uint _nextPingSequence = 1;
        private uint _epoch;
        private int _lastRequestedTick = -1;
        private int _serverTick;
        private int _lastRecordedLocalHash = -1;
        private long _serverTickSampledAt;
        private long _lastPingAt;
        private long _acceptedInputs;
        private long _retimedInputs;
        private long _rejectedInputs;
        private long _rebuilds;

        public SimClientSessionState State { get; private set; }
        public RollbackEngine Engine { get { return _engine; } }
        public uint Epoch { get { return _epoch; } }

        public SimNetStats Stats
        {
            get
            {
                SimNetStats stats = new SimNetStats();
                stats.SmoothedRttMilliseconds = _lead.SmoothedRttMilliseconds;
                stats.JitterMilliseconds = _lead.JitterMilliseconds;
                stats.InputLeadTicks = _lead.CurrentLead;
                stats.AcceptedInputs = _acceptedInputs;
                stats.RetimedInputs = _retimedInputs;
                stats.RejectedInputs = _rejectedInputs;
                stats.Mispredictions = _engine.TotalMispredictions;
                stats.Rebuilds = _rebuilds;
                return stats;
            }
        }

        public event Action<SimInputProposal> InputAnticipated;
        public event Action<SimInputDecision> InputResolved;
        public event Action<SimEventProposal> EventAnticipated;
        public event Action<SimEventDecision> EventResolved;
        public event Action<SimClientSessionState> StateChanged;

        public SimClientSession(RollbackEngine engine, ISimTransport transport,
            SimConfig simulation, SimNetConfig network, uint localPlayerId, IList<uint> playerIds,
            SimActionQueue actions)
        {
            if (engine == null) throw new ArgumentNullException("engine");
            if (engine.CurrentTick < 0) throw new ArgumentException("Engine must be initialised.", "engine");
            if (transport == null) throw new ArgumentNullException("transport");
            if (simulation == null) throw new ArgumentNullException("simulation");
            if (network == null) throw new ArgumentNullException("network");
            if (playerIds == null) throw new ArgumentNullException("playerIds");
            if (playerIds.Count > SimProtocol.MaxPlayers)
                throw new ArgumentException("Roster exceeds the protocol player limit.", "playerIds");
            if (actions == null) throw new ArgumentNullException("actions");
            if (transport.LocalPeerId != localPlayerId || localPlayerId == SimProtocol.ServerPeerId)
            {
                throw new ArgumentException("Client transport identity must equal the nonzero local player ID.");
            }
            string reason;
            if (!network.Validate(simulation, out reason))
            {
                throw new ArgumentException(reason, "network");
            }
            if (engine.SimulationConfigHash != simulation.ComputeHash()
                || engine.NetworkConfigHash != network.ComputeHash())
            {
                throw new ArgumentException("Session configs do not match the rollback engine.");
            }

            _engine = engine;
            _transport = transport;
            _simulation = simulation;
            _network = network;
            _localPlayerId = localPlayerId;
            _actions = actions;
            _playerIds = new uint[playerIds.Count];
            for (int i = 0; i < playerIds.Count; ++i) _playerIds[i] = playerIds[i];
            Array.Sort(_playerIds);
            uint[] enginePlayers = engine.Inputs.CopyPlayerIds();
            if (!SameSortedRoster(_playerIds, enginePlayers))
            {
                throw new ArgumentException("Session roster does not match the rollback engine.", "playerIds");
            }
            if (Array.BinarySearch(_playerIds, localPlayerId) < 0)
            {
                throw new ArgumentException("The local player is not in the roster.", "localPlayerId");
            }
            _lead = new SimAdaptiveInputLead(network, simulation.TickRate);
            _desync.Fatal = false;
            _desync.DesyncDetected += OnDesync;
            _engine.HardResyncRequired += OnHardResyncRequired;
            State = SimClientSessionState.Connecting;
        }

        public void Start(long nowMicroseconds)
        {
            SimByteWriter writer = new SimByteWriter(32 + _playerIds.Length * 4);
            SimProtocol.WriteHeader(ref writer, SimMessageKind.ClientHello);
            writer.WriteUInt32(_localPlayerId);
            writer.WriteUInt64(_simulation.ComputeHash());
            writer.WriteUInt64(_network.ComputeHash());
            writer.WriteUInt64(_engine.ConstructionHash);
            writer.WriteUInt16((ushort)_playerIds.Length);
            for (int i = 0; i < _playerIds.Length; ++i) writer.WriteUInt32(_playerIds[i]);
            Send(writer.ToArray(), SimDelivery.ReliableOrdered);
            _lastPingAt = nowMicroseconds;
        }

        public void Dispose()
        {
            _desync.DesyncDetected -= OnDesync;
            _engine.HardResyncRequired -= OnHardResyncRequired;
        }

        public void Pump(long nowMicroseconds)
        {
            SimTransportMessage message;
            while (_transport.TryReceive(out message))
            {
                if (message.SenderId != SimProtocol.ServerPeerId
                    || message.Payload.Array == null || message.Payload.Count < 3)
                {
                    continue;
                }
                try
                {
                    SimByteReader reader = new SimByteReader(
                        message.Payload.Array, message.Payload.Offset, message.Payload.Count);
                    SimMessageKind kind = SimProtocol.ReadHeader(ref reader);
                    switch (kind)
                    {
                        case SimMessageKind.ServerHello:
                            ReadServerHello(ref reader, nowMicroseconds);
                            break;
                        case SimMessageKind.InputDecision:
                            ReadInputDecision(ref reader, nowMicroseconds);
                            break;
                        case SimMessageKind.CanonicalFrame:
                            ReadCanonicalFrame(ref reader, nowMicroseconds);
                            break;
                        case SimMessageKind.ClockPong:
                            ReadClockPong(ref reader, nowMicroseconds);
                            break;
                        case SimMessageKind.ServerHash:
                            ReadServerHash(ref reader);
                            break;
                        case SimMessageKind.EventDecision:
                            ReadEventDecision(ref reader);
                            break;
                        case SimMessageKind.Rebuild:
                            ReadRebuild(message.Delivery, ref reader, nowMicroseconds);
                            break;
                        default:
                            SimLog.Warning("Client dropped server message kind " + kind);
                            break;
                    }
                }
                catch (SimWireFormatException ex)
                {
                    SimLog.Warning("Client dropped malformed message: " + ex.Message);
                }
            }

            _lead.Update(nowMicroseconds);
            if (State != SimClientSessionState.Disconnected
                && nowMicroseconds - _lastPingAt >= 500000)
            {
                SendClockPing(nowMicroseconds);
            }
        }

        /// <summary>Speculates locally and sends one future input proposal.</summary>
        public uint SubmitLocalInput(SimInput input, long nowMicroseconds)
        {
            if (State != SimClientSessionState.Running && State != SimClientSessionState.CatchingUp)
            {
                throw new InvalidOperationException("The client is not running.");
            }
            int estimatedServerTick = EstimateServerTick(nowMicroseconds);
            int requestedTick = Math.Max(_lastRequestedTick + 1, estimatedServerTick + _lead.CurrentLead);
            uint sequence = _nextSequence++;
            input.PlayerId = _localPlayerId;
            input.Tick = requestedTick;
            _engine.SubmitSpeculativeInput(input, sequence);

            SimInputProposal proposal = new SimInputProposal();
            proposal.Sequence = sequence;
            proposal.RequestedTick = requestedTick;
            proposal.CapturedAtMicroseconds = nowMicroseconds;
            proposal.Input = input;
            _recentProposals.Enqueue(proposal);
            while (_recentProposals.Count > _network.InputRedundancy)
            {
                _recentProposals.Dequeue();
            }
            foreach (SimInputProposal recentValue in _recentProposals)
            {
                SimInputProposal recent = recentValue;
                Send(SimProtocolCodec.EncodeInputProposal(ref recent), SimDelivery.Unreliable);
            }
            _lastRequestedTick = requestedTick;

            Action<SimInputProposal> handler = InputAnticipated;
            if (handler != null) handler(proposal);
            return sequence;
        }

        /// <summary>Submits a deterministic event and immediately raises its anticipation hook.</summary>
        public uint SubmitEvent(ISimAction action, long nowMicroseconds)
        {
            if (State != SimClientSessionState.Running && State != SimClientSessionState.CatchingUp)
            {
                throw new InvalidOperationException("The client is not running.");
            }
            ushort typeId;
            byte[] payload;
            _actions.EncodeNetworkAction(action, out typeId, out payload);
            SimEventProposal proposal = new SimEventProposal();
            proposal.Sequence = _nextEventSequence++;
            proposal.RequestedTick = Math.Max(
                _lastRequestedTick + 1, EstimateServerTick(nowMicroseconds) + _lead.CurrentLead);
            proposal.TypeId = typeId;
            proposal.Payload = payload;
            byte[] bytes = SimProtocolCodec.EncodeEventProposal(ref proposal);
            Send(bytes, SimDelivery.ReliableOrdered);

            Action<SimEventProposal> handler = EventAnticipated;
            if (handler != null) handler(proposal);
            return proposal.Sequence;
        }

        /// <summary>Advances the budgeted prediction/reconciliation engine by one Unity tick.</summary>
        public bool Advance()
        {
            if (State == SimClientSessionState.Connecting
                || State == SimClientSessionState.Resyncing
                || State == SimClientSessionState.Disconnected)
            {
                return false;
            }
            bool advanced = _engine.Advance();
            RecordLocalHashes();
            if (_engine.NeedsHardResync)
            {
                RequestRebuild();
            }
            else if (_engine.IsCatchingUp)
            {
                SetState(SimClientSessionState.CatchingUp);
            }
            else if (State != SimClientSessionState.Connecting)
            {
                SetState(SimClientSessionState.Running);
            }
            return advanced;
        }

        private void ReadServerHello(ref SimByteReader reader, long nowMicroseconds)
        {
            bool accepted = reader.ReadByte() != 0;
            uint epoch = reader.ReadUInt32();
            int serverTick = reader.ReadInt32();
            ulong simulationHash = reader.ReadUInt64();
            ulong networkHash = reader.ReadUInt64();
            ulong constructionHash = reader.ReadUInt64();
            if (!accepted || simulationHash != _simulation.ComputeHash()
                || networkHash != _network.ComputeHash()
                || constructionHash != _engine.ConstructionHash)
            {
                SetState(SimClientSessionState.Disconnected);
                return;
            }
            _epoch = epoch;
            _serverTick = serverTick;
            _serverTickSampledAt = nowMicroseconds;
            SetState(SimClientSessionState.Running);
        }

        private void ReadInputDecision(ref SimByteReader reader, long nowMicroseconds)
        {
            SimInputDecision decision = SimProtocolCodec.ReadInputDecision(ref reader);
            if (decision.PlayerId != _localPlayerId)
            {
                return;
            }
            if (!_resolvedInputs.Add(decision.Sequence))
            {
                return;
            }
            _resolvedInputOrder.Enqueue(decision.Sequence);
            while (_resolvedInputOrder.Count > ResolutionHistory)
            {
                _resolvedInputs.Remove(_resolvedInputOrder.Dequeue());
            }
            _lead.RecordDecision(decision.Disposition, nowMicroseconds);
            if (decision.Disposition == SimAdmissionDisposition.Accepted)
            {
                _acceptedInputs += 1;
            }
            else if (decision.Disposition == SimAdmissionDisposition.Retimed)
            {
                _retimedInputs += 1;
                _engine.RetimeSpeculativeInput(
                    _localPlayerId, decision.Sequence, decision.RequestedTick, decision.AssignedTick);
            }
            else
            {
                _rejectedInputs += 1;
                _engine.RejectSpeculativeInput(_localPlayerId, decision.Sequence, decision.RequestedTick);
            }
            Action<SimInputDecision> handler = InputResolved;
            if (handler != null) handler(decision);
        }

        private void ReadCanonicalFrame(ref SimByteReader reader, long nowMicroseconds)
        {
            SimCanonicalFrame frame = SimProtocolCodec.ReadCanonicalFrame(ref reader);
            if (frame.Epoch != _epoch || frame.Tick <= _engine.ConfirmedTick)
            {
                return;
            }
            SimAuthoritativeEvent[] events = frame.Events ?? new SimAuthoritativeEvent[0];
            for (int i = 0; i < events.Length; ++i)
            {
                _engine.SubmitAuthoritativeEvent(events[i]);
            }
            _engine.SubmitAuthoritativeFrame(frame);
            if (frame.Tick > _serverTick)
            {
                _serverTick = frame.Tick;
                _serverTickSampledAt = nowMicroseconds;
            }
        }

        private void SendClockPing(long nowMicroseconds)
        {
            SimByteWriter writer = new SimByteWriter(20);
            SimProtocol.WriteHeader(ref writer, SimMessageKind.ClockPing);
            writer.WriteUInt32(_nextPingSequence++);
            writer.WriteUInt64(unchecked((ulong)nowMicroseconds));
            Send(writer.ToArray(), SimDelivery.Unreliable);
            _lastPingAt = nowMicroseconds;
        }

        private void ReadClockPong(ref SimByteReader reader, long nowMicroseconds)
        {
            reader.ReadUInt32();
            long sentAt = unchecked((long)reader.ReadUInt64());
            reader.ReadUInt64();
            int serverTick = reader.ReadInt32();
            _lead.RecordRtt(nowMicroseconds - sentAt);
            if (serverTick >= _serverTick)
            {
                _serverTick = serverTick;
                _serverTickSampledAt = nowMicroseconds;
            }
        }

        private void ReadServerHash(ref SimByteReader reader)
        {
            uint epoch = reader.ReadUInt32();
            int tick = reader.ReadInt32();
            SimStateHashes hashes = new SimStateHashes(
                reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64());
            if (epoch == _epoch)
            {
                _desync.RecordPeer(SimProtocol.ServerPeerId, tick, hashes);
            }
        }

        private void ReadEventDecision(ref SimByteReader reader)
        {
            SimEventDecision decision = SimProtocolCodec.ReadEventDecision(ref reader);
            ulong key = ((ulong)decision.PlayerId << 32) | decision.Sequence;
            if (!_appliedEvents.Add(key))
            {
                return;
            }
            _appliedEventOrder.Enqueue(key);
            while (_appliedEventOrder.Count > ResolutionHistory)
            {
                _appliedEvents.Remove(_appliedEventOrder.Dequeue());
            }
            if (decision.Disposition != SimAdmissionDisposition.Rejected)
            {
                SimAuthoritativeEvent command = new SimAuthoritativeEvent();
                command.PlayerId = decision.PlayerId;
                command.Sequence = decision.Sequence;
                command.Tick = decision.AssignedTick;
                command.TypeId = decision.TypeId;
                command.Payload = decision.Payload;
                _engine.SubmitAuthoritativeEvent(command);
                if (_engine.NeedsHardResync)
                {
                    RequestRebuild();
                }
            }
            if (decision.PlayerId == _localPlayerId)
            {
                Action<SimEventDecision> handler = EventResolved;
                if (handler != null) handler(decision);
            }
        }

        private void RecordLocalHashes()
        {
            for (int tick = _lastRecordedLocalHash + 1; tick <= _engine.ConfirmedTick; ++tick)
            {
                Snapshot snapshot;
                if (_engine.TryGetConfirmedSnapshot(tick, out snapshot))
                {
                    _desync.RecordLocal(tick, snapshot.Hashes);
                    _lastRecordedLocalHash = tick;
                }
            }
        }

        private void OnDesync(SimDesyncReport report)
        {
            RequestRebuild();
        }

        private void OnHardResyncRequired(string reason)
        {
            RequestRebuild();
        }

        private void RequestRebuild()
        {
            if (State == SimClientSessionState.Resyncing
                || State == SimClientSessionState.Disconnected
                || _epoch == 0)
            {
                return;
            }
            SimByteWriter writer = new SimByteWriter(12);
            SimProtocol.WriteHeader(ref writer, SimMessageKind.RebuildRequest);
            writer.WriteUInt32(_epoch);
            Send(writer.ToArray(), SimDelivery.ReliableOrdered);
            SetState(SimClientSessionState.Resyncing);
        }

        private void ReadRebuild(SimDelivery delivery, ref SimByteReader reader, long nowMicroseconds)
        {
            if (delivery != SimDelivery.ReliableOrdered)
            {
                return;
            }
            SimRebuildState state = SimRebuildCodec.ReadBody(ref reader);
            _engine.PrepareForRebuild(ref state);
            _recentProposals.Clear();
            _resolvedInputs.Clear();
            _resolvedInputOrder.Clear();
            _appliedEvents.Clear();
            _appliedEventOrder.Clear();
            _lastRequestedTick = state.ResumeTick;
            _lastRecordedLocalHash = state.ResumeTick;
            _serverTick = state.ResumeTick;
            _serverTickSampledAt = nowMicroseconds;
            _rebuilds += 1;
            SetState(SimClientSessionState.Running);
        }

        private int EstimateServerTick(long nowMicroseconds)
        {
            long elapsed = Math.Max(0, nowMicroseconds - _serverTickSampledAt);
            return _serverTick + (int)(elapsed * _simulation.TickRate / 1000000L);
        }

        private void SetState(SimClientSessionState state)
        {
            if (State == state) return;
            State = state;
            Action<SimClientSessionState> handler = StateChanged;
            if (handler != null) handler(state);
        }

        private void Send(byte[] bytes, SimDelivery delivery)
        {
            _transport.Send(SimProtocol.ServerPeerId, bytes, 0, bytes.Length, delivery);
        }

        private static bool SameSortedRoster(uint[] a, uint[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; ++i)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }
    }
}
