using System;
using System.Collections.Generic;
using UNDPWR.Core;
using UNDPWR.Diagnostics;
using UNDPWR.Gameplay;
using UNDPWR.Rollback;

namespace UNDPWR.Net
{
    /// <summary>Runs the canonical command timeline and authoritative deterministic world.</summary>
    public sealed class SimServerSession
    {
        private const uint EventDecisionHistory = 256;
        private readonly RollbackEngine _engine;
        private readonly ISimTransport _transport;
        private readonly SimConfig _simulation;
        private readonly SimNetConfig _network;
        private readonly SimInputScheduler _scheduler;
        private readonly SimActionQueue _actions;
        private readonly uint[] _playerIds;
        private readonly HashSet<uint> _clients = new HashSet<uint>();
        private readonly Queue<byte[]> _recentFrames = new Queue<byte[]>();
        private readonly Dictionary<ulong, SimEventDecision> _eventDecisions =
            new Dictionary<ulong, SimEventDecision>();
        private readonly Dictionary<uint, uint> _lastEventSequence = new Dictionary<uint, uint>();
        private readonly Dictionary<uint, uint> _localInputSequence = new Dictionary<uint, uint>();
        private int _lastPublishedHash = -1;

        public uint Epoch { get; private set; }
        public RollbackEngine Engine { get { return _engine; } }

        public event Action<uint> ClientAccepted;
        public event Action<uint> ClientRejected;

        public SimServerSession(RollbackEngine engine, ISimTransport transport,
            SimConfig simulation, SimNetConfig network, IList<uint> playerIds, SimActionQueue actions)
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
            if (transport.LocalPeerId != SimProtocol.ServerPeerId)
            {
                throw new ArgumentException("The authoritative server transport must use peer ID 0.", "transport");
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
            _actions = actions;
            _playerIds = new uint[playerIds.Count];
            for (int i = 0; i < playerIds.Count; ++i) _playerIds[i] = playerIds[i];
            Array.Sort(_playerIds);
            uint[] enginePlayers = engine.Inputs.CopyPlayerIds();
            if (!SameSortedRoster(_playerIds, enginePlayers))
            {
                throw new ArgumentException("Session roster does not match the rollback engine.", "playerIds");
            }
            for (int i = 0; i < _playerIds.Length; ++i)
            {
                _lastEventSequence[_playerIds[i]] = 0;
                _localInputSequence[_playerIds[i]] = 0;
            }
            _scheduler = new SimInputScheduler(_playerIds, engine.CurrentTick, network.ServerMaxFutureTicks);
            Epoch = 1;
        }

        /// <summary>The next server tick available for an in-process host command.</summary>
        public int NextInputTick { get { return _scheduler.CurrentTick + 1; } }

        /// <summary>
        /// Schedules input sampled in the authoritative process without routing it through a
        /// client transport. This is the host-player and local-simulation path.
        /// </summary>
        public SimInputDecision SubmitLocalInput(SimInput input)
        {
            uint sequence;
            if (!_localInputSequence.TryGetValue(input.PlayerId, out sequence))
            {
                throw new ArgumentException("The input player is not in the server roster.", "input");
            }
            sequence += 1;
            _localInputSequence[input.PlayerId] = sequence;
            input.Tick = NextInputTick;
            SimInputProposal proposal = new SimInputProposal();
            proposal.Sequence = sequence;
            proposal.RequestedTick = input.Tick;
            proposal.Input = input;
            return _scheduler.Submit(input.PlayerId, ref proposal);
        }

        /// <summary>Drains client proposals and control traffic.</summary>
        public void Pump(long nowMicroseconds)
        {
            SimTransportMessage message;
            while (_transport.TryReceive(out message))
            {
                ArraySegment<byte> payload = message.Payload;
                if (payload.Array == null || payload.Count < 3)
                {
                    continue;
                }
                try
                {
                    SimByteReader reader = new SimByteReader(payload.Array, payload.Offset, payload.Count);
                    SimMessageKind kind = SimProtocol.ReadHeader(ref reader);
                    switch (kind)
                    {
                        case SimMessageKind.ClientHello:
                            ReadClientHello(message.SenderId, message.Delivery, ref reader);
                            break;
                        case SimMessageKind.InputProposal:
                            ReadInputProposal(message.SenderId, ref reader);
                            break;
                        case SimMessageKind.ClockPing:
                            ReadClockPing(message.SenderId, nowMicroseconds, ref reader);
                            break;
                        case SimMessageKind.EventProposal:
                            ReadEventProposal(message.SenderId, message.Delivery, ref reader);
                            break;
                        case SimMessageKind.RebuildRequest:
                            ReadRebuildRequest(message.SenderId, message.Delivery, ref reader);
                            break;
                        default:
                            SimLog.Warning("Server dropped client message kind " + kind);
                            break;
                    }
                }
                catch (SimWireFormatException ex)
                {
                    SimLog.Warning("Server dropped malformed message: " + ex.Message);
                }
            }
        }

