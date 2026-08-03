using System;
using UnityEngine;

namespace UNDPWR.Core
{
    /// <summary>
    /// Writes managed simulation state into a byte buffer while folding it into a hash.
    /// </summary>
    /// <remarks>
    /// The gameplay layer needs its own state in the rollback snapshot alongside the
    /// physics blob: health, timers, scores, the pending action log. That state is
    /// captured and restored on the same schedule as physics, which means several times
    /// per frame during a replay, so the path must not allocate and must produce a hash
    /// that is comparable byte-for-byte against another peer.
    /// <para>
    /// This is deliberately a cursor over a caller-owned buffer rather than a
    /// <c>BinaryWriter</c> or a list of boxed values. Each write appends the value's raw
    /// unmanaged bytes and folds those same bytes into a running
    /// <see cref="SimHash"/>, so the buffer and the hash always agree and the hash needs
    /// no second pass. The buffer grows if a capture outgrows it, and the grown array is
    /// exposed through <see cref="Buffer"/> so the caller can retain it for reuse; because
    /// this is a struct, pass it by <c>ref</c> to anything that writes, or the grown
    /// buffer and advanced position are lost.
    /// </para>
    /// <para>
    /// Only <c>unmanaged</c> values are writable, and they are stored in native memory
    /// layout. That is safe here for the same reason the rest of the framework is: peers
    /// must share a CPU architecture, so the layout is identical on every peer that is
    /// allowed to play together at all.
    /// </para>
    /// </remarks>
    public struct SimStateWriter
    {
        private byte[] _buffer;
        private int _position;
        private ulong _hash;

        /// <summary>Creates a writer over an existing buffer, which may be null or empty.</summary>
        /// <param name="buffer">
        /// The buffer to write into, reused across captures. Grown automatically if a
        /// capture needs more room; read the possibly-new array back from
        /// <see cref="Buffer"/> afterwards.
        /// </param>
        public SimStateWriter(byte[] buffer)
        {
            _buffer = buffer;
            _position = 0;
            _hash = SimHash.OffsetBasis;
        }

        /// <summary>The buffer written into, which may differ from the one passed in if it grew.</summary>
        public byte[] Buffer { get { return _buffer; } }

        /// <summary>How many bytes have been written.</summary>
        public int Position { get { return _position; } }

        /// <summary>The FNV-1a hash of everything written so far.</summary>
        public ulong Hash { get { return _hash; } }

        /// <summary>Appends an unmanaged value in native layout and folds it into the hash.</summary>
        public unsafe void Write<T>(T value) where T : unmanaged
        {
            int size = sizeof(T);
            EnsureCapacity(size);
            fixed (byte* dst = &_buffer[_position])
            {
                *(T*)dst = value;
                for (int i = 0; i < size; ++i)
                {
                    _hash = SimHash.Combine(_hash, dst[i]);
                }
            }
            _position += size;
        }

        /// <summary>Appends a length-prefixed run of unmanaged values.</summary>
        /// <remarks>
        /// The length prefix is written first so <see cref="SimStateReader.ReadArray{T}"/>
        /// knows how many to read back. Used for the handful of variable-length pieces of
        /// gameplay state, such as team scores or a per-tick contact list, whose count is
        /// small and bounded but not fixed.
        /// </remarks>
        public void WriteArray<T>(T[] array, int count) where T : unmanaged
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException("count");
            }
            if (array == null && count > 0)
            {
                throw new ArgumentNullException("array");
            }

