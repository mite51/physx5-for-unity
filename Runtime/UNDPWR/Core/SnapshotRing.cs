using System;
using UNDPWR.Diagnostics;

namespace UNDPWR.Core
{
    /// <summary>
    /// One tick's captured world state across every channel, plus the hashes and tick
    /// number it belongs to.
    /// </summary>
    /// <remarks>
    /// A snapshot carries three independent channels, captured and restored together:
    /// <list type="bullet">
    /// <item><description><b>Physics</b> (<see cref="Data"/>) — the opaque native blob of
    /// pose, velocity and articulation state, hashed natively during capture into
    /// <see cref="Hash"/>.</description></item>
    /// <item><description><b>Entity</b> (<see cref="EntityData"/>) — per-entity managed
    /// state such as health and timers, written by the gameplay layer through a
    /// <see cref="SimStateWriter"/> and hashed into <see cref="EntityHash"/>.</description></item>
    /// <item><description><b>Game</b> (<see cref="GameData"/>) — the game mode's own
    /// state such as scores and the pending action log, hashed into
    /// <see cref="GameHash"/>.</description></item>
    /// </list>
    /// Keeping the channels and their hashes separate is what lets a desync be attributed
    /// to physics, to a particular entity, or to the game mode, rather than to "something".
    /// The two managed channels are empty for a world driven without a gameplay layer.
    /// </remarks>
    public sealed class Snapshot
    {
        /// <summary>The tick this state was captured at the end of.</summary>
        public int Tick { get; internal set; }

        /// <summary>The captured physics bytes. Only the first <see cref="Size"/> are meaningful.</summary>
        public byte[] Data { get; internal set; }

        /// <summary>How many bytes of <see cref="Data"/> are meaningful.</summary>
        public int Size { get; internal set; }

        /// <summary>The physics state's hash, computed natively during capture.</summary>
        public ulong Hash { get; internal set; }

        /// <summary>The captured entity-channel bytes, or an empty buffer when unused.</summary>
        public byte[] EntityData { get; internal set; }

        /// <summary>How many bytes of <see cref="EntityData"/> are meaningful.</summary>
        public int EntitySize { get; internal set; }

        /// <summary>The entity channel's hash, folded from its bytes during capture.</summary>
        public ulong EntityHash { get; internal set; }

        /// <summary>The captured game-channel bytes, or an empty buffer when unused.</summary>
        public byte[] GameData { get; internal set; }

        /// <summary>How many bytes of <see cref="GameData"/> are meaningful.</summary>
        public int GameSize { get; internal set; }

        /// <summary>The game channel's hash, folded from its bytes during capture.</summary>
        public ulong GameHash { get; internal set; }

        /// <summary>
        /// Whether the server has confirmed every input up to <see cref="Tick"/>, making
        /// this state final rather than predicted.
        /// </summary>
        public bool IsConfirmed { get; internal set; }

        /// <summary>
        /// The three channel hashes folded into one, which is what peers compare to decide
        /// whether they agree.
        /// </summary>
        /// <remarks>
        /// Folding all three every time, including the empty managed channels, keeps the
        /// value well-defined for a physics-only world: the two managed hashes are their
        /// zero-length hash and contribute the same constant on every peer.
        /// </remarks>
        public ulong CombinedHash
        {
            get
            {
                ulong hash = SimHash.OffsetBasis;
                hash = SimHash.Combine(hash, Hash);
                hash = SimHash.Combine(hash, EntityHash);
                hash = SimHash.Combine(hash, GameHash);
                return hash;
            }
        }

        internal Snapshot(int capacity)
        {
            Data = new byte[capacity];
            EntityData = new byte[0];
            GameData = new byte[0];
            Tick = -1;
        }

        /// <summary>Copies the physics payload into a fresh array, for sending or archiving.</summary>
        public byte[] ToArray()
        {
            byte[] copy = new byte[Size];
            Buffer.BlockCopy(Data, 0, copy, 0, Size);
            return copy;
        }
    }

    /// <summary>
    /// A fixed-size ring of recent snapshots, one per tick.
    /// </summary>
    /// <remarks>
    /// Sized once at construction and never grown during a session, because allocating
    /// during a rollback would drop a frame at exactly the moment the simulation is
    /// already doing the most work. The ring holds
    /// <see cref="SimConfig.SnapshotHistory"/> ticks, which bounds how far back a late
    /// input can still be applied: an input older than the ring cannot be honoured,
    /// because the state it would have to be applied to is gone.
    /// <para>
    /// Capacity must exceed <see cref="SimConfig.PredictionHorizon"/>, which
    /// <see cref="SimConfig.Validate"/> enforces, otherwise the tick a rollback needs to
    /// rewind to has already been overwritten by the prediction that followed it.
    /// </para>
    /// </remarks>
    public sealed class SnapshotRing
    {
        private readonly Snapshot[] _slots;
        private int _newestTick = -1;

