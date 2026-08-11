using System;
using System.Collections.Generic;
using System.Text;
using UNDPWR.Core;
using UNDPWR.Diagnostics;

namespace UNDPWR.Net
{
    /// <summary>
    /// What a peer reported when its confirmed-tick hash disagreed with ours.
    /// </summary>
    public struct SimDesyncReport
    {
        /// <summary>The confirmed tick the two peers disagree about.</summary>
        public int Tick;

        /// <summary>Which peer reported the differing hash.</summary>
        public uint PeerId;

        /// <summary>This peer's per-channel hashes at that tick.</summary>
        public SimStateHashes Local;

        /// <summary>The other peer's per-channel hashes at that tick.</summary>
        public SimStateHashes Peer;

        /// <summary>
        /// Which channels actually differ, which is the first thing worth knowing.
        /// </summary>
        public SimStateChannel Channels;

        /// <summary>This peer's combined snapshot hash at that tick.</summary>
        public ulong LocalHash { get { return Local.Combined; } }

        /// <summary>The other peer's combined hash at that tick.</summary>
        public ulong PeerHash { get { return Peer.Combined; } }

        /// <summary>
        /// A multi-line account naming the diverged channels and showing all three side by
        /// side, so the log says where to look rather than only that something is wrong.
        /// </summary>
        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.AppendFormat("Desync at tick {0} against peer {1}. Diverged: {2}.",
                Tick, PeerId, Channels == SimStateChannel.None ? "nothing" : Channels.ToString());

            AppendChannel(text, "physics", Local.Physics, Peer.Physics);
            AppendChannel(text, "entity ", Local.Entity, Peer.Entity);
            AppendChannel(text, "game   ", Local.Game, Peer.Game);

            text.Append(Hint());
            return text.ToString();
        }

        private static void AppendChannel(StringBuilder text, string name, ulong local, ulong peer)
        {
            text.AppendFormat("\n  {0} local {1:X16} {2} peer {3:X16}",
                name, local, local == peer ? "==" : "!=", peer);
        }