            Write(count);
            for (int i = 0; i < count; ++i)
            {
                Write(array[i]);
            }
        }

        /// <summary>Appends a signed 32-bit integer.</summary>
        public void WriteInt(int value) { Write(value); }

        /// <summary>Appends an unsigned 32-bit integer.</summary>
        public void WriteUInt(uint value) { Write(value); }

        /// <summary>Appends a single-precision float, by its exact bits.</summary>
        public void WriteFloat(float value) { Write(value); }

        /// <summary>Appends a boolean as one byte.</summary>
        public void WriteBool(bool value) { Write(value ? (byte)1 : (byte)0); }

        /// <summary>Appends a <see cref="Vector3"/>.</summary>
        public void WriteVector3(Vector3 value) { Write(value); }

        /// <summary>Appends a <see cref="Quaternion"/>.</summary>
        public void WriteQuaternion(Quaternion value) { Write(value); }

        private void EnsureCapacity(int extra)
        {
            int needed = _position + extra;
            if (_buffer == null)
            {
                _buffer = new byte[needed < 64 ? 64 : needed];
                return;
            }
            if (_buffer.Length < needed)
            {
                int capacity = _buffer.Length == 0 ? 64 : _buffer.Length;
                while (capacity < needed)
                {
                    capacity *= 2;
                }
                Array.Resize(ref _buffer, capacity);
            }
        }
    }

    /// <summary>
    /// Reads managed simulation state back out of a buffer written by
    /// <see cref="SimStateWriter"/>.
    /// </summary>
    /// <remarks>
    /// Reads must mirror the writes exactly, in the same order and the same types, or the
    /// cursor drifts and every value after the mistake is garbage. That coupling is the
    /// price of an allocation-free format with no self-describing tags; it is the same
    /// contract a <c>BinaryReader</c>/<c>BinaryWriter</c> pair has. Like the writer, this
    /// is a struct and must be passed by <c>ref</c> to keep the cursor advancing.
    /// </remarks>
    public struct SimStateReader
    {
        private readonly byte[] _buffer;
        private readonly int _size;
        private int _position;

        /// <summary>Creates a reader over the meaningful prefix of a buffer.</summary>
        /// <param name="buffer">The buffer to read from.</param>
        /// <param name="size">How many bytes of it are meaningful.</param>
        public SimStateReader(byte[] buffer, int size)
        {
            _buffer = buffer;
            _size = size;
            _position = 0;
        }

        /// <summary>How many bytes have been read.</summary>
        public int Position { get { return _position; } }

        /// <summary>How many meaningful bytes remain.</summary>
        public int Remaining { get { return _size - _position; } }

        /// <summary>Reads one unmanaged value in native layout.</summary>
        public unsafe T Read<T>() where T : unmanaged
        {
            int size = sizeof(T);
            if (_position + size > _size)
            {
                throw new InvalidOperationException(string.Format(
                    "SimStateReader ran off the end: needed {0} bytes at position {1} of {2}. The read sequence " +
                    "does not match the write sequence.", size, _position, _size));
            }

            T value;
            fixed (byte* src = &_buffer[_position])
            {
                value = *(T*)src;
            }
            _position += size;
            return value;
        }

        /// <summary>Reads a length-prefixed run of unmanaged values into a destination.</summary>
        /// <param name="destination">
        /// Receives the values. Must be large enough for the stored count.
        /// </param>
        /// <returns>How many values were read.</returns>
        public int ReadArray<T>(T[] destination) where T : unmanaged
        {
            int count = Read<int>();
            if (destination == null || destination.Length < count)
            {
                throw new ArgumentException(string.Format(
                    "Destination array holds {0} but the buffer stored {1} elements",
                    destination == null ? 0 : destination.Length, count));
            }
            for (int i = 0; i < count; ++i)
            {
                destination[i] = Read<T>();
            }
            return count;
        }

        /// <summary>Reads a signed 32-bit integer.</summary>
        public int ReadInt() { return Read<int>(); }

        /// <summary>Reads an unsigned 32-bit integer.</summary>
        public uint ReadUInt() { return Read<uint>(); }

        /// <summary>Reads a single-precision float.</summary>
        public float ReadFloat() { return Read<float>(); }

        /// <summary>Reads a boolean stored as one byte.</summary>
        public bool ReadBool() { return Read<byte>() != 0; }

        /// <summary>Reads a <see cref="Vector3"/>.</summary>
        public Vector3 ReadVector3() { return Read<Vector3>(); }

        /// <summary>Reads a <see cref="Quaternion"/>.</summary>
        public Quaternion ReadQuaternion() { return Read<Quaternion>(); }
    }
}
