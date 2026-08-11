using UNDPWR.Rollback;

namespace UNDPWR.Net
{
    /// <summary>
    /// Serialises a <see cref="SimRebuildState"/> to the wire and back, so the agreed rebuild
    /// state can travel over whatever reliable channel the game already runs.
    /// </summary>
    /// <remarks>
    /// The rebuild payload is large and loss-sensitive — every peer must apply the exact same
    /// bytes — so unlike inputs it belongs on a reliable transport, and unlike a
    /// <see cref="SimMessageKind.Handshake"/> it is not something <see cref="SimSession"/>
    /// puts on the wire itself. The game drives the rebuild handshake (who sends, when every
    /// peer is ready) and uses this codec for the payload. The layout is the same hand-rolled
    /// little-endian form as the rest of <see cref="SimByteWriter"/>, so it is endian-stable.
    /// </remarks>
    public static class SimRebuildCodec
    {
        /// <summary>Appends a rebuild payload to a writer, tagged with its message kind.</summary>
        public static void Write(ref SimByteWriter writer, ref SimRebuildState state)
        {
            writer.WriteByte((byte)SimMessageKind.Rebuild);
            writer.WriteInt32(state.ResumeTick);

            uint[] players = state.PlayerIds ?? EmptyPlayers;
            writer.WriteUInt16((ushort)players.Length);
            for (int i = 0; i < players.Length; ++i)
            {
                writer.WriteUInt32(players[i]);
            }

            writer.WriteUInt64(state.PhysicsHash);
            writer.WriteBytes(state.PhysicsData, 0, state.PhysicsSize);
            writer.WriteBytes(state.EntityData, 0, state.EntitySize);
            writer.WriteBytes(state.GameData, 0, state.GameSize);
        }

        /// <summary>Serialises a rebuild payload to a fresh byte array.</summary>
        public static byte[] Encode(ref SimRebuildState state)
        {
            SimByteWriter writer = new SimByteWriter(
                32 + state.PhysicsSize + state.EntitySize + state.GameSize);
            Write(ref writer, ref state);
            return writer.ToArray();
        }

        /// <summary>
        /// Reads a rebuild payload from a reader whose message-kind byte has already been
        /// consumed and confirmed to be <see cref="SimMessageKind.Rebuild"/>.
        /// </summary>
        public static SimRebuildState ReadBody(ref SimByteReader reader)
        {
            SimRebuildState state = new SimRebuildState();
            state.ResumeTick = reader.ReadInt32();

            int playerCount = reader.ReadUInt16();
            state.PlayerIds = new uint[playerCount];
            for (int i = 0; i < playerCount; ++i)
            {
                state.PlayerIds[i] = reader.ReadUInt32();
            }

            state.PhysicsHash = reader.ReadUInt64();
            state.PhysicsData = reader.ReadBytes();
            state.PhysicsSize = state.PhysicsData.Length;
            state.EntityData = reader.ReadBytes();
            state.EntitySize = state.EntityData.Length;
            state.GameData = reader.ReadBytes();
            state.GameSize = state.GameData.Length;
            return state;
        }

        /// <summary>
        /// Decodes a rebuild payload produced by <see cref="Encode"/>, validating the leading
        /// message-kind byte.
        /// </summary>
        public static SimRebuildState Decode(byte[] bytes, int offset, int length)
        {
            SimByteReader reader = new SimByteReader(bytes, offset, length);
            SimMessageKind kind = (SimMessageKind)reader.ReadByte();
            if (kind != SimMessageKind.Rebuild)
            {
                throw new SimWireFormatException("expected a Rebuild message, got kind " + (byte)kind);
            }
            return ReadBody(ref reader);
        }

        private static readonly uint[] EmptyPlayers = new uint[0];
    }
}