        /// <summary>Where the diverged channel points, since the categories barely overlap.</summary>
        private string Hint()
        {
            switch (Channels)
            {
                case SimStateChannel.Physics:
                    return "\n  Physics alone: the solver, the rollback path, or an actor moved outside a step handler. " +
                        "Managed state agrees, so gameplay logic ran identically.";
                case SimStateChannel.Entity:
                    return "\n  Entity channel alone: per-entity managed state. Physics agrees, so this is capture/restore " +
                        "order or a field mutated outside a step handler, not the simulation.";
                case SimStateChannel.Game:
                    return "\n  Game channel alone: the mode, the score, or the action queue. Physics agrees, so the bodies " +
                        "are in the same places and the disagreement is in game logic reacting to them.";
                case SimStateChannel.None:
                    return "\n  No channel differs, so the fold disagrees with its parts -- a framework bug, not a game one.";
                default:
                    return "\n  More than one channel: usually one cause that has already propagated. The earliest diverged " +
                        "tick is the one to look at; this may not be it.";
            }
        }
    }

    /// <summary>
    /// Compares this peer's confirmed-tick hashes against the hashes other peers report for
    /// the same ticks, and raises the first disagreement.
    /// </summary>
    /// <remarks>
    /// A confirmed tick is final: every peer had every input for it, restored the same
    /// snapshot and ran the same step, so every peer's <c>Snapshot.CombinedHash</c> must be
    /// identical. When two disagree, the simulations have diverged — a determinism bug, a
    /// tampered peer, or a version mismatch the handshake missed. Catching it here turns a
    /// slow, unattributable drift into a single reported tick.
    /// <para>
    /// The check is <b>mandatory</b>, because the engine rewinds a data-dependent depth and
    /// runs a data-dependent-length window with no fixed identical-sequence property to fall
    /// back on, only PGS transparency. Confirmed-hash agreement is what verifies that
    /// property held, which is why <see cref="Fatal"/> is set for every networked session:
    /// the first disagreement throws rather than merely reporting.
    /// </para>
    /// Hashes can arrive before or after the local tick is computed and in any order, so both
    /// sides are kept in bounded ring buffers and compared whenever the matching half turns
    /// up. Ticks older than the retained window are dropped rather than compared, on the
    /// assumption that a live desync shows up within a few hundred ticks.
    /// </remarks>
    public sealed class SimDesyncDetector
    {
        private readonly Dictionary<int, SimStateHashes> _localHashes = new Dictionary<int, SimStateHashes>();
        private readonly Dictionary<long, SimStateHashes> _peerHashes = new Dictionary<long, SimStateHashes>();
        private readonly int _window;
        private int _newestLocalTick = -1;

        /// <summary>Raised once per disagreeing tick, before <see cref="Fatal"/> is honoured.</summary>
        public event Action<SimDesyncReport> DesyncDetected;

        /// <summary>The number of disagreements seen so far.</summary>
        public int DesyncCount { get; private set; }

        /// <summary>
        /// When true, a disagreement throws <see cref="SimDesyncException"/> after the event
        /// is raised. A networked session sets it, because confirmed-hash agreement is the
        /// only thing verifying the PGS transparency the engine's data-dependent rollback
        /// rests on.
        /// </summary>
        public bool Fatal { get; set; }

        /// <summary>Creates a detector that retains the last <paramref name="window"/> ticks.</summary>
        public SimDesyncDetector(int window = 256)
        {
            _window = window < 1 ? 1 : window;
        }

        /// <summary>
        /// Records this peer's confirmed hash for a tick and checks it against any peer hashes
        /// already received for it.
        /// </summary>
        public void RecordLocal(int tick, SimStateHashes hash)
        {
            if (tick < 0)
            {
                return;
            }

            _localHashes[tick] = hash;
            if (tick > _newestLocalTick)
            {
                _newestLocalTick = tick;
            }

            // A peer hash may already be waiting for this tick from any peer.
            List<long> matched = null;
            foreach (KeyValuePair<long, SimStateHashes> entry in _peerHashes)
            {
                if (TickOfKey(entry.Key) == tick)
                {
                    if (!entry.Value.Equals(hash))
                    {
                        Report(tick, hash, entry.Value, PeerOfKey(entry.Key));
                    }
                    if (matched == null)
                    {
                        matched = new List<long>();
                    }
                    matched.Add(entry.Key);
                }
            }
            if (matched != null)
            {
                for (int i = 0; i < matched.Count; ++i)
                {
                    _peerHashes.Remove(matched[i]);
                }
            }

            Prune();
        }

        /// <summary>
        /// Records a peer's confirmed hash for a tick and checks it against ours, if we have
        /// computed that tick.
        /// </summary>
        public void RecordPeer(uint peerId, int tick, SimStateHashes hash)
        {
            if (tick < 0)
            {
                return;
            }

            SimStateHashes localHash;
            if (_localHashes.TryGetValue(tick, out localHash))
            {
                if (!localHash.Equals(hash))
                {
                    Report(tick, localHash, hash, peerId);
                }
                return;
            }

            // Not computed yet, or already dropped from the window. Only stash it if it is
            // still within reach of the local frontier.
            if (_newestLocalTick >= 0 && tick <= _newestLocalTick - _window)
            {
                return;
            }
            _peerHashes[KeyOf(peerId, tick)] = hash;
        }

        private void Report(int tick, SimStateHashes localHash, SimStateHashes peerHash, uint peerId)
        {
            DesyncCount++;

            SimDesyncReport report = new SimDesyncReport();
            report.Tick = tick;
            report.PeerId = peerId;
            report.Local = localHash;
            report.Peer = peerHash;
            report.Channels = localHash.Differences(peerHash);

            SimLog.Error(report.Describe());

            Action<SimDesyncReport> handler = DesyncDetected;
            if (handler != null)
            {
                handler(report);
            }

            if (Fatal)
            {
                throw new SimDesyncException(report);
            }
        }

        private void Prune()
        {
            if (_newestLocalTick < _window)
            {
                return;
            }
            int oldest = _newestLocalTick - _window;

            List<int> staleLocal = null;
            foreach (int tick in _localHashes.Keys)
            {
                if (tick <= oldest)
                {
                    if (staleLocal == null)
                    {
                        staleLocal = new List<int>();
                    }
                    staleLocal.Add(tick);
                }
            }
            if (staleLocal != null)
            {
                for (int i = 0; i < staleLocal.Count; ++i)
                {
                    _localHashes.Remove(staleLocal[i]);
                }
            }

            List<long> stalePeer = null;
            foreach (long key in _peerHashes.Keys)
            {
                if (TickOfKey(key) <= oldest)
                {
                    if (stalePeer == null)
                    {
                        stalePeer = new List<long>();
                    }
                    stalePeer.Add(key);
                }
            }
            if (stalePeer != null)
            {
                for (int i = 0; i < stalePeer.Count; ++i)
                {
                    _peerHashes.Remove(stalePeer[i]);
                }
            }
        }

        private static long KeyOf(uint peerId, int tick)
        {
            return ((long)peerId << 32) | unchecked((uint)tick);
        }

        private static int TickOfKey(long key)
        {
            return unchecked((int)(key & 0xFFFFFFFF));
        }

        private static uint PeerOfKey(long key)
        {
            return (uint)((key >> 32) & 0xFFFFFFFF);
        }
    }

    /// <summary>Thrown when a confirmed-tick hash disagrees and the detector is set fatal.</summary>
    public sealed class SimDesyncException : Exception
    {
        /// <summary>The disagreement that caused the throw.</summary>
        public SimDesyncReport Report { get; private set; }

        /// <summary>Creates the exception from a report.</summary>
        public SimDesyncException(SimDesyncReport report)
            : base(report.Describe())
        {
            Report = report;
        }
    }
}