        /// <summary>Finalizes one server tick, simulates it, and publishes authority.</summary>
        public bool Advance()
        {
            SimCanonicalFrame frame = _scheduler.FinalizeNextFrame(Epoch);
            frame.Events = _engine.CopyAuthoritativeEvents(frame.Tick);
            _engine.SubmitAuthoritativeFrame(frame);
            PublishCanonical(frame);
            bool advanced = _engine.Advance();
            PublishHashes();
            return advanced;
        }

        private void ReadClientHello(uint senderId, SimDelivery delivery, ref SimByteReader reader)
        {
            bool accepted = delivery == SimDelivery.ReliableOrdered;
            uint claimedId = reader.ReadUInt32();
            ulong simulationHash = reader.ReadUInt64();
            ulong networkHash = reader.ReadUInt64();
            ulong constructionHash = reader.ReadUInt64();
            int count = reader.ReadUInt16();
            if (count > SimProtocol.MaxPlayers)
            {
                throw new SimWireFormatException("client roster exceeds the player limit");
            }
            uint[] roster = new uint[count];
            for (int i = 0; i < count; ++i) roster[i] = reader.ReadUInt32();

            accepted = accepted
                && claimedId == senderId
                && senderId != SimProtocol.ServerPeerId
                && Array.BinarySearch(_playerIds, senderId) >= 0
                && simulationHash == _simulation.ComputeHash()
                && networkHash == _network.ComputeHash()
                && constructionHash == _engine.ConstructionHash
                && SameRoster(roster);
            if (accepted)
            {
                _clients.Add(senderId);
            }
            SendServerHello(senderId, accepted);
            if (accepted)
            {
                SendRebuild(senderId);
            }

            Action<uint> handler = accepted ? ClientAccepted : ClientRejected;
            if (handler != null) handler(senderId);
        }

        private void SendServerHello(uint clientId, bool accepted)
        {
            SimByteWriter writer = new SimByteWriter(32);
            SimProtocol.WriteHeader(ref writer, SimMessageKind.ServerHello);
            writer.WriteByte(accepted ? (byte)1 : (byte)0);
            writer.WriteUInt32(Epoch);
            writer.WriteInt32(_scheduler.CurrentTick);
            writer.WriteUInt64(_simulation.ComputeHash());
            writer.WriteUInt64(_network.ComputeHash());
            writer.WriteUInt64(_engine.ConstructionHash);
            Send(clientId, writer.ToArray(), SimDelivery.ReliableOrdered);
        }

        private void ReadInputProposal(uint senderId, ref SimByteReader reader)
        {
            if (!_clients.Contains(senderId))
            {
                return;
            }
            SimInputProposal proposal = SimProtocolCodec.ReadInputProposal(ref reader);
            SimInputDecision decision = _scheduler.Submit(senderId, ref proposal);
            byte[] bytes = SimProtocolCodec.EncodeInputDecision(ref decision);
            Send(senderId, bytes, SimDelivery.ReliableOrdered);
        }

        private void ReadClockPing(uint senderId, long nowMicroseconds, ref SimByteReader reader)
        {
            if (!_clients.Contains(senderId))
            {
                return;
            }
            uint sequence = reader.ReadUInt32();
            ulong clientSent = reader.ReadUInt64();
            SimByteWriter writer = new SimByteWriter(32);
            SimProtocol.WriteHeader(ref writer, SimMessageKind.ClockPong);
            writer.WriteUInt32(sequence);
            writer.WriteUInt64(clientSent);
            writer.WriteUInt64(unchecked((ulong)nowMicroseconds));
            writer.WriteInt32(_scheduler.CurrentTick);
            Send(senderId, writer.ToArray(), SimDelivery.Unreliable);
        }

