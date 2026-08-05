using System;
using System.Collections.Generic;
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

        /// <summary>This peer's combined snapshot hash at that tick.</summary>
        public ulong LocalHash;

        /// <summary>The other peer's hash at that tick.</summary>
        public ulong PeerHash;

        /// <summary>Which peer reported the differing hash.</summary>
        public uint PeerId;
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
    /// The check is <b>diagnostic</b> while the engine runs a fixed prediction horizon: the
    /// horizon is the safety net, and a stray mismatch resolves itself. It becomes
    /// <b>mandatory</b> the moment conditional rollback removes that net (adaptive-rollback
    /// Phase 2), which is what <see cref="Fatal"/> expresses: set it, and the first
    /// disagreement throws rather than merely reporting.
    /// </para>
    /// Hashes can arrive before or after the local tick is computed and in any order, so both
    /// sides are kept in bounded ring buffers and compared whenever the matching half turns
    /// up. Ticks older than the retained window are dropped rather than compared, on the
    /// assumption that a live desync shows up within a few hundred ticks.
    /// </remarks>
    public sealed class SimDesyncDetector
    {
        private readonly Dictionary<int, ulong> _localHashes = new Dictionary<int, ulong>();
        private readonly Dictionary<long, ulong> _peerHashes = new Dictionary<long, ulong>();
        private readonly int _window;
        private int _newestLocalTick = -1;

        /// <summary>Raised once per disagreeing tick, before <see cref="Fatal"/> is honoured.</summary>
        public event Action<SimDesyncReport> DesyncDetected;

        /// <summary>The number of disagreements seen so far.</summary>
        public int DesyncCount { get; private set; }

        /// <summary>
        /// When true, a disagreement throws <see cref="SimDesyncException"/> after the event
        /// is raised. Must be true once the fixed horizon is removed.
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
        public void RecordLocal(int tick, ulong hash)
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
            foreach (KeyValuePair<long, ulong> entry in _peerHashes)
            {
                if (TickOfKey(entry.Key) == tick)
                {
                    if (entry.Value != hash)
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
        public void RecordPeer(uint peerId, int tick, ulong hash)
        {
            if (tick < 0)
            {
                return;
            }

            ulong localHash;
            if (_localHashes.TryGetValue(tick, out localHash))
            {
                if (localHash != hash)
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

        private void Report(int tick, ulong localHash, ulong peerHash, uint peerId)
        {
            DesyncCount++;

            SimDesyncReport report = new SimDesyncReport();
            report.Tick = tick;
            report.LocalHash = localHash;
            report.PeerHash = peerHash;
            report.PeerId = peerId;

            SimLog.Error(string.Format(
                "Desync at tick {0}: local hash {1:X16}, peer {2} hash {3:X16}",
                tick, localHash, peerId, peerHash));

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
            : base(string.Format("Desync at tick {0}: local {1:X16} != peer {2} {3:X16}",
                report.Tick, report.LocalHash, report.PeerId, report.PeerHash))
        {
            Report = report;
        }
    }
}