        /// <summary>How many ticks the ring holds.</summary>
        public int Capacity { get { return _slots.Length; } }

        /// <summary>The most recent tick stored, or -1 when the ring is empty.</summary>
        public int NewestTick { get { return _newestTick; } }

        /// <summary>
        /// The oldest tick still retrievable, or -1 when the ring is empty.
        /// </summary>
        public int OldestTick
        {
            get
            {
                if (_newestTick < 0) return -1;
                int oldest = _newestTick - _slots.Length + 1;
                return oldest < 0 ? 0 : oldest;
            }
        }

        /// <summary>
        /// Creates a ring.
        /// </summary>
        /// <param name="capacity">How many ticks to retain. Must be positive.</param>
        /// <param name="initialStateSize">
        /// Expected snapshot size, used to preallocate. Buffers grow on demand if the
        /// registry later gets larger, which is why this only needs to be close.
        /// </param>
        public SnapshotRing(int capacity, int initialStateSize)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException("capacity", "A snapshot ring needs at least one slot.");
            }

            _slots = new Snapshot[capacity];
            for (int i = 0; i < capacity; ++i)
            {
                _slots[i] = new Snapshot(initialStateSize);
            }
        }

        /// <summary>
        /// Returns the slot a tick should be written into, ready to be filled.
        /// </summary>
        /// <remarks>
        /// Hands back the buffer rather than taking one, so that a capture writes
        /// straight into the ring instead of into a temporary that then has to be copied.
        /// The returned object is owned by the ring and is reused once the tick falls out
        /// of history.
        /// </remarks>
        public Snapshot BeginWrite(int tick)
        {
            if (tick < 0)
            {
                throw new ArgumentOutOfRangeException("tick", "Tick numbers start at zero.");
            }

            Snapshot slot = _slots[Index(tick)];
            slot.Tick = tick;
            slot.Size = 0;
            slot.Hash = 0;
            slot.EntitySize = 0;
            slot.EntityHash = 0;
            slot.GameSize = 0;
            slot.GameHash = 0;
            slot.IsConfirmed = false;
            return slot;
        }

        /// <summary>
        /// Publishes a slot returned by <see cref="BeginWrite"/> once it has been filled.
        /// </summary>
        public void CompleteWrite(Snapshot slot, int size, ulong hash)
        {
            if (slot == null)
            {
                throw new ArgumentNullException("slot");
            }

            slot.Size = size;
            slot.Hash = hash;
            if (slot.Tick > _newestTick)
            {
                _newestTick = slot.Tick;
            }
        }

        /// <summary>
        /// Retrieves a tick's snapshot.
        /// </summary>
        /// <returns>
        /// False when the tick has fallen out of history or was never written. A caller
        /// that gets false cannot roll back that far and must resynchronise instead.
        /// </returns>
        public bool TryGet(int tick, out Snapshot snapshot)
        {
            snapshot = null;
            if (tick < 0 || _newestTick < 0 || tick > _newestTick || tick < OldestTick)
            {
                return false;
            }

            Snapshot candidate = _slots[Index(tick)];

            // The slot may have been recycled for a newer tick already.
            if (candidate.Tick != tick || candidate.Size == 0)
            {
                return false;
            }

            snapshot = candidate;
            return true;
        }

        /// <summary>True when a tick is still retrievable.</summary>
        public bool Contains(int tick)
        {
            Snapshot ignored;
            return TryGet(tick, out ignored);
        }

        /// <summary>
        /// Marks every tick up to and including <paramref name="tick"/> as confirmed.
        /// </summary>
        /// <remarks>
        /// A confirmed snapshot is one whose inputs are all final, so it will never be
        /// resimulated and its hash can be compared bit-exactly against other peers.
        /// </remarks>
        public void MarkConfirmedThrough(int tick)
        {
            for (int t = OldestTick; t <= tick && t <= _newestTick; ++t)
            {
                Snapshot slot;
                if (TryGet(t, out slot))
                {
                    slot.IsConfirmed = true;
                }
            }
        }

        /// <summary>
        /// Discards everything, for a synchronised rebuild where the whole timeline is
        /// being replaced.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _slots.Length; ++i)
            {
                _slots[i].Tick = -1;
                _slots[i].Size = 0;
                _slots[i].Hash = 0;
                _slots[i].EntitySize = 0;
                _slots[i].EntityHash = 0;
                _slots[i].GameSize = 0;
                _slots[i].GameHash = 0;
                _slots[i].IsConfirmed = false;
            }
            _newestTick = -1;
            SimLog.Info("Snapshot ring cleared");
        }

        private int Index(int tick)
        {
            return tick % _slots.Length;
        }
    }
}
