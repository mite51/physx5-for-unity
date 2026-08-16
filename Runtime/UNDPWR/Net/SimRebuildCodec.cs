using UNDPWR.Rollback;

namespace UNDPWR.Net
{
    /// <summary>
    /// Serialises a <see cref="SimRebuildState"/> to the wire and back, so the agreed rebuild
    /// state travels over the authoritative session's reliable ordered channel.
    /// </summary>
    /// <remarks>
    /// The rebuild payload is large and loss-sensitive — every peer must apply the exact same
    /// bytes — so unlike inputs it belongs on a reliable transport, and unlike a
    /// input and canonical-frame traffic it must arrive exactly once. The layout is the same
    /// hand-rolled little-endian form as the rest of <see cref="SimByteWriter"/>.
    /// </remarks>
    public static class SimRebuildCodec
    {
        /// <summary>Appends a rebuild payload to a writer, tagged with its message kind.</summary>
        public static void Write(ref SimByteWriter writer, ref SimRebuildState state)
        {
            SimProtocol.WriteHeader(ref writer, SimMessageKind.Rebuild);
            writer.WriteInt32(state.ResumeTick);

            uint[] players = state.PlayerIds ?? EmptyPlayers;
            if (players.Length > SimProtocol.MaxPlayers)
            {
                throw new System.ArgumentException("Rebuild roster exceeds the protocol player limit.", "state");
            }
            writer.WriteUInt16((ushort)players.Length);
            for (int i = 0; i < players.Length; ++i)
            {
                writer.WriteUInt32(players[i]);
            }
            for (int i = 0; i < players.Length; ++i)
            {
                uint sequence = state.LastInputSequences != null
                    && i < state.LastInputSequences.Length
                    ? state.LastInputSequences[i]
                    : 0;
                SimInput input = state.LastInputs != null && i < state.LastInputs.Length
                    ? state.LastInputs[i]
                    : SimInput.Neutral(players[i], state.ResumeTick);
                input.PlayerId = players[i];
                input.Tick = state.ResumeTick;
                writer.WriteUInt32(sequence);
                SimInputCodec.Write(ref writer, input);
            }
            SimAuthoritativeEvent[] events =
                state.PendingEvents ?? new SimAuthoritativeEvent[0];
            if (events.Length > SimProtocol.MaxPendingEvents)
            {
                throw new System.ArgumentException(
                    "Rebuild exceeds the pending-event limit.", "state");
            }
            writer.WriteUInt16((ushort)events.Length);
            for (int i = 0; i < events.Length; ++i)
            {
                SimProtocolCodec.WriteAuthoritativeEvent(ref writer, events[i]);
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
                32 + (state.PlayerIds == null ? 0 : state.PlayerIds.Length * 36)
                + state.PhysicsSize + state.EntitySize + state.GameSize);
            Write(ref writer, ref state);
            return writer.ToArray();
        }

        /// <summary>
        /// Reads a rebuild payload from a reader whose protocol header has already been consumed.
        /// </summary>
        public static SimRebuildState ReadBody(ref SimByteReader reader)
        {
            SimRebuildState state = new SimRebuildState();
            state.ResumeTick = reader.ReadInt32();

            int playerCount = reader.ReadUInt16();
            if (playerCount > SimProtocol.MaxPlayers)
            {
                throw new SimWireFormatException("rebuild roster exceeds the player limit");
            }
            state.PlayerIds = new uint[playerCount];
            for (int i = 0; i < playerCount; ++i)
            {
                state.PlayerIds[i] = reader.ReadUInt32();
            }
            state.LastInputs = new SimInput[playerCount];
            state.LastInputSequences = new uint[playerCount];
            for (int i = 0; i < playerCount; ++i)
            {
                state.LastInputSequences[i] = reader.ReadUInt32();
                state.LastInputs[i] = SimInputCodec.Read(ref reader);
            }
            int eventCount = reader.ReadUInt16();
            if (eventCount > SimProtocol.MaxPendingEvents)
            {
                throw new SimWireFormatException("rebuild exceeds the pending-event limit");
            }
            state.PendingEvents = new SimAuthoritativeEvent[eventCount];
            for (int i = 0; i < eventCount; ++i)
            {
                state.PendingEvents[i] = SimProtocolCodec.ReadAuthoritativeEvent(ref reader);
                if (state.PendingEvents[i].Tick <= state.ResumeTick)
                {
                    throw new SimWireFormatException(
                        "rebuild contains an event at or before its resume tick");
                }
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
            SimMessageKind kind = SimProtocol.ReadHeader(ref reader);
            if (kind != SimMessageKind.Rebuild)
            {
                throw new SimWireFormatException("expected a Rebuild message, got kind " + (byte)kind);
            }
            return ReadBody(ref reader);
        }

        private static readonly uint[] EmptyPlayers = new uint[0];
    }
}
