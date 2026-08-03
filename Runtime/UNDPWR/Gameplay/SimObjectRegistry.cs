using System;
using System.Collections.Generic;
using UNDPWR.Core;
using UNDPWR.Diagnostics;

namespace UNDPWR.Gameplay
{
    /// <summary>
    /// The set of gameplay entities in a world, iterable in a stable-ID order that is the
    /// same on every peer.
    /// </summary>
    /// <remarks>
    /// Iteration order is the whole reason this exists rather than a bare dictionary. The
    /// entity channel is written one entity at a time, and every peer must write the same
    /// entities in the same order or the channel's bytes disagree and restore reads the
    /// wrong fields. Ordering by stable ID gives an order that does not depend on spawn
    /// order, join order or anything a peer decides locally.
    /// <para>
    /// The pool preregisters a fixed set at session start and never adds or removes after,
    /// so the sort happens once. Entities that come and go flip their active flag instead of
    /// leaving the registry, which keeps the channel layout constant — the same property the
    /// physics snapshot relies on for its own layout.
    /// </para>
    /// </remarks>
    public sealed class SimObjectRegistry
    {
        private static readonly Comparison<SimGameEntity> ByStableId = CompareByStableId;

        private readonly Dictionary<uint, SimGameEntity> _byId = new Dictionary<uint, SimGameEntity>();
        private readonly List<SimGameEntity> _ordered = new List<SimGameEntity>();
        private bool _needsSort;

        /// <summary>How many entities are registered.</summary>
        public int Count { get { return _ordered.Count; } }

        /// <summary>Every entity, in ascending stable-ID order.</summary>
        public IReadOnlyList<SimGameEntity> Ordered
        {
            get
            {
                EnsureSorted();
                return _ordered;
            }
        }

        /// <summary>Adds an entity. Ordering is re-established lazily before the next iteration.</summary>
        public void Register(SimGameEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException("entity");
            }
            if (_byId.ContainsKey(entity.StableId))
            {
                throw new InvalidOperationException(string.Format(
                    "Stable ID {0} is already registered; two entities would share an identity", entity.StableId));
            }

            _byId.Add(entity.StableId, entity);
            _ordered.Add(entity);
            _needsSort = true;
        }

        /// <summary>Removes an entity. Rare — prefer flipping its active flag.</summary>
        /// <remarks>
        /// Unregistering changes the entity channel's layout, which every peer would have to
        /// agree on, exactly the commitment the physics layer warns about. Pooling and the
        /// active flag exist so this almost never happens.
        /// </remarks>
        public bool Unregister(uint stableId)
        {
            SimGameEntity entity;
            if (!_byId.TryGetValue(stableId, out entity))
            {
                return false;
            }
            _byId.Remove(stableId);
            _ordered.Remove(entity);
            SimLog.Warning(string.Format(
                "Entity {0} unregistered; the entity-channel layout has changed and every peer must agree on it",
                stableId));
            return true;
        }

        /// <summary>Looks up an entity by stable ID.</summary>
        public bool TryGet(uint stableId, out SimGameEntity entity)
        {
            return _byId.TryGetValue(stableId, out entity);
        }

        /// <summary>Writes every entity's state into the entity channel, in stable-ID order.</summary>
        public void CaptureAll(ref SimStateWriter writer)
        {
            EnsureSorted();
            for (int i = 0; i < _ordered.Count; ++i)
            {
                ((ISimEntityState)_ordered[i]).CaptureEntityState(ref writer);
            }
        }

        /// <summary>Reads every entity's state back, in the same stable-ID order.</summary>
        public void RestoreAll(ref SimStateReader reader)
        {
            EnsureSorted();
            for (int i = 0; i < _ordered.Count; ++i)
            {
                ((ISimEntityState)_ordered[i]).RestoreEntityState(ref reader);
            }
        }

        private void EnsureSorted()
        {
            if (_needsSort)
            {
                _ordered.Sort(ByStableId);
                _needsSort = false;
            }
        }

        private static int CompareByStableId(SimGameEntity a, SimGameEntity b)
        {
            return a.StableId.CompareTo(b.StableId);
        }
    }
}
