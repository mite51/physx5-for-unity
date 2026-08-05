using System;
using System.Collections.Generic;
using UNDPWR.Core;
using UNDPWR.Diagnostics;
using UNDPWR.Rollback;

namespace UNDPWR.Net
{
    /// <summary>
    /// The outcome of comparing a peer's join handshake against ours.
    /// </summary>
    public struct SimHandshakeResult
    {
        /// <summary>The peer that sent the handshake.</summary>
        public uint PeerId;

        /// <summary>True when the peer's config and player set match ours.</summary>
        public bool Accepted;

        /// <summary>Why the handshake was rejected, or null when accepted.</summary>
        public string Reason;
    }

    /// <summary>
    /// Ties a <see cref="RollbackEngine"/> to an <see cref="ISimTransport"/>: it sends this
    /// peer's inputs, feeds arriving inputs into the engine, agrees the session at join, and
    /// exchanges confirmed-tick hashes so a divergence is caught.
    /// </summary>
    /// <remarks>
    /// The framework keeps the fixed-update loop in the caller's hands, so a session does not
    /// call <see cref="RollbackEngine.Advance"/> itself. A frame looks like:
    /// <code>
    /// session.Pump();                     // drain the network into the engine
    /// session.SubmitLocalInput(input);    // stamped for engine.LocalInputTick
    /// engine.Advance();                   // one simulated tick
    /// session.PublishConfirmed();         // broadcast and check the new confirmed hash
    /// </code>
    /// <para>
    /// Two invariants the session depends on. Every peer constructs its engine with the same
    /// player-ID set, so slot order agrees. And every peer computes the same
    /// <see cref="SimConfig.ComputeHash"/>; the handshake refuses a peer whose hash differs,
    /// so a PGS/TGS or horizon mismatch is a clean rejection at join rather than a desync
    /// discovered mid-match.
    /// </para>
    /// Inputs are re-sent in a small redundancy window every frame, so a lost datagram is
    /// recovered by the next one without the transport having to be reliable.
    /// </remarks>
    public sealed class SimSession
    {
        private readonly RollbackEngine _engine;
        private readonly ISimTransport _transport;
        private readonly SimDesyncDetector _detector;
        private readonly ulong _configHash;
        private readonly uint _localPlayerId;
        private readonly uint[] _playerIds;

        private readonly Queue<SimInput> _recentLocal = new Queue<SimInput>();
        private readonly HashSet<uint> _acceptedPeers = new HashSet<uint>();
        private int _lastPublishedTick = -1;

        /// <summary>How many recent local inputs are re-sent each frame for loss recovery.</summary>
        public int RedundancyWindow { get; set; }

        /// <summary>Publish and check a confirmed hash every this-many ticks. One means every tick.</summary>
        public int HashInterval { get; set; }

        /// <summary>The desync detector this session feeds. Never null.</summary>
        public SimDesyncDetector Desync { get { return _detector; } }

        /// <summary>Raised for each peer handshake received, accepted or not.</summary>
        public event Action<SimHandshakeResult> HandshakeReceived;

        /// <summary>
        /// Creates a session over an engine and a transport.
        /// </summary>
        /// <param name="engine">The rollback engine this peer drives.</param>
        /// <param name="transport">How messages reach the other peers.</param>
        /// <param name="config">The config every peer must agree on.</param>
        /// <param name="localPlayerId">This peer's player ID.</param>
        /// <param name="playerIds">Every player in the session, the same set on every peer.</param>
        /// <param name="detector">The desync detector to feed, or null to create a default one.</param>
        public SimSession(
            RollbackEngine engine,
            ISimTransport transport,
            SimConfig config,
            uint localPlayerId,
            IList<uint> playerIds,
            SimDesyncDetector detector = null)
        {
            if (engine == null)
            {
                throw new ArgumentNullException("engine");
            }
            if (transport == null)
            {
                throw new ArgumentNullException("transport");
            }
            if (playerIds == null)
            {
                throw new ArgumentNullException("playerIds");
            }

            _engine = engine;
            _transport = transport;
            _detector = detector ?? new SimDesyncDetector();
            _configHash = config.ComputeHash();
            _localPlayerId = localPlayerId;

            // Conditional rollback and the free-running clock both remove the fixed horizon
            // that was the session's safety net: a peer that rewinds a data-dependent depth,
            // or runs a data-dependent-length window, no longer has the identical-sequence
            // property to fall back on, only PGS transparency. That has to be verified rather
            // than assumed, so confirmed-hash detection becomes mandatory and fatal the moment
            // either flag is set. AdaptiveRollbackPlan.md §5-6.
            if (config.ConditionalRollback || config.FreeRunningClock)
            {
                _detector.Fatal = true;
            }

            _playerIds = new uint[playerIds.Count];
            for (int i = 0; i < playerIds.Count; ++i)
            {
                _playerIds[i] = playerIds[i];
            }
            Array.Sort(_playerIds);

            RedundancyWindow = 4;
            HashInterval = 1;
        }

        /// <summary>The rollback engine this session drives.</summary>
        public RollbackEngine Engine { get { return _engine; } }

        /// <summary>
        /// Announces this peer to the others: broadcasts the config hash and player set so a
        /// mismatch is refused before a tick runs. Call once, after construction.
        /// </summary>
        public void Start()
        {
            SimByteWriter writer = new SimByteWriter(16 + _playerIds.Length * 4);
            writer.WriteByte((byte)SimMessageKind.Handshake);
            writer.WriteUInt64(_configHash);
            writer.WriteUInt32(_localPlayerId);
            writer.WriteUInt16((ushort)_playerIds.Length);
            for (int i = 0; i < _playerIds.Length; ++i)
            {
                writer.WriteUInt32(_playerIds[i]);
            }
            SendCopy(ref writer);
        }

