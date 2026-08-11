using System;
using System.Collections.Generic;
using System.Text;
using UNDPWR.Core;
using UNDPWR.Diagnostics;
using UNDPWR.Interop;
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
    /// so a solver or other config mismatch is a clean rejection at join rather than a desync
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
        private readonly SimConfig _config;
        private readonly ulong _configHash;
        private readonly uint _localPlayerId;
        private uint[] _playerIds;

        private readonly Queue<SimInput> _recentLocal = new Queue<SimInput>();

        // The oldest tick the local player has not stamped yet. SubmitLocalInput fills from here
        // so the local stream never has a hole in it.
        private int _nextLocalTick;
        private readonly HashSet<uint> _acceptedPeers = new HashSet<uint>();
        private int _lastPublishedTick = -1;

        // The registration table is exchanged after the first confirmed step assigns the PhysX
        // indices, and again after each rebuild changes the body set. Sent on a few consecutive
        // confirmed ticks rather than once, because the transport is lossy and a dropped table
        // would skip the check for the whole session; a resend only ever repeats a log line when
        // a mismatch is real and persistent.
        private const int InternalIdRedundancy = 3;
        private int _internalIdSendsLeft = InternalIdRedundancy;

        // Peer registration tables waiting to be checked. Held rather than compared on arrival
        // because a peer may send its table while this peer's own world has not yet taken the
        // first step that assigns the local actor indices; the comparison runs from
        // PublishConfirmed, where the local step is guaranteed to have happened.
        private readonly Dictionary<uint, SimInternalIdEntry[]> _pendingPeerIds =
            new Dictionary<uint, SimInternalIdEntry[]>();

        // Peers whose registration order has been confirmed to match, so the confirmation is
        // logged once rather than on every resend.
        private readonly HashSet<uint> _registrationConfirmed = new HashSet<uint>();

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
            _detector.DesyncDetected += LogDivergedEntities;
            _config = config;
            _configHash = config.ComputeHash();
            _localPlayerId = localPlayerId;

            // The engine rewinds a data-dependent depth and runs a data-dependent-length
            // window, so there is no fixed identical-sequence property to fall back on, only
            // PGS transparency. That has to be verified rather than assumed, so confirmed-hash
            // detection is mandatory and fatal. AdaptiveRollbackPlan.md §5-6.
            _detector.Fatal = true;

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

        /// <summary>This peer's player ID.</summary>
        public uint LocalPlayerId { get { return _localPlayerId; } }

        /// <summary>A sorted copy of the roster this session currently expects.</summary>
        public uint[] CopyRoster()
        {
            uint[] copy = new uint[_playerIds.Length];
            Array.Copy(_playerIds, copy, _playerIds.Length);
            return copy;
        }

        /// <summary>
        /// Replaces the session's expected roster after a synchronised rebuild changed it, and
        /// re-announces this peer so the handshake reflects the new player set.
        /// </summary>
        /// <remarks>
        /// The engine's own roster is replaced inside
        /// <see cref="RollbackEngine.PrepareForRebuild(ref SimRebuildState, bool)"/>; this keeps
        /// the session's copy — the one the handshake compares against
        /// (<see cref="SamePlayers"/>) — in step, so a peer that joined mid-match is no longer
        /// rejected for a player-set mismatch. Call after applying the rebuild.
        /// </remarks>
        public void ReplaceRoster(IList<uint> playerIds)
        {
            if (playerIds == null)
            {
                throw new ArgumentNullException("playerIds");
            }
            _playerIds = new uint[playerIds.Count];
            for (int i = 0; i < playerIds.Count; ++i)
            {
                _playerIds[i] = playerIds[i];
            }
            Array.Sort(_playerIds);

            // The old acceptance set described the previous roster; make every peer re-handshake
            // against the new one. Only the handshake: priming here would submit neutral input
            // over ticks the local player may already have real input in.
            _acceptedPeers.Clear();
            SendHandshake();
        }

        /// <summary>
        /// Resets the session's per-frame bookkeeping to a freshly rebuilt tick, so it does not
        /// try to republish hashes for ticks the rebuild discarded or resend stale inputs.
        /// </summary>
        /// <remarks>
        /// Call after <see cref="RollbackEngine.PrepareForRebuild(ref SimRebuildState, bool)"/>.
        /// The resume tick is already confirmed and hashed locally by the rebuild, so publishing
        /// resumes from the tick after it.
        /// </remarks>
        public void NotifyRebuilt(int resumeTick)
        {
            _lastPublishedTick = resumeTick;
            _recentLocal.Clear();

            // The rebuild reset the input buffer to resumeTick - 1, so the local run starts over
            // from the resume tick.
            _nextLocalTick = resumeTick;

            // A rebuild can add or remove bodies, so the registration table may have changed.
            // Re-exchange and re-confirm it once the rebuilt world has taken its first step.
            _internalIdSendsLeft = InternalIdRedundancy;
            _registrationConfirmed.Clear();
        }

        /// <summary>
        /// Announces this peer to the others and readies its input stream. Call once, after
        /// construction and after <see cref="RollbackEngine.Initialise"/>.
        /// </summary>
        /// <remarks>
        /// The announcement broadcasts the config hash and player set so a mismatch is refused
        /// before a tick runs. It also marks where this peer's input stream begins, which
        /// <see cref="SubmitLocalInput"/> then keeps unbroken.
        /// </remarks>
        public void Start()
        {
            SendHandshake();
            _nextLocalTick = _engine.CurrentTick;
        }

        private void SendHandshake()
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
        /// <remarks>
        /// Stamp <paramref name="input"/> for <see cref="RollbackEngine.LocalInputTick"/>, and
        /// call this once per fixed update. Every tick between the last one submitted and this
        /// one is filled in with a copy, so the local stream is unbroken whatever the caller
        /// passed.
        /// <para>
        /// That matters more than it looks. Confirmation needs an unbroken run —
        /// <see cref="InputBuffer.ConfirmedThrough"/> walks forward only over ticks every player
        /// has filled, and stops at the first that is not — so one missing tick is not untidy,
        /// it is terminal: the frontier never gets past it, and the peer stalls for good once
        /// the clock reaches its bound, with input appearing to do nothing at all. Stamping
        /// ahead by <see cref="SimConfig.LocalInputDelay"/> opens exactly such a hole at the
        /// start: <see cref="RollbackEngine.LocalInputTick"/> is the clock plus the delay, so
        /// the very first stamp is for tick <c>LocalInputDelay</c> and nothing ever covers the
        /// delay ticks between the tick a session starts at and that first stamp.
        /// </para>
        /// <para>
        /// Copying the current sample across the gap is also the right value, not just a
        /// convenient one: repeating the newest input is exactly what the other peers'
        /// prediction assumed for those ticks, so filling them agrees with the guess instead of
        /// correcting it.
        /// </para>
        /// </remarks>
        public void SubmitLocalInput(SimInput input)
        {
            input.PlayerId = _localPlayerId;

            int from = _nextLocalTick <= input.Tick ? _nextLocalTick : input.Tick;

            // A gap wider than the buffer cannot be filled -- the oldest copies would fall out
            // of the ring before the newest went in. The peer has hitched beyond what the
            // session can absorb and needs a rebuild; fill what will fit so the diagnosis is a
            // stall rather than a silent wrong answer.
            int span = input.Tick - from + 1;
            if (span > _config.SnapshotHistory)
            {
                SimLog.Warning(string.Format(
                    "Local input jumped {0} ticks, past the {1}-tick buffer, so ticks {2} to {3} are lost. " +
                    "This peer stopped submitting for longer than the session can absorb and needs a rebuild.",
                    span, _config.SnapshotHistory, from, input.Tick - _config.SnapshotHistory));
                from = input.Tick - _config.SnapshotHistory + 1;
            }

            int added = 0;
            for (int tick = from; tick <= input.Tick; ++tick)
            {
                SimInput stamped = input;
                stamped.Tick = tick;
                _engine.SubmitInput(stamped);
                _recentLocal.Enqueue(stamped);
                ++added;
            }

            if (input.Tick >= _nextLocalTick)
            {
                _nextLocalTick = input.Tick + 1;
            }

            // Everything just filled has to go out in this datagram, so the redundancy window
            // widens to hold it rather than dropping the oldest of a burst it never sent.
            int keep = RedundancyWindow < 1 ? 1 : RedundancyWindow;
            if (keep < added)
            {
                keep = added;
            }
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

                // All three channels, not the fold. Sixteen extra bytes on a message already
                // sent every confirmed tick, and a mismatch names the channel instead of
                // reporting that the worlds differ somehow.
                SimStateHashes hashes = snapshot.Hashes;
                _detector.RecordLocal(tick, hashes);

                SimByteWriter writer = new SimByteWriter(36);
                writer.WriteByte((byte)SimMessageKind.Hash);
                writer.WriteUInt32(_localPlayerId);
                writer.WriteInt32(tick);
                writer.WriteUInt64(hashes.Physics);
                writer.WriteUInt64(hashes.Entity);
                writer.WriteUInt64(hashes.Game);
                SendCopy(ref writer);
            }

            // Once the first confirmed step has assigned PhysX its actor indices, tell the peers
            // how this peer registered its bodies, so a registration-order mismatch is caught as a
            // named line here rather than as a gradual physics desync after the first contact.
            if (_internalIdSendsLeft > 0 && confirmed >= 1)
            {
                SendInternalIds();
                _internalIdSendsLeft -= 1;
            }

            // Check any peer tables that arrived before the local world had stepped. Now that a
            // confirmed step exists the local actor indices are assigned and comparable.
            if (confirmed >= 1 && _pendingPeerIds.Count > 0)
            {
                CheckPendingInternalIds();
            }
        }

        private void CheckPendingInternalIds()
        {
            int localCount;
            SimInternalIdEntry[] local = _engine.ReadInternalIds(out localCount);

            foreach (KeyValuePair<uint, SimInternalIdEntry[]> pending in _pendingPeerIds)
            {
                SimInternalIdEntry[] peer = pending.Value;
                string problem;
                if (!SimRegistrationCheck.Compare(local, localCount, peer, peer.Length, out problem))
                {
                    SimLog.Error(string.Format(
                        "Registration mismatch against peer {0}: {1}\n{2}",
                        pending.Key, problem, SimRegistrationCheck.Describe(local, localCount)));
                }
                else if (_registrationConfirmed.Add(pending.Key))
                {
                    // Say so once. A check that only ever speaks up on failure is indistinguishable
                    // from a check that never ran, which is exactly the doubt worth removing while
                    // someone is eliminating causes of a desync.
                    SimLog.Info(string.Format(
                        "Registration order matches peer {0} across {1} bodies.", pending.Key, localCount));
                }
            }

            _pendingPeerIds.Clear();
        }

        private void SendInternalIds()
        {
            int count;
            SimInternalIdEntry[] entries = _engine.ReadInternalIds(out count);

            SimByteWriter writer = new SimByteWriter(7 + count * 12);
            writer.WriteByte((byte)SimMessageKind.InternalIds);
            writer.WriteUInt32(_localPlayerId);
            writer.WriteUInt16((ushort)count);
            for (int i = 0; i < count; ++i)
            {
                writer.WriteUInt32(entries[i].StableId);
                writer.WriteUInt32(entries[i].Kind);
                writer.WriteUInt32(entries[i].InternalActorIndex);
            }
            SendCopy(ref writer);
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
                case SimMessageKind.InternalIds:
                    ReadInternalIds(ref reader);
                    break;
                case SimMessageKind.EntityHashes:
                    ReadEntityHashes(ref reader);
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

        /// <summary>
        /// Logs this peer's per-entity hashes for a physics disagreement, so the body that
        /// diverged can be found by diffing two peers' logs.
        /// </summary>
        /// <remarks>
        /// Only the local table is printed, and deliberately so. Every peer runs its own
        /// detector and every peer reports the same tick, so each one logs its own table
        /// without a request, a reply, or a round trip to a peer that may itself be the wrong
        /// one. The entry whose hash differs between the two logs is the diverged body; the
        /// entries that agree are the ones worth ruling out.
        /// </remarks>
        private void LogDivergedEntities(SimDesyncReport report)
        {
            if ((report.Channels & SimStateChannel.Physics) == 0)
            {
                return;
            }

            SimEntryHash[] entries;
            int count;
            if (!_engine.TryGetConfirmedEntityHashes(report.Tick, out entries, out count))
            {
                SimLog.Warning(string.Format(
                    "No per-entity hashes retained for tick {0}. Set SimConfig.PerEntityHashDiagnostics " +
                    "on every peer to have the next physics desync name the body.", report.Tick));
                return;
            }

            // Send the table as well as logging it. Both peers detect this tick and both send, so
            // each side ends up able to name the body on its own -- no request, no reply, and no
            // need to decide which peer is the correct one.
            SendEntityHashes(report.Tick, entries, count);

            StringBuilder text = new StringBuilder();
            text.AppendFormat(
                "Per-entity hashes at tick {0} ({1} entries), sent to the peers for comparison.",
                report.Tick, count);
            for (int i = 0; i < count; ++i)
            {
                text.AppendFormat("\n  id {0,-6} kind {1,-2} {2:X16}",
                    entries[i].StableId, entries[i].Kind, entries[i].Hash);
            }
            SimLog.Error(text.ToString());
        }

        private void SendEntityHashes(int tick, SimEntryHash[] entries, int count)
        {
            SimByteWriter writer = new SimByteWriter(11 + count * 16);
            writer.WriteByte((byte)SimMessageKind.EntityHashes);
            writer.WriteUInt32(_localPlayerId);
            writer.WriteInt32(tick);
            writer.WriteUInt16((ushort)count);
            for (int i = 0; i < count; ++i)
            {
                writer.WriteUInt32(entries[i].StableId);
                writer.WriteUInt32(entries[i].Kind);
                writer.WriteUInt64(entries[i].Hash);
            }
            SendCopy(ref writer);
        }

        /// <summary>
        /// Compares a peer's per-entity table for a tick against ours and names the diverged body.
        /// </summary>
        private void ReadEntityHashes(ref SimByteReader reader)
        {
            uint senderId = reader.ReadUInt32();
            int tick = reader.ReadInt32();
            int peerCount = reader.ReadUInt16();

            SimEntryHash[] peer = new SimEntryHash[peerCount];
            for (int i = 0; i < peerCount; ++i)
            {
                peer[i].StableId = reader.ReadUInt32();
                peer[i].Kind = reader.ReadUInt32();
                peer[i].Hash = reader.ReadUInt64();
            }

            SimEntryHash[] local;
            int localCount;
            if (!_engine.TryGetConfirmedEntityHashes(tick, out local, out localCount))
            {
                SimLog.Warning(string.Format(
                    "Peer {0} sent per-entity hashes for tick {1}, but this peer no longer retains that " +
                    "tick. Widen SimConfig.SnapshotHistory to compare it.", senderId, tick));
                return;
            }

            string difference = SimEntityHashDiff.Describe(local, localCount, peer, peerCount, senderId, tick);
            if (difference != null)
            {
                SimLog.Error(difference);
            }
        }

        private void ReadHash(ref SimByteReader reader)
        {
            uint senderId = reader.ReadUInt32();
            int tick = reader.ReadInt32();
            ulong physics = reader.ReadUInt64();
            ulong entity = reader.ReadUInt64();
            ulong game = reader.ReadUInt64();
            _detector.RecordPeer(senderId, tick, new SimStateHashes(physics, entity, game));
        }

        /// <summary>
        /// Stashes a peer's registration table for checking once the local world has stepped.
        /// </summary>
        /// <remarks>
        /// The comparison itself runs in <see cref="PublishConfirmed"/>: a peer that has taken its
        /// first step may send its table while this peer has not, and the local actor indices are
        /// not assigned until then, so comparing on arrival could report a mismatch that is only a
        /// difference in progress. The peers have already agreed config and roster by the time
        /// either sends a table, so a real mismatch is a genuine build-order bug, not a join
        /// transient. The actor index is stable across ticks, so the few ticks between the two
        /// peers do not matter; see <see cref="SimRegistrationCheck"/>.
        /// </remarks>
        private void ReadInternalIds(ref SimByteReader reader)
        {
            uint senderId = reader.ReadUInt32();
            int peerCount = reader.ReadUInt16();

            SimInternalIdEntry[] peer = new SimInternalIdEntry[peerCount];
            for (int i = 0; i < peerCount; ++i)
            {
                peer[i].StableId = reader.ReadUInt32();
                peer[i].Kind = reader.ReadUInt32();
                peer[i].InternalActorIndex = reader.ReadUInt32();
            }

            _pendingPeerIds[senderId] = peer;
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
