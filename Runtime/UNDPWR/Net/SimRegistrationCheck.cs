using System.Text;
using UNDPWR.Interop;

namespace UNDPWR.Net
{
    /// <summary>
    /// Compares two peers' stable-ID → PhysX-actor-index mappings and names the first body that
    /// was registered into PhysX differently, which is a determinism bug the handshake's config
    /// and roster checks cannot see.
    /// </summary>
    /// <remarks>
    /// PhysX hands out an actor index from insertion order, and the solver visits bodies and sums
    /// contact impulses in that order. Two peers that register the same entities in different
    /// orders get different indices, round differently the first time anything touches, and drift
    /// apart with no hint of the cause. Catching it by exchanging the mapping turns that slow,
    /// silent desync into one named line at join.
    /// <para>
    /// The comparison deliberately looks only at the actor index, never the island node index.
    /// The actor index is assigned when a body enters the scene and is stable for as long as it
    /// stays there; the island node index changes as a body joins or leaves a simulation island,
    /// which happens every time the ball falls asleep against a wall. Two peers a few ticks apart
    /// legitimately hold different island node indices for the same body, so comparing them would
    /// cry wolf. The actor index is what encodes registration order, and it is the same on every
    /// peer whenever the world is built identically, whatever tick each has reached.
    /// </para>
    /// </remarks>
    public static class SimRegistrationCheck
    {
        /// <summary>
        /// Compares a peer's registration table against the local one on the cross-tick-stable
        /// fields, and describes the first disagreement.
        /// </summary>
        /// <param name="local">The local table, as read from <see cref="UNDPWR.Core.DeterministicWorld.ReadInternalIds"/>.</param>
        /// <param name="localCount">How many of <paramref name="local"/> are meaningful.</param>
        /// <param name="peer">The peer's table, as received on the wire.</param>
        /// <param name="peerCount">How many of <paramref name="peer"/> are meaningful.</param>
        /// <param name="problem">A description of the first disagreement, or null when they agree.</param>
        /// <returns>True when the two mappings agree.</returns>
        public static bool Compare(
            SimInternalIdEntry[] local, int localCount,
            SimInternalIdEntry[] peer, int peerCount,
            out string problem)
        {
            if (localCount != peerCount)
            {
                problem = string.Format(
                    "Peers registered different numbers of bodies: {0} locally, {1} on the peer. " +
                    "Every peer must register the same entities before stepping; a missing or extra " +
                    "body is a spawn that ran on one peer and not the other.",
                    localCount, peerCount);
                return false;
            }

            for (int i = 0; i < localCount; ++i)
            {
                if (local[i].StableId != peer[i].StableId)
                {
                    problem = string.Format(
                        "Registration order differs at position {0}: this peer has stable ID {1}, the " +
                        "peer has {2}. Entities must be committed in ascending stable-ID order on every " +
                        "peer, so the two built the world in a different order.",
                        i, local[i].StableId, peer[i].StableId);
                    return false;
                }

                if (local[i].InternalActorIndex != peer[i].InternalActorIndex)
                {
                    problem = string.Format(
                        "Stable ID {0} was given different PhysX actor indices: {1} locally, {2} on the " +
                        "peer. The same framework entity is a different body inside PhysX, so the two will " +
                        "diverge the moment it touches anything.",
                        local[i].StableId, local[i].InternalActorIndex, peer[i].InternalActorIndex);
                    return false;
                }
            }

            problem = null;
            return true;
        }

        /// <summary>
        /// Renders a registration table for logging, so two peers' tables can be diffed by eye
        /// when the automatic comparison cannot run.
        /// </summary>
        public static string Describe(SimInternalIdEntry[] entries, int count)
        {
            StringBuilder text = new StringBuilder();
            text.AppendFormat("Registration table ({0} entries): stable ID, kind, PhysX actor index", count);
            for (int i = 0; i < count; ++i)
            {
                text.AppendFormat("\n  id {0,-6} kind {1,-2} actor {2}",
                    entries[i].StableId, entries[i].Kind, entries[i].InternalActorIndex);
            }
            return text.ToString();
        }
    }
}