        /// <summary>
        /// Submits a local input to the engine and broadcasts it — with a few recent inputs
        /// behind it — to the other peers.
        /// </summary>
        public void SubmitLocalInput(SimInput input)
        {
            input.PlayerId = _localPlayerId;
            _engine.SubmitInput(input);

            _recentLocal.Enqueue(input);
            int keep = RedundancyWindow < 1 ? 1 : RedundancyWindow;
            while (_recentLocal.Count > keep)
            {
                _recentLocal.Dequeue();
            }

            SimByteWriter writer = new SimByteWriter(4 + _recentLocal.Count * SimInputCodec.Size);
            writer.WriteByte((byte)SimMessageKind.Input);
            writer.WriteUInt16((ushort)_recentLocal.Count);
            foreach (SimInput recent in _recentLocal)
            {
                SimInputCodec.Write(ref writer, recent);
            }
            SendCopy(ref writer);
        }

        /// <summary>
        /// Drains every message waiting on the transport: remote inputs into the engine, peer
        /// hashes into the detector, and handshakes into the acceptance check.
        /// </summary>
        public void Pump()
        {
            ArraySegment<byte> message;
            while (_transport.TryReceive(out message))
            {
                byte[] array = message.Array;
                if (array == null || message.Count < 1)
                {
                    continue;
                }
                try
                {
                    Dispatch(array, message.Offset, message.Count);
                }
                catch (SimWireFormatException ex)
                {
                    SimLog.Warning("Dropping malformed message: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Publishes this peer's combined hash for each newly confirmed tick and checks it
        /// against any peer hashes already received. Call once per frame, after
        /// <see cref="RollbackEngine.Advance"/>.
        /// </summary>
        public void PublishConfirmed()
        {
            int confirmed = _engine.ConfirmedTick;
            int interval = HashInterval < 1 ? 1 : HashInterval;

            for (int tick = _lastPublishedTick + 1; tick <= confirmed; ++tick)
            {
                _lastPublishedTick = tick;
                if (tick % interval != 0)
                {
                    continue;
                }

                Snapshot snapshot;
                if (!_engine.TryGetConfirmedSnapshot(tick, out snapshot))
                {
                    continue;
                }

                ulong hash = snapshot.CombinedHash;
                _detector.RecordLocal(tick, hash);

                SimByteWriter writer = new SimByteWriter(20);
                writer.WriteByte((byte)SimMessageKind.Hash);
                writer.WriteUInt32(_localPlayerId);
                writer.WriteInt32(tick);
                writer.WriteUInt64(hash);
                SendCopy(ref writer);
            }
        }

        private void Dispatch(byte[] array, int offset, int count)
        {
            SimByteReader reader = new SimByteReader(array, offset, count);
            SimMessageKind kind = (SimMessageKind)reader.ReadByte();
            switch (kind)
            {
                case SimMessageKind.Input:
                    ReadInputs(ref reader);
                    break;
                case SimMessageKind.Hash:
                    ReadHash(ref reader);
                    break;
                case SimMessageKind.Handshake:
                    ReadHandshake(ref reader);
                    break;
                default:
                    SimLog.Warning("Dropping message with unknown kind " + (byte)kind);
                    break;
            }
        }

        private void ReadInputs(ref SimByteReader reader)
        {
            int countOfInputs = reader.ReadUInt16();
            for (int i = 0; i < countOfInputs; ++i)
            {
                SimInput input = SimInputCodec.Read(ref reader);
                // A peer must not be able to move another player. Drop anything claiming to be
                // our own input, too — ours is authoritative locally.
                if (input.PlayerId == _localPlayerId)
                {
                    continue;
                }
                _engine.SubmitInput(input);
            }
        }

        private void ReadHash(ref SimByteReader reader)
        {
            uint senderId = reader.ReadUInt32();
            int tick = reader.ReadInt32();
            ulong hash = reader.ReadUInt64();
            _detector.RecordPeer(senderId, tick, hash);
        }

        private void ReadHandshake(ref SimByteReader reader)
        {
            ulong peerConfigHash = reader.ReadUInt64();
            uint peerId = reader.ReadUInt32();
            int peerPlayerCount = reader.ReadUInt16();

            uint[] peerPlayers = new uint[peerPlayerCount];
            for (int i = 0; i < peerPlayerCount; ++i)
            {
                peerPlayers[i] = reader.ReadUInt32();
            }

            string reason = null;
            if (peerConfigHash != _configHash)
            {
                reason = string.Format("config hash mismatch: local {0:X16}, peer {1:X16}",
                    _configHash, peerConfigHash);
            }
            else if (!SamePlayers(peerPlayers))
            {
                reason = "player set mismatch";
            }

            SimHandshakeResult result = new SimHandshakeResult();
            result.PeerId = peerId;
            result.Accepted = reason == null;
            result.Reason = reason;

            if (result.Accepted)
            {
                _acceptedPeers.Add(peerId);
            }
            else
            {
                SimLog.Error(string.Format("Rejecting peer {0}: {1}", peerId, reason));
            }

            Action<SimHandshakeResult> handler = HandshakeReceived;
            if (handler != null)
            {
                handler(result);
            }
        }

        private bool SamePlayers(uint[] peerPlayers)
        {
            if (peerPlayers.Length != _playerIds.Length)
            {
                return false;
            }
            Array.Sort(peerPlayers);
            for (int i = 0; i < _playerIds.Length; ++i)
            {
                if (peerPlayers[i] != _playerIds[i])
                {
                    return false;
                }
            }
            return true;
        }

        private void SendCopy(ref SimByteWriter writer)
        {
            byte[] bytes = writer.ToArray();
            _transport.Broadcast(bytes, 0, bytes.Length);
        }
    }
}
