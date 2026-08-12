using System.Text;
using UNDPWR.Interop;

namespace UNDPWR.Net
{
    /// <summary>
    /// Compares how two peers BUILT their bodies and names the first entity that was constructed
    /// differently, which is a determinism bug no other check in the framework can see.
    /// </summary>
    /// <remarks>
    /// Everything the session already exchanges describes state: where a body is, how fast it is
    /// going, whether it is asleep. Nothing describes how the body was made -- its shapes, their
    /// local poses and offsets, its materials, its mass, its solver iteration counts, its
    /// depenetration clamp. All of that is read by every solve and none of it is in a snapshot, so
    /// two peers that build the same entity from slightly different numbers agree on the state
    /// hash, the per-entity hashes and the registration table, and diverge anyway.
    /// <para>
    /// What makes this worth a dedicated check is the delay between cause and symptom. A
    /// construction difference does nothing at all while a body is lightly loaded, and then desyncs
    /// it within a second or two once it is squeezed. A one-ULP difference in one shape's local
    /// pose on a spiked ball is invisible for as long as the ball is only rolling on the floor, and
    /// forks the simulation about a hundred ticks after two players start pressing it between them.
    /// By that point nothing in the logs points anywhere near the construction.
    /// </para>
    /// <para>
    /// Compounds of offset shapes are the case that needs this most, for two reasons. They have far
    /// more surface to get wrong -- twenty-five shapes is twenty-five geometries, local poses and
    /// material bindings rather than one -- and the mass canonicalisation that makes a
    /// near-spherical compound's mass frame stable across peers (see <c>SimMass.Setup</c>)
    /// deliberately discards the detail that would otherwise have shown up as a mass-hash
    /// mismatch. The construction hash is what is left to catch them.
    /// </para>
    /// <para>
    /// Peers exchange this once after the world is built and again after a rebuild. Unlike the
    /// state hash it does not change as the simulation runs, so two peers on different ticks can
    /// still compare it directly.
    /// </para>
    /// </remarks>
    public static class SimConstructionCheck
    {
        /// <summary>
        /// Compares a peer's construction table against the local one and describes the first
        /// disagreement.
        /// </summary>
        /// <param name="local">The local table, as read from <see cref="UNDPWR.Core.DeterministicWorld.ReadConstructionHashes"/>.</param>
        /// <param name="localCount">How many of <paramref name="local"/> are meaningful.</param>
        /// <param name="peer">The peer's table, as received on the wire.</param>
        /// <param name="peerCount">How many of <paramref name="peer"/> are meaningful.</param>
        /// <param name="problem">A description of the first disagreement, or null when they agree.</param>
        /// <returns>True when the two peers built the same bodies.</returns>
        public static bool Compare(
            SimEntryHash[] local, int localCount,
            SimEntryHash[] peer, int peerCount,
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

                if (local[i].Hash != peer[i].Hash)
                {
                    problem = string.Format(
                        "Stable ID {0} was built differently on the two peers: construction hash " +
                        "0x{1:X16} locally, 0x{2:X16} on the peer. Something about how this entity was " +
                        "made differs -- the number of shapes or the order they were attached in, a " +
                        "shape's geometry, local pose, contact or rest offset, filter data or material, " +
                        "or the body's mass, damping, velocity clamps, depenetration clamp or solver " +
                        "iteration counts. The state hashes will keep agreeing until the body is loaded " +
                        "hard enough for the difference to matter, so fix this rather than waiting to " +
                        "see whether it causes a desync.",
                        local[i].StableId, local[i].Hash, peer[i].Hash);
                    return false;
                }
            }

            problem = null;
            return true;
        }

        /// <summary>
        /// Renders a construction table for logging, so two peers' tables can be diffed by eye
        /// when the automatic comparison cannot run.
        /// </summary>
        public static string Describe(SimEntryHash[] entries, int count)
        {
            StringBuilder text = new StringBuilder();
            text.AppendFormat("Construction table ({0} entries): stable ID, kind, construction hash", count);
            for (int i = 0; i < count; ++i)
            {
                text.AppendFormat("\n  id {0,-6} kind {1,-2} construction 0x{2:X16}",
                    entries[i].StableId, entries[i].Kind, entries[i].Hash);
            }
            return text.ToString();
        }
    }
}
