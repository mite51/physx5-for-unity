using System;
using UNDPWR.Rollback;

namespace UNDPWR.Net
{
    /// <summary>
    /// The kinds of message the framework puts on the wire.
    /// </summary>
    /// <remarks>
    /// The set is deliberately tiny. Inputs are the only <i>simulation</i> data that ever
    /// crosses the network — everything else is recomputed identically by every peer — so the
    /// other two kinds are pure control traffic: one handshake to agree the session is
    /// compatible before a tick runs, and one confirmed-tick hash so a divergence is caught as
    /// a reported mismatch rather than as a slow, unattributable drift.
    /// </remarks>
    public enum SimMessageKind : byte
    {
        /// <summary>Config hash and player set, exchanged once at join.</summary>
        Handshake = 1,

        /// <summary>One or more <see cref="SimInput"/>s. The only simulation data on the wire.</summary>
        Input = 2,

        /// <summary>
        /// A confirmed tick and its three per-channel snapshot hashes, for desync detection.
        /// Peers compare the fold; the parts are carried so a mismatch can name the channel.
        /// </summary>
        Hash = 3,

        /// <summary>
        /// A full synchronised-rebuild payload: the agreed tick, roster and every snapshot
        /// channel, for a mid-match join, a leave, or a desync recovery. Carried on a reliable
        /// path by the game rather than through the best-effort input transport.
        /// </summary>
        Rebuild = 4,

        /// <summary>
        /// Each body's stable ID paired with the PhysX actor index it was given, sent once after
        /// the first step so peers can verify they built the world in the same order. A mismatch
        /// is a determinism bug the config and roster handshake cannot see.
        /// </summary>
        InternalIds = 5,

        /// <summary>
        /// One confirmed tick's per-entity hashes, sent only when a physics disagreement is
        /// detected for that tick, so the peers can name the body that diverged rather than
        /// only the tick.
        /// </summary>
        EntityHashes = 6,
    }

    /// <summary>
    /// Appends primitives to a growable buffer in a fixed little-endian layout.
    /// </summary>
    /// <remarks>
    /// The layout is written by hand rather than through <see cref="BitConverter"/> so it does
    /// not depend on the host's endianness: two peers on different-endian machines would
    /// otherwise serialise the same value to different bytes and reject each other at the
    /// handshake, or worse, mis-read an input. Every multi-byte field is little-endian.
    /// </remarks>
    public struct SimByteWriter
    {
        private byte[] _buffer;
        private int _length;

        /// <summary>Creates a writer with an initial capacity.</summary>
        public SimByteWriter(int capacity)
        {
            _buffer = new byte[capacity < 1 ? 1 : capacity];
            _length = 0;
        }

        /// <summary>How many bytes have been written.</summary>
        public int Length { get { return _length; } }

        private void Ensure(int extra)
        {
            int needed = _length + extra;
            if (_buffer == null)
            {
                _buffer = new byte[needed < 1 ? 1 : needed];
                return;
            }
            if (needed <= _buffer.Length)
            {
                return;
            }
            int size = _buffer.Length;
            while (size < needed)
            {
                size *= 2;
            }
            Array.Resize(ref _buffer, size);
        }

        /// <summary>Writes one byte.</summary>
        public void WriteByte(byte value)
        {
            Ensure(1);
            _buffer[_length++] = value;
        }

        /// <summary>Writes a 16-bit unsigned integer, little-endian.</summary>
        public void WriteUInt16(ushort value)
        {
            Ensure(2);
            _buffer[_length++] = (byte)(value & 0xFF);
            _buffer[_length++] = (byte)((value >> 8) & 0xFF);
        }

        /// <summary>Writes a 32-bit unsigned integer, little-endian.</summary>
        public void WriteUInt32(uint value)
        {
            Ensure(4);
            _buffer[_length++] = (byte)(value & 0xFF);
            _buffer[_length++] = (byte)((value >> 8) & 0xFF);
            _buffer[_length++] = (byte)((value >> 16) & 0xFF);
            _buffer[_length++] = (byte)((value >> 24) & 0xFF);
        }

        /// <summary>Writes a 32-bit signed integer, little-endian.</summary>
        public void WriteInt32(int value)
        {
            WriteUInt32(unchecked((uint)value));
        }

        /// <summary>Writes a 64-bit unsigned integer, little-endian.</summary>
        public void WriteUInt64(ulong value)
        {
            Ensure(8);
            for (int i = 0; i < 8; ++i)
            {
                _buffer[_length++] = (byte)((value >> (i * 8)) & 0xFF);
            }
        }

        /// <summary>Writes a 32-bit float by its IEEE-754 bit pattern.</summary>
        public void WriteSingle(float value)
        {
            WriteUInt32(SimFloatBits.ToBits(value));
        }

        /// <summary>
        /// Writes a length-prefixed byte block: a little-endian 32-bit count followed by that
        /// many bytes, so the reader can recover the block without knowing its size in advance.
        /// </summary>
        public void WriteBytes(byte[] value, int offset, int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException("count");
            }
            WriteInt32(count);
            if (count == 0)
            {
                return;
            }
            if (value == null || offset < 0 || offset + count > value.Length)
            {
                throw new ArgumentOutOfRangeException("count", "byte block runs past the source buffer");
            }
            Ensure(count);
            Array.Copy(value, offset, _buffer, _length, count);
            _length += count;
        }

