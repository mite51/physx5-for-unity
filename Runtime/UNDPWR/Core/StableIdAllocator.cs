using System;
using System.Collections.Generic;
using UNDPWR.Diagnostics;

namespace UNDPWR.Core
{
    /// <summary>
    /// Hands out the stable IDs that every peer identifies the same object by.
    /// </summary>
    /// <remarks>
    /// This is the piece the original UNDPWR found hardest to get right, and the reason
    /// it eventually worked: every peer must agree on which PhysX actor corresponds to
    /// which game object, and that agreement cannot come from spawn order, instance IDs,
    /// scene traversal order or anything else the engine decides locally. It has to be
    /// assigned from something all peers compute identically.
    ///
    /// <para>IDs are partitioned so that different sources cannot collide:</para>
    /// <list type="bullet">
    /// <item><description><b>Authored</b> (1 .. 0x0FFFFFFF) belongs to content. A crate
    /// placed in a scene keeps its ID across sessions and across builds, so it is
    /// assigned at author time and baked, never allocated at runtime.</description></item>
    /// <item><description><b>Deterministic runtime</b> (0x10000000 .. 0x7FFFFFFF) is for
    /// objects spawned during a session. Allocation is a pure function of the session
    /// seed and the number of allocations so far, so every peer walking the same
    /// simulation allocates the same IDs in the same order without exchanging a
    /// message.</description></item>
    /// <item><description><b>Local</b> (0x80000000 and above) is for objects that exist
    /// on one peer only, such as a debug visualiser. Never registered with a world, and
    /// rejected if it is.</description></item>
    /// </list>
    ///
    /// <para>The deterministic runtime range only works while allocation happens inside
    /// the simulation, on a tick every peer runs. Allocating from a UI callback, a
    /// coroutine or a network message handler happens at a different point on each peer
    /// and desyncs the allocator itself, which is why <see cref="Allocate"/> asks what
    /// tick it is being called on and complains when that goes backwards.</para>
    /// </remarks>
    public sealed class StableIdAllocator
    {
        /// <summary>First ID in the authored range.</summary>
        public const uint AuthoredRangeStart = 1u;

        /// <summary>Last ID in the authored range.</summary>
        public const uint AuthoredRangeEnd = 0x0FFFFFFFu;

        /// <summary>First ID in the deterministic runtime range.</summary>
        public const uint RuntimeRangeStart = 0x10000000u;

        /// <summary>Last ID in the deterministic runtime range.</summary>
        public const uint RuntimeRangeEnd = 0x7FFFFFFFu;

        /// <summary>First ID in the local, never-networked range.</summary>
        public const uint LocalRangeStart = 0x80000000u;

        private readonly uint _sessionSeed;
        private uint _nextRuntimeId = RuntimeRangeStart;
        private uint _nextLocalId = LocalRangeStart;
        private int _lastAllocationTick = int.MinValue;
        private readonly HashSet<uint> _issued = new HashSet<uint>();

        /// <summary>The seed every peer in this session shares.</summary>
        public uint SessionSeed { get { return _sessionSeed; } }

        /// <summary>How many deterministic runtime IDs have been handed out.</summary>
        public int RuntimeAllocationCount { get { return (int)(_nextRuntimeId - RuntimeRangeStart); } }

        /// <summary>
        /// Creates an allocator for a session.
        /// </summary>
        /// <param name="sessionSeed">
        /// Agreed by every peer at session start, usually broadcast by whoever created
        /// the session. Peers with different seeds allocate different IDs and will not
        /// agree on anything.
        /// </param>
        public StableIdAllocator(uint sessionSeed)
        {
            _sessionSeed = sessionSeed;
        }

