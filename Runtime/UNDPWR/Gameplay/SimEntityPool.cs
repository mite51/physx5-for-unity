using System;
using System.Collections.Generic;
using UnityEngine;
using UNDPWR.Core;
using UNDPWR.Diagnostics;
using UNDPWR.Interop;

namespace UNDPWR.Gameplay
{
    /// <summary>
    /// A fixed pool of preregistered entities, spawned by enabling a dormant instance rather
    /// than by creating one.
    /// </summary>
    /// <remarks>
    /// This is deliberately simpler than the pool it replaces, and the rollback model is why.
    /// Every instance is created and registered once at session start, in stable-ID order, so
    /// the snapshot layout never changes during a session; spawning is
    /// <see cref="DeterministicWorld.SetEntityEnabled"/> plus a teleport, and despawning is
    /// the same in reverse. There is no creation log to replay for a joining peer, because
    /// nothing is ever created after setup, and there is no free list to keep in sync,
    /// because "which instances are free" is derived from the entities' own active flags,
    /// which are in the entity channel and therefore already roll back.
    /// <para>
    /// That last point is what makes spawn allocation deterministic without storing anything:
    /// <see cref="Spawn"/> hands out the lowest-ID inactive instance of the requested kind,
    /// which is a pure function of state every peer has restored identically. Two peers
    /// spawning from the same state pick the same instance without exchanging a message.
    /// </para>
    /// </remarks>
    public sealed class SimEntityPool
    {
        private sealed class PoolGroup
        {
            public string Key;
            public SimGameEntity[] Instances;
        }

        private sealed class PendingConfig
        {
            public string Key;
            public SimGameEntity Prefab;
            public int Count;
        }

        private readonly SimContext _context;
        private readonly List<PendingConfig> _pending = new List<PendingConfig>();
        private readonly Dictionary<string, PoolGroup> _groups = new Dictionary<string, PoolGroup>();
        private readonly List<SimGameEntity> _all = new List<SimGameEntity>();
        private bool _built;

        /// <summary>Creates a pool for a context.</summary>
        public SimEntityPool(SimContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }
            _context = context;
        }

        /// <summary>
        /// Configures a group of pooled instances of one prefab. Call before
        /// <see cref="Preregister"/>; every peer must add the same groups in the same order.
        /// </summary>
        /// <param name="key">The name gameplay spawns this prefab by.</param>
        /// <param name="prefab">The entity prefab to clone.</param>
        /// <param name="count">How many instances to keep, the hard cap on concurrent spawns.</param>
        public void Add(string key, SimGameEntity prefab, int count)
        {
            if (_built)
            {
                throw new InvalidOperationException("Pool groups must be added before Preregister");
            }
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("key must be non-empty", "key");
            }
            if (prefab == null)
            {
                throw new ArgumentNullException("prefab");
            }
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException("count", "A pool group needs at least one instance");
            }
            _pending.Add(new PendingConfig { Key = key, Prefab = prefab, Count = count });
        }

        /// <summary>
        /// Instantiates every configured instance, registers it with the world and the
        /// registry, and leaves it dormant.
        /// </summary>
        /// <remarks>
        /// Registration is deferred insertion, so the actors do not reach the scene until the
        /// engine commits. Call <see cref="DisableAllInitially"/> after that commit and before
        /// the first capture, so tick zero records every pooled instance as disabled and
        /// inactive.
        /// </remarks>
        public void Preregister()
        {
            if (_built)
            {
                throw new InvalidOperationException("Preregister was already called");
            }
            _built = true;

            for (int c = 0; c < _pending.Count; ++c)
            {
                PendingConfig config = _pending[c];
                SimGameEntity[] instances = new SimGameEntity[config.Count];

                for (int i = 0; i < config.Count; ++i)
                {
                    SimGameEntity instance = UnityEngine.Object.Instantiate(config.Prefab);
                    instance.name = string.Format("{0}#{1}", config.Key, i);

                    SimHandleKind kind;
                    IntPtr handle = instance.ResolveNativeHandle(out kind);
                    if (handle == IntPtr.Zero)
                    {
                        throw new InvalidOperationException(string.Format(
                            "Pooled prefab '{0}' returned a null native handle from ResolveNativeHandle", config.Key));
                    }

                    uint id = _context.Ids.Allocate(0);
                    SimEntity registration = _context.World.Register(id, handle, kind);
                    instance.Bind(id, registration, _context);
                    _context.Registry.Register(instance);

                    instances[i] = instance;
                    _all.Add(instance);
                }

                _groups.Add(config.Key, new PoolGroup { Key = config.Key, Instances = instances });
            }

            _pending.Clear();
            SimLog.Info(string.Format("Pool preregistered {0} instance(s) across {1} group(s)",
                _all.Count, _groups.Count));
        }

        /// <summary>
        /// Disables every pooled instance in the world, after the initial commit. Their
        /// disabled state is then captured at tick zero and replays like any other state.
        /// </summary>
        public void DisableAllInitially()
        {
            for (int i = 0; i < _all.Count; ++i)
            {
                _context.World.SetEntityEnabled(_all[i].StableId, false);
            }
        }

        /// <summary>
        /// Brings the lowest-ID dormant instance of a group into play at a pose.
        /// </summary>
        /// <returns>The spawned entity, or null when the group is exhausted.</returns>
        public SimGameEntity Spawn(string key, Vector3 position, Quaternion rotation)
        {
            return Spawn(key, position, rotation, SimGameEntity.NoOwner);
        }

        /// <summary>
        /// Brings the lowest-ID dormant instance of a group into play at a pose, recording
        /// the spawning owner for <see cref="SimGameEntity.OnSimSpawn"/> to read.
        /// </summary>
        /// <returns>The spawned entity, or null when the group is exhausted.</returns>
        public SimGameEntity Spawn(string key, Vector3 position, Quaternion rotation, uint owner)
        {
            PoolGroup group;
            if (!_groups.TryGetValue(key, out group))
            {
                SimLog.Error(string.Format("Spawn('{0}') for a group that was never configured", key));
                return null;
            }

            // Lowest-ID inactive. Because instances were created in stable-ID order, the first
            // inactive in the array is the lowest-ID inactive, and every peer restoring the
            // same active flags scans to the same one.
            for (int i = 0; i < group.Instances.Length; ++i)
            {
                SimGameEntity instance = group.Instances[i];
                if (!instance.IsActive)
                {
                    _context.World.SetEntityEnabled(instance.StableId, true);
                    SimBody.Teleport(instance.Body, position, rotation, Vector3.zero, Vector3.zero);
                    instance.SpawnOwner = owner;
                    instance.Activate(_context.CurrentTick);
                    return instance;
                }
            }

            SimLog.Warning(string.Format(
                "Pool group '{0}' is exhausted; raise its count or despawn sooner. The spawn was dropped, " +
                "which every peer does identically, so it does not desync.", key));
            return null;
        }

        /// <summary>Returns an active instance to its pool by stable ID.</summary>
        public bool Despawn(uint stableId)
        {
            SimGameEntity instance;
            if (!_context.Registry.TryGet(stableId, out instance))
            {
                SimLog.Warning(string.Format("Despawn({0}) for an unregistered entity", stableId));
                return false;
            }
            if (!instance.IsActive)
            {
                return false;
            }

            instance.Deactivate(_context.CurrentTick);
            _context.World.SetEntityEnabled(stableId, false);
            return true;
        }
    }
}
