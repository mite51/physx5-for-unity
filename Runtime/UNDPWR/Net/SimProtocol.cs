using System;
using UNDPWR.Core;
using UNDPWR.Rollback;

namespace UNDPWR.Net
{
    /// <summary>Constants and common framing for the authoritative wire protocol.</summary>
    public static class SimProtocol
    {
        /// <summary>The only protocol version understood by this build.</summary>
        public const ushort Version = 2;

        public const int MaxPlayers = 256;
        public const int MaxEventPayloadBytes = 4096;
        public const int MaxPendingEvents = 4096;

        /// <summary>The reserved transport peer ID of the authoritative server.</summary>
        public const uint ServerPeerId = 0;

        /// <summary>Writes the common kind and version prefix.</summary>
        public static void WriteHeader(ref SimByteWriter writer, SimMessageKind kind)
        {
            writer.WriteByte((byte)kind);
            writer.WriteUInt16(Version);
        }

        /// <summary>Reads and validates the common prefix.</summary>
        public static SimMessageKind ReadHeader(ref SimByteReader reader)
        {
            SimMessageKind kind = (SimMessageKind)reader.ReadByte();
            ushort version = reader.ReadUInt16();
            if (version != Version)
            {
                throw new SimWireFormatException(string.Format(
                    "protocol version {0} is incompatible with required version {1}", version, Version));
            }
            return kind;
        }
    }

    /// <summary>The authoritative server's disposition of a proposed command.</summary>
    public enum SimAdmissionDisposition : byte
    {
        Accepted = 0,
        Retimed = 1,
        Rejected = 2
    }

    /// <summary>Why an input or event proposal was rejected.</summary>
    public enum SimAdmissionRejection : byte
    {
        None = 0,
        UnknownPlayer = 1,
        WrongPlayer = 2,
        DuplicateSequence = 3,
        TooFarInFuture = 4,
        Malformed = 5
    }

    /// <summary>A client's request to apply a sampled input on a future server tick.</summary>
    public struct SimInputProposal
    {
        public uint Sequence;
        public int RequestedTick;
        public long CapturedAtMicroseconds;
        public SimInput Input;
    }

    /// <summary>The server's final scheduling result for one proposal.</summary>
    public struct SimInputDecision
    {
        public uint PlayerId;
        public uint Sequence;
        public int RequestedTick;
        public int AssignedTick;
        public SimAdmissionDisposition Disposition;
        public SimAdmissionRejection Rejection;
    }

    /// <summary>One player's canonical command within a finalized server tick.</summary>
    public struct SimCanonicalInput
    {
        public uint Sequence;
        public int RequestedTick;
        public SimInput Input;
    }

    /// <summary>Every canonical player command for one finalized server tick.</summary>
    public sealed class SimCanonicalFrame
    {
        public uint Epoch;
        public int Tick;
        public SimCanonicalInput[] Inputs;
        public SimAuthoritativeEvent[] Events;
    }

    /// <summary>A deterministic gameplay-event proposal.</summary>
    public struct SimEventProposal
    {
        public uint Sequence;
        public int RequestedTick;
        public ushort TypeId;
        public byte[] Payload;
    }

    /// <summary>An authoritative deterministic-event assignment.</summary>
    public struct SimEventDecision
    {
        public uint PlayerId;
        public uint Sequence;
        public int RequestedTick;
        public int AssignedTick;
        public SimAdmissionDisposition Disposition;
        public SimAdmissionRejection Rejection;
        public ushort TypeId;
        public byte[] Payload;
    }

    /// <summary>Codec for authoritative input and event messages.</summary>
    public static class SimProtocolCodec
    {
        public static byte[] EncodeInputProposal(ref SimInputProposal proposal)
        {
            SimByteWriter writer = new SimByteWriter(48);
            SimProtocol.WriteHeader(ref writer, SimMessageKind.InputProposal);
            writer.WriteUInt32(proposal.Sequence);
            writer.WriteInt32(proposal.RequestedTick);
            writer.WriteUInt64(unchecked((ulong)proposal.CapturedAtMicroseconds));
            SimInputCodec.Write(ref writer, proposal.Input);
            return writer.ToArray();
        }