        /// <summary>
        /// Allocates the next deterministic runtime ID.
        /// </summary>
        /// <param name="currentTick">
        /// The tick being simulated. Used only to catch allocation from outside the
        /// simulation, which is the mistake this range is most often broken by.
        /// </param>
        /// <exception cref="InvalidOperationException">The runtime range is exhausted.</exception>
        public uint Allocate(int currentTick)
        {
            if (_nextRuntimeId > RuntimeRangeEnd)
            {
                throw new InvalidOperationException(
                    "The deterministic runtime stable-ID range is exhausted. Pool and reuse spawned objects " +
                    "with DeterministicWorld.SetEntityEnabled rather than allocating a new ID for each one.");
            }

            // Allocation order is the determinism guarantee here, so a caller that
            // allocates from outside the tick loop needs to hear about it. Replays
            // legitimately revisit a tick, so only going backwards is suspicious.
            if (currentTick < _lastAllocationTick)
            {
                SimLog.Warning(string.Format(
                    "Stable ID allocated on tick {0} after one was allocated on tick {1}. Allocation order is " +
                    "part of the determinism guarantee, so this must happen inside the simulation, not from a " +
                    "UI callback, coroutine or network handler.",
                    currentTick, _lastAllocationTick));
            }
            _lastAllocationTick = currentTick;

            uint id = _nextRuntimeId++;
            _issued.Add(id);
            return id;
        }

        /// <summary>
        /// Allocates an ID for something that exists on this peer only and is never
        /// registered with a networked world.
        /// </summary>
        public uint AllocateLocal()
        {
            if (_nextLocalId == 0u)
            {
                throw new InvalidOperationException("The local stable-ID range is exhausted.");
            }
            return _nextLocalId++;
        }

        /// <summary>
        /// Records an authored ID so that a collision with another authored object, or
        /// with a runtime allocation, is caught at load rather than in the field.
        /// </summary>
        /// <returns>False when the ID is out of range or already in use.</returns>
        public bool RegisterAuthored(uint stableId)
        {
            if (!IsAuthored(stableId))
            {
                SimLog.Error(string.Format(
                    "Authored stable ID {0} is outside the authored range [{1}, {2}]. Authored content must not " +
                    "use runtime or local IDs.",
                    stableId, AuthoredRangeStart, AuthoredRangeEnd));
                return false;
            }
            if (!_issued.Add(stableId))
            {
                SimLog.Error(string.Format(
                    "Authored stable ID {0} is used more than once. Every peer would resolve it to a different " +
                    "actor.", stableId));
                return false;
            }
            return true;
        }

        /// <summary>True when the ID belongs to authored content.</summary>
        public static bool IsAuthored(uint stableId)
        {
            return stableId >= AuthoredRangeStart && stableId <= AuthoredRangeEnd;
        }

        /// <summary>True when the ID was allocated deterministically at runtime.</summary>
        public static bool IsRuntime(uint stableId)
        {
            return stableId >= RuntimeRangeStart && stableId <= RuntimeRangeEnd;
        }

        /// <summary>True when the ID belongs to this peer only and must never be networked.</summary>
        public static bool IsLocal(uint stableId)
        {
            return stableId >= LocalRangeStart;
        }

        /// <summary>
        /// Restores the allocator to a known point, so that a peer rebuilding from a
        /// snapshot continues the same allocation sequence as everyone else.
        /// </summary>
        /// <remarks>
        /// Part of the synchronised rebuild that brings a mid-match joiner in. Without
        /// it the joiner would start allocating from the beginning of the range and
        /// collide with IDs already in use.
        /// </remarks>
        public void RestoreTo(uint nextRuntimeId, int lastAllocationTick)
        {
            if (nextRuntimeId < RuntimeRangeStart || nextRuntimeId > RuntimeRangeEnd + 1)
            {
                throw new ArgumentOutOfRangeException("nextRuntimeId",
                    string.Format("{0} is outside the deterministic runtime range", nextRuntimeId));
            }
            _nextRuntimeId = nextRuntimeId;
            _lastAllocationTick = lastAllocationTick;
        }

        /// <summary>The next runtime ID that would be handed out, for replication.</summary>
        public uint NextRuntimeId { get { return _nextRuntimeId; } }
    }
}
