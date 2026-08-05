using System;

namespace UNDPWR.Net
{
    /// <summary>
    /// The one thing the framework needs from a network: move opaque byte messages between
    /// peers.
    /// </summary>
    /// <remarks>
    /// The framework never reaches for a socket directly, so it can run over anything a game
    /// already uses — a lobby relay, a peer-to-peer mesh, a loopback for tests — as long as it
    /// can send a datagram and hand back the ones that arrive. The contract is intentionally
    /// thin:
    /// <list type="bullet">
    /// <item><description>Messages are whole. A message sent as one call is received as one
    /// <see cref="TryReceive"/>, never split or merged. UDP already gives this per datagram; a
    /// stream transport must frame.</description></item>
    /// <item><description>Delivery is best-effort. Messages may be lost, duplicated or
    /// reordered; the framework is built to tolerate all three (inputs are idempotent and
    /// re-sent, hashes are per-tick), so the transport is not required to be reliable.</description></item>
    /// <item><description><see cref="Broadcast"/> reaches every <i>other</i> peer in the
    /// session, not the sender.</description></item>
    /// </list>
    /// The framework only ever sends inputs and two kinds of control message (see
    /// <see cref="SimMessageKind"/>); it does not put simulation state on the wire.
    /// </remarks>
    public interface ISimTransport
    {
        /// <summary>
        /// Sends a message to every other peer in the session.
        /// </summary>
        /// <param name="data">The buffer holding the message.</param>
        /// <param name="offset">Where the message starts in <paramref name="data"/>.</param>
        /// <param name="length">How many bytes the message is.</param>
        void Broadcast(byte[] data, int offset, int length);

        /// <summary>
        /// Hands back the next received message, if any.
        /// </summary>
        /// <param name="message">
        /// The received bytes. Only valid until the next call; copy anything that must
        /// outlive it.
        /// </param>
        /// <returns>False when nothing is pending.</returns>
        bool TryReceive(out ArraySegment<byte> message);
    }
}