        public static SimInputProposal ReadInputProposal(ref SimByteReader reader)
        {
            SimInputProposal proposal = new SimInputProposal();
            proposal.Sequence = reader.ReadUInt32();
            proposal.RequestedTick = reader.ReadInt32();
            proposal.CapturedAtMicroseconds = unchecked((long)reader.ReadUInt64());
            proposal.Input = SimInputCodec.Read(ref reader);
            return proposal;
        }

        public static byte[] EncodeInputDecision(ref SimInputDecision decision)
        {
            SimByteWriter writer = new SimByteWriter(24);
            SimProtocol.WriteHeader(ref writer, SimMessageKind.InputDecision);
            writer.WriteUInt32(decision.PlayerId);
            writer.WriteUInt32(decision.Sequence);
            writer.WriteInt32(decision.RequestedTick);
            writer.WriteInt32(decision.AssignedTick);
            writer.WriteByte((byte)decision.Disposition);
            writer.WriteByte((byte)decision.Rejection);
            return writer.ToArray();
        }

        public static SimInputDecision ReadInputDecision(ref SimByteReader reader)
        {
            SimInputDecision decision = new SimInputDecision();
            decision.PlayerId = reader.ReadUInt32();
            decision.Sequence = reader.ReadUInt32();
            decision.RequestedTick = reader.ReadInt32();
            decision.AssignedTick = reader.ReadInt32();
            decision.Disposition = (SimAdmissionDisposition)reader.ReadByte();
            decision.Rejection = (SimAdmissionRejection)reader.ReadByte();
            return decision;
        }

        public static byte[] EncodeCanonicalFrame(SimCanonicalFrame frame)
        {
            if (frame == null || frame.Inputs == null)
            {
                throw new ArgumentNullException("frame");
            }
            if (frame.Inputs.Length > SimProtocol.MaxPlayers)
            {
                throw new ArgumentException("Canonical frame exceeds the player limit.", "frame");
            }
            SimAuthoritativeEvent[] events = frame.Events ?? new SimAuthoritativeEvent[0];
            if (events.Length > SimProtocol.MaxPendingEvents)
            {
                throw new ArgumentException("Canonical frame exceeds the event limit.", "frame");
            }
            SimByteWriter writer = new SimByteWriter(18 + frame.Inputs.Length * 36);
            SimProtocol.WriteHeader(ref writer, SimMessageKind.CanonicalFrame);
            writer.WriteUInt32(frame.Epoch);
            writer.WriteInt32(frame.Tick);
            writer.WriteUInt16((ushort)frame.Inputs.Length);
            for (int i = 0; i < frame.Inputs.Length; ++i)
            {
                writer.WriteUInt32(frame.Inputs[i].Sequence);
                writer.WriteInt32(frame.Inputs[i].RequestedTick);
                SimInputCodec.Write(ref writer, frame.Inputs[i].Input);
            }
            writer.WriteUInt16((ushort)events.Length);
            for (int i = 0; i < events.Length; ++i)
            {
                WriteAuthoritativeEvent(ref writer, events[i]);
            }
            return writer.ToArray();
        }

        public static SimCanonicalFrame ReadCanonicalFrame(ref SimByteReader reader)
        {
            SimCanonicalFrame frame = new SimCanonicalFrame();
            frame.Epoch = reader.ReadUInt32();
            frame.Tick = reader.ReadInt32();
            int count = reader.ReadUInt16();
            if (count > SimProtocol.MaxPlayers)
            {
                throw new SimWireFormatException("canonical frame exceeds the player limit");
            }
            frame.Inputs = new SimCanonicalInput[count];
            for (int i = 0; i < count; ++i)
            {
                frame.Inputs[i].Sequence = reader.ReadUInt32();
                frame.Inputs[i].RequestedTick = reader.ReadInt32();
                frame.Inputs[i].Input = SimInputCodec.Read(ref reader);
            }
            int eventCount = reader.ReadUInt16();
            if (eventCount > SimProtocol.MaxPendingEvents)
            {
                throw new SimWireFormatException("canonical frame exceeds the event limit");
            }
            frame.Events = new SimAuthoritativeEvent[eventCount];
            for (int i = 0; i < eventCount; ++i)
            {
                frame.Events[i] = ReadAuthoritativeEvent(ref reader);
                if (frame.Events[i].Tick != frame.Tick)
                {
                    throw new SimWireFormatException(
                        "canonical frame contains an event assigned to another tick");
                }
            }
            return frame;
        }

