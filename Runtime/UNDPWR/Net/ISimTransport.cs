using System;

namespace UNDPWR.Net
{
    /// <summary>Delivery guarantee requested for a simulation message.</summary>
    public enum SimDelivery
    {
        /// <summary>May be lost, duplicated, or reordered.</summary>
        Unreliable = 0,

        /// <summary>Delivered exactly once and in send order.</summary>
        ReliableOrdered = 1
    }

    /// <summary>A received message together with its authenticated sender.</summary>
    public struct SimTransportMessage
    {
        /// <summary>The transport-authenticated peer that sent the message.</summary>
        public uint SenderId;

        /// <summary>The complete message payload.</summary>
        public ArraySegment<byte> Payload;

        /// <summary>The delivery path on which the message arrived.</summary>
        public SimDelivery Delivery;
    }

    /// <summary>
    /// Moves opaque messages between one authoritative server and its clients.
    /// </summary>
    /// <remarks>
    /// Sender identity must come from the authenticated connection, never from bytes supplied
    /// by a client. The authoritative scheduler relies on it to prevent one player submitting
    /// another player's commands.
    /// <list type="bullet">
    /// <item><description>Messages are whole. A message sent as one call is received as one
    /// <see cref="TryReceive"/>, never split or merged. UDP already gives this per datagram; a
    /// stream transport must frame.</description></item>
    /// <item><description><see cref="SimDelivery.Unreliable"/> is used for redundant input and
    /// canonical-frame traffic.</description></item>
    /// <item><description><see cref="SimDelivery.ReliableOrdered"/> is required for handshakes,
    /// deterministic events, and rebuild snapshots.</description></item>
    /// </list>
    /// </remarks>
    public interface ISimTransport
    {
        /// <summary>The authenticated ID of this endpoint.</summary>
        uint LocalPeerId { get; }

        /// <summary>
        /// Sends a complete message to one endpoint.
        /// </summary>
        /// <param name="recipientId">The authenticated destination peer.</param>
        /// <param name="data">The buffer holding the message.</param>
        /// <param name="offset">Where the message starts in <paramref name="data"/>.</param>
        /// <param name="length">How many bytes the message is.</param>
        /// <param name="delivery">The required delivery guarantee.</param>
        void Send(uint recipientId, byte[] data, int offset, int length, SimDelivery delivery);

        /// <summary>
        /// Hands back the next received message, if any.
        /// </summary>
        /// <param name="message">The sender, delivery path, and complete payload.</param>
        /// <returns>False when nothing is pending.</returns>
        bool TryReceive(out SimTransportMessage message);
    }
}
