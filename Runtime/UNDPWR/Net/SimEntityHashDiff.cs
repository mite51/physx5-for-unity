using System.Text;
using UNDPWR.Interop;

namespace UNDPWR.Net
{
    /// <summary>
    /// Compares two peers' per-entity hash tables for the same confirmed tick and names the
    /// bodies that diverged.
    /// </summary>
    /// <remarks>
    /// The whole-world physics hash says the simulations differ; this says which body. That is
    /// usually the whole diagnosis, because the bodies in a scene do different jobs: a single
    /// dynamic body that nothing pushes points at contact or sleep behaviour, a body under
    /// per-tick forces points at how those forces are applied, and a body that should never move
    /// at all points at something writing to it.
    /// <para>
    /// Both peers detect the same disagreeing tick independently and both send their table, so the
    /// comparison runs on each side without a request, a reply, or having to decide which peer is
    /// the correct one.
    /// </para>
    /// </remarks>
    public static class SimEntityHashDiff
    {
        /// <summary>
        /// Describes every entity whose hash differs between two tables for one tick.
        /// </summary>
        /// <param name="local">This peer's table for the tick.</param>
        /// <param name="localCount">How many of <paramref name="local"/> are meaningful.</param>
        /// <param name="peer">The peer's table for the same tick.</param>
        /// <param name="peerCount">How many of <paramref name="peer"/> are meaningful.</param>
        /// <param name="peerId">The peer the table came from, for the message.</param>
        /// <param name="tick">The confirmed tick both tables describe.</param>
        /// <returns>A description of the differences, or null when the two tables agree.</returns>
        public static string Describe(
            SimEntryHash[] local, int localCount,
            SimEntryHash[] peer, int peerCount,
            uint peerId, int tick)
        {
            if (localCount != peerCount)
            {
                return string.Format(
                    "Peers hold different numbers of bodies at tick {0}: {1} locally, {2} on peer {3}. " +
                    "A spawn or despawn ran on one peer and not the other.",
                    tick, localCount, peerCount, peerId);
            }

            StringBuilder text = null;
            for (int i = 0; i < localCount; ++i)
            {
                if (local[i].StableId != peer[i].StableId)
                {
                    return string.Format(
                        "Tables are not in the same order at tick {0}: position {1} is stable ID {2} " +
                        "locally and {3} on peer {4}.",
                        tick, i, local[i].StableId, peer[i].StableId, peerId);
                }

                if (local[i].Hash == peer[i].Hash)
                {
                    continue;
                }

                if (text == null)
                {
                    text = new StringBuilder();
                    text.AppendFormat("Diverged bodies at tick {0} against peer {1}:", tick, peerId);
                }
                text.AppendFormat("\n  id {0} kind {1}: local {2:X16} != peer {3:X16}",
                    local[i].StableId, local[i].Kind, local[i].Hash, peer[i].Hash);
            }

            if (text == null)
            {
                return null;
            }

            text.Append("\n  Every other body agrees, so whatever happened is confined to the bodies above.");
            return text.ToString();
        }
    }
}