        internal static void WriteAuthoritativeEvent(
            ref SimByteWriter writer, SimAuthoritativeEvent command)
        {
            byte[] payload = command.Payload ?? new byte[0];
            if (payload.Length > SimProtocol.MaxEventPayloadBytes)
            {
                throw new ArgumentException("Event payload exceeds the protocol limit.", "command");
            }
            writer.WriteUInt32(command.PlayerId);
            writer.WriteUInt32(command.Sequence);
            writer.WriteInt32(command.Tick);
            writer.WriteUInt16(command.TypeId);
            writer.WriteBytes(payload, 0, payload.Length);
        }

        internal static SimAuthoritativeEvent ReadAuthoritativeEvent(ref SimByteReader reader)
        {
            SimAuthoritativeEvent command = new SimAuthoritativeEvent();
            command.PlayerId = reader.ReadUInt32();
            command.Sequence = reader.ReadUInt32();
            command.Tick = reader.ReadInt32();
            command.TypeId = reader.ReadUInt16();
            command.Payload = reader.ReadBytes(SimProtocol.MaxEventPayloadBytes);
            return command;
        }

        public static byte[] EncodeEventProposal(ref SimEventProposal proposal)
        {
            byte[] payload = proposal.Payload ?? new byte[0];
            if (payload.Length > SimProtocol.MaxEventPayloadBytes)
            {
                throw new ArgumentException("Event payload exceeds the protocol limit.", "proposal");
            }
            SimByteWriter writer = new SimByteWriter(20 + payload.Length);
            SimProtocol.WriteHeader(ref writer, SimMessageKind.EventProposal);
            writer.WriteUInt32(proposal.Sequence);
            writer.WriteInt32(proposal.RequestedTick);
            writer.WriteUInt16(proposal.TypeId);
            writer.WriteBytes(payload, 0, payload.Length);
            return writer.ToArray();
        }

        public static SimEventProposal ReadEventProposal(ref SimByteReader reader)
        {
            SimEventProposal proposal = new SimEventProposal();
            proposal.Sequence = reader.ReadUInt32();
            proposal.RequestedTick = reader.ReadInt32();
            proposal.TypeId = reader.ReadUInt16();
            proposal.Payload = reader.ReadBytes(SimProtocol.MaxEventPayloadBytes);
            return proposal;
        }

        public static byte[] EncodeEventDecision(ref SimEventDecision decision)
        {
            byte[] payload = decision.Payload ?? new byte[0];
            if (payload.Length > SimProtocol.MaxEventPayloadBytes)
            {
                throw new ArgumentException("Event payload exceeds the protocol limit.", "decision");
            }
            SimByteWriter writer = new SimByteWriter(28 + payload.Length);
            SimProtocol.WriteHeader(ref writer, SimMessageKind.EventDecision);
            writer.WriteUInt32(decision.PlayerId);
            writer.WriteUInt32(decision.Sequence);
            writer.WriteInt32(decision.RequestedTick);
            writer.WriteInt32(decision.AssignedTick);
            writer.WriteByte((byte)decision.Disposition);
            writer.WriteByte((byte)decision.Rejection);
            writer.WriteUInt16(decision.TypeId);
            writer.WriteBytes(payload, 0, payload.Length);
            return writer.ToArray();
        }

        public static SimEventDecision ReadEventDecision(ref SimByteReader reader)
        {
            SimEventDecision decision = new SimEventDecision();
            decision.PlayerId = reader.ReadUInt32();
            decision.Sequence = reader.ReadUInt32();
            decision.RequestedTick = reader.ReadInt32();
            decision.AssignedTick = reader.ReadInt32();
            decision.Disposition = (SimAdmissionDisposition)reader.ReadByte();
            decision.Rejection = (SimAdmissionRejection)reader.ReadByte();
            decision.TypeId = reader.ReadUInt16();
            decision.Payload = reader.ReadBytes(SimProtocol.MaxEventPayloadBytes);
            return decision;
        }
    }
}