        private void ReadEventProposal(uint senderId, SimDelivery delivery, ref SimByteReader reader)
        {
            if (!_clients.Contains(senderId) || delivery != SimDelivery.ReliableOrdered)
            {
                return;
            }
            SimEventProposal proposal = SimProtocolCodec.ReadEventProposal(ref reader);
            ulong key = ((ulong)senderId << 32) | proposal.Sequence;
            SimEventDecision cached;
            if (_eventDecisions.TryGetValue(key, out cached))
            {
                Send(senderId, SimProtocolCodec.EncodeEventDecision(ref cached), SimDelivery.ReliableOrdered);
                return;
            }

            SimEventDecision decision = new SimEventDecision();
            decision.PlayerId = senderId;
            decision.Sequence = proposal.Sequence;
            decision.RequestedTick = proposal.RequestedTick;
            decision.AssignedTick = -1;
            decision.Disposition = SimAdmissionDisposition.Rejected;
            decision.TypeId = proposal.TypeId;
            decision.Payload = proposal.Payload;

            uint previous;
            if (!_lastEventSequence.TryGetValue(senderId, out previous))
            {
                decision.Rejection = SimAdmissionRejection.UnknownPlayer;
            }
            else if (proposal.Sequence == 0 || proposal.Sequence <= previous)
            {
                decision.Rejection = SimAdmissionRejection.DuplicateSequence;
            }
            else if (proposal.RequestedTick > _scheduler.CurrentTick + _network.ServerMaxFutureTicks)
            {
                decision.Rejection = SimAdmissionRejection.TooFarInFuture;
            }
            else
            {
                try
                {
                    _actions.ValidateNetworkAction(decision.TypeId, decision.Payload);
                    decision.AssignedTick = Math.Max(proposal.RequestedTick, _scheduler.CurrentTick + 1);
                    decision.Disposition = decision.AssignedTick == proposal.RequestedTick
                        ? SimAdmissionDisposition.Accepted
                        : SimAdmissionDisposition.Retimed;
                    decision.Rejection = SimAdmissionRejection.None;
                    _lastEventSequence[senderId] = proposal.Sequence;

                    SimAuthoritativeEvent command = new SimAuthoritativeEvent();
                    command.PlayerId = senderId;
                    command.Sequence = proposal.Sequence;
                    command.Tick = decision.AssignedTick;
                    command.TypeId = decision.TypeId;
                    command.Payload = decision.Payload;
                    _engine.SubmitAuthoritativeEvent(command);
                }
                catch (Exception ex)
                {
                    decision.Rejection = SimAdmissionRejection.Malformed;
                    SimLog.Warning("Rejected malformed event proposal: " + ex.Message);
                }
            }

            _eventDecisions[key] = decision;
            if (_lastEventSequence.ContainsKey(senderId) && proposal.Sequence > previous)
            {
                _lastEventSequence[senderId] = proposal.Sequence;
            }
            if (proposal.Sequence > EventDecisionHistory)
            {
                ulong staleKey = ((ulong)senderId << 32)
                    | (proposal.Sequence - EventDecisionHistory);
                _eventDecisions.Remove(staleKey);
            }
            byte[] bytes = SimProtocolCodec.EncodeEventDecision(ref decision);
            if (decision.Disposition == SimAdmissionDisposition.Rejected)
            {
                Send(senderId, bytes, SimDelivery.ReliableOrdered);
            }
            else
            {
                foreach (uint clientId in _clients)
                {
                    Send(clientId, bytes, SimDelivery.ReliableOrdered);
                }
            }
        }

        private void ReadRebuildRequest(uint senderId, SimDelivery delivery, ref SimByteReader reader)
        {
            if (!_clients.Contains(senderId) || delivery != SimDelivery.ReliableOrdered)
            {
                return;
            }
            uint requestEpoch = reader.ReadUInt32();
            if (requestEpoch != Epoch)
            {
                return;
            }
            SendRebuild(senderId);
        }

        private void SendRebuild(uint clientId)
        {
            SimRebuildState state;
            if (!_engine.CaptureRebuildState(_engine.ConfirmedTick, out state))
            {
                return;
            }
            byte[] bytes = SimRebuildCodec.Encode(ref state);
            Send(clientId, bytes, SimDelivery.ReliableOrdered);
        }

        private void PublishCanonical(SimCanonicalFrame frame)
        {
            byte[] newest = SimProtocolCodec.EncodeCanonicalFrame(frame);
            _recentFrames.Enqueue(newest);
            while (_recentFrames.Count > _network.CanonicalFrameRedundancy)
            {
                _recentFrames.Dequeue();
            }
            foreach (uint clientId in _clients)
            {
                foreach (byte[] bytes in _recentFrames)
                {
                    Send(clientId, bytes, SimDelivery.Unreliable);
                }
            }
        }

        private void PublishHashes()
        {
            for (int tick = _lastPublishedHash + 1; tick <= _engine.ConfirmedTick; ++tick)
            {
                Snapshot snapshot;
                if (!_engine.TryGetConfirmedSnapshot(tick, out snapshot))
                {
                    continue;
                }
                _lastPublishedHash = tick;
                SimByteWriter writer = new SimByteWriter(40);
                SimProtocol.WriteHeader(ref writer, SimMessageKind.ServerHash);
                writer.WriteUInt32(Epoch);
                writer.WriteInt32(tick);
                writer.WriteUInt64(snapshot.Hashes.Physics);
                writer.WriteUInt64(snapshot.Hashes.Entity);
                writer.WriteUInt64(snapshot.Hashes.Game);
                byte[] bytes = writer.ToArray();
                foreach (uint clientId in _clients)
                {
                    Send(clientId, bytes, SimDelivery.Unreliable);
                }
            }
        }

        private bool SameRoster(uint[] roster)
        {
            if (roster.Length != _playerIds.Length) return false;
            Array.Sort(roster);
            for (int i = 0; i < roster.Length; ++i)
            {
                if (roster[i] != _playerIds[i]) return false;
            }
            return true;
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

        private void Send(uint recipientId, byte[] bytes, SimDelivery delivery)
        {
            _transport.Send(recipientId, bytes, 0, bytes.Length, delivery);
        }
    }
}