        /// <summary>Returns a copy of exactly the bytes written.</summary>
        public byte[] ToArray()
        {
            byte[] result = new byte[_length];
            Array.Copy(_buffer, result, _length);
            return result;
        }
    }

    /// <summary>
    /// Reads the primitives <see cref="SimByteWriter"/> writes, in the same little-endian
    /// layout, and refuses to read past the end of the segment it was given.
    /// </summary>
    public struct SimByteReader
    {
        private readonly byte[] _buffer;
        private readonly int _end;
        private int _position;

        /// <summary>Creates a reader over a segment of a buffer.</summary>
        public SimByteReader(byte[] buffer, int offset, int length)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException("buffer");
            }
            if (offset < 0 || length < 0 || offset + length > buffer.Length)
            {
                throw new ArgumentOutOfRangeException("length");
            }
            _buffer = buffer;
            _position = offset;
            _end = offset + length;
        }

        /// <summary>True while there is at least one more byte to read.</summary>
        public bool HasMore { get { return _position < _end; } }

        private void Require(int count)
        {
            if (_position + count > _end)
            {
                throw new SimWireFormatException("message truncated");
            }
        }

        /// <summary>Reads one byte.</summary>
        public byte ReadByte()
        {
            Require(1);
            return _buffer[_position++];
        }

        /// <summary>Reads a little-endian 16-bit unsigned integer.</summary>
        public ushort ReadUInt16()
        {
            Require(2);
            ushort value = (ushort)(_buffer[_position] | (_buffer[_position + 1] << 8));
            _position += 2;
            return value;
        }

        /// <summary>Reads a little-endian 32-bit unsigned integer.</summary>
        public uint ReadUInt32()
        {
            Require(4);
            uint value = (uint)(_buffer[_position]
                | (_buffer[_position + 1] << 8)
                | (_buffer[_position + 2] << 16)
                | (_buffer[_position + 3] << 24));
            _position += 4;
            return value;
        }

        /// <summary>Reads a little-endian 32-bit signed integer.</summary>
        public int ReadInt32()
        {
            return unchecked((int)ReadUInt32());
        }

        /// <summary>Reads a little-endian 64-bit unsigned integer.</summary>
        public ulong ReadUInt64()
        {
            Require(8);
            ulong value = 0;
            for (int i = 0; i < 8; ++i)
            {
                value |= (ulong)_buffer[_position + i] << (i * 8);
            }
            _position += 8;
            return value;
        }

        /// <summary>Reads a 32-bit float from its IEEE-754 bit pattern.</summary>
        public float ReadSingle()
        {
            return SimFloatBits.FromBits(ReadUInt32());
        }

        /// <summary>
        /// Reads a length-prefixed byte block written by <see cref="SimByteWriter.WriteBytes"/>,
        /// returning a freshly allocated array of exactly the written length.
        /// </summary>
        public byte[] ReadBytes()
        {
            int count = ReadInt32();
            if (count < 0)
            {
                throw new SimWireFormatException("negative byte-block length");
            }
            Require(count);
            byte[] result = new byte[count];
            if (count > 0)
            {
                Array.Copy(_buffer, _position, result, 0, count);
                _position += count;
            }
            return result;
        }
    }

    /// <summary>
    /// Reinterprets a float as its 32-bit pattern and back, without allocating and without
    /// depending on API-level differences between Unity's runtimes.
    /// </summary>
    /// <remarks>
    /// Reinterpreting through a pointer yields the canonical IEEE-754 bit-pattern number on
    /// any endianness; the surrounding writer serialises that number little-endian, so the
    /// bytes on the wire are identical across hosts.
    /// </remarks>
    internal static class SimFloatBits
    {
        public static unsafe uint ToBits(float value)
        {
            return *(uint*)&value;
        }

        public static unsafe float FromBits(uint bits)
        {
            return *(float*)&bits;
        }
    }

    /// <summary>
    /// Serialises a <see cref="SimInput"/> to the wire and back, bit-for-bit.
    /// </summary>
    /// <remarks>
    /// The payload is the exact simulation-affecting fields — player, tick, buttons and the
    /// four axes — as raw bits, 28 bytes. It is deliberately <b>not</b> quantised here:
    /// whatever a peer submits locally is what it must send, so any quantisation has to happen
    /// before the input is submitted (see <c>SimInputEncoder</c>), not on the wire, or the
    /// sender would simulate a value its peers never receive. <see cref="SimInput.IsPredicted"/>
    /// is not carried; it is a local bookkeeping flag the receiver always clears on submit.
    /// </remarks>
    public static class SimInputCodec
    {
        /// <summary>The fixed on-wire size of one input, in bytes.</summary>
        public const int Size = 28;

        /// <summary>Appends one input to a writer.</summary>
        public static void Write(ref SimByteWriter writer, SimInput input)
        {
            writer.WriteUInt32(input.PlayerId);
            writer.WriteInt32(input.Tick);
            writer.WriteUInt32(input.Buttons);
            writer.WriteSingle(input.AxisX);
            writer.WriteSingle(input.AxisY);
            writer.WriteSingle(input.AxisZ);
            writer.WriteSingle(input.AxisW);
        }

        /// <summary>Reads one input from a reader.</summary>
        public static SimInput Read(ref SimByteReader reader)
        {
            SimInput input = new SimInput();
            input.PlayerId = reader.ReadUInt32();
            input.Tick = reader.ReadInt32();
            input.Buttons = reader.ReadUInt32();
            input.AxisX = reader.ReadSingle();
            input.AxisY = reader.ReadSingle();
            input.AxisZ = reader.ReadSingle();
            input.AxisW = reader.ReadSingle();
            return input;
        }
    }

    /// <summary>Thrown when an incoming message cannot be parsed.</summary>
    public sealed class SimWireFormatException : Exception
    {
        /// <summary>Creates the exception with a message.</summary>
        public SimWireFormatException(string message) : base(message) { }
    }
}
