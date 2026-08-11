using System;
using System.Collections.Generic;
using UnityEngine;
using UNDPWR.Core;
using UNDPWR.Interop;

namespace UNDPWR.Gameplay
{
    /// <summary>
    /// Drives entities' visible transforms from the simulation, interpolating between the two
    /// most recent simulated poses so rendering is smooth even though the simulation moves in
    /// fixed, discrete ticks.
    /// </summary>
    /// <remarks>
    /// This is the one-way valve between simulation and presentation. The simulation advances
    /// in fixed steps and, under this framework, replays its prediction window every frame; the
    /// display refreshes at whatever rate the monitor runs. Snapping visible transforms
    /// straight onto the newest simulated pose makes motion judder at any refresh rate that is
    /// not an exact multiple of the tick rate. Interpolating between the previous and current
    /// simulated poses by how far the render clock sits between ticks removes the judder.
    /// <para>
    /// The direction of the arrow is the whole point: poses flow out of the simulation into
    /// transforms and never back. A binder that read <c>transform.position</c> back into the
    /// sim would let a render-rate quantity into a deterministic computation and desync. The
    /// binder only ever writes transforms, from <see cref="DeterministicWorld.ReadPoses"/>,
    /// which is a read of committed state.
    /// </para>
    /// <para>
    /// Call <see cref="Sample"/> once each time the simulation has advanced, then
    /// <see cref="Render"/> each frame with an alpha in [0, 1] for how far the render clock is
    /// through the current tick. Inactive (pooled-out) entities are left alone, since their
    /// presentation root is already hidden.
    /// </para>
    /// </remarks>
    public sealed class SimPresentationBinder
    {
        private struct Target
        {
            public SimGameEntity Entity;
            public Vector3 PreviousPosition;
            public Vector3 CurrentPosition;
            public Quaternion PreviousRotation;
            public Quaternion CurrentRotation;
        }

        private readonly DeterministicWorld _world;
        private readonly SimObjectRegistry _registry;
        private readonly Dictionary<uint, int> _index = new Dictionary<uint, int>();
        private Target[] _targets = new Target[0];
        private bool _hasBaseline;

        /// <summary>Creates a binder over a world and its registry.</summary>
        public SimPresentationBinder(DeterministicWorld world, SimObjectRegistry registry)
        {
            if (world == null)
            {
                throw new ArgumentNullException("world");
            }
            if (registry == null)
            {
                throw new ArgumentNullException("registry");
            }
            _world = world;
            _registry = registry;
        }

        /// <summary>
        /// Rebuilds the set of driven transforms from the registry. Call once after the pool
        /// has been preregistered, since the registry is fixed from then on.
        /// </summary>
        public void Rebuild()
        {
            IReadOnlyList<SimGameEntity> ordered = _registry.Ordered;
            _targets = new Target[ordered.Count];
            _index.Clear();
            for (int i = 0; i < ordered.Count; ++i)
            {
                _targets[i].Entity = ordered[i];
                _index[ordered[i].StableId] = i;
            }
            _hasBaseline = false;
        }

        /// <summary>
        /// Takes a fresh pose sample from the simulation, making the last sample the one to
        /// interpolate from.
        /// </summary>
        public void Sample()
        {
            for (int i = 0; i < _targets.Length; ++i)
            {
                _targets[i].PreviousPosition = _targets[i].CurrentPosition;
                _targets[i].PreviousRotation = _targets[i].CurrentRotation;
            }

            int count;
            SimPoseEntry[] poses = _world.ReadPoses(out count);
            for (int i = 0; i < count; ++i)
            {
                int index;
                if (_index.TryGetValue(poses[i].StableId, out index))
                {
                    _targets[index].CurrentPosition = poses[i].Pose.Position;
                    _targets[index].CurrentRotation = poses[i].Pose.Rotation;
                }
            }

            if (!_hasBaseline)
            {
                // First sample has no "previous"; interpolate from itself so nothing snaps.
                for (int i = 0; i < _targets.Length; ++i)
                {
                    _targets[i].PreviousPosition = _targets[i].CurrentPosition;
                    _targets[i].PreviousRotation = _targets[i].CurrentRotation;
                }
                _hasBaseline = true;
            }
        }

        /// <summary>
        /// Places every active entity's transform between its previous and current simulated
        /// pose.
        /// </summary>
        /// <param name="alpha">How far the render clock is through the current tick, in [0, 1].</param>
        public void Render(float alpha)
        {
            float a = Mathf.Clamp01(alpha);
            for (int i = 0; i < _targets.Length; ++i)
            {
                SimGameEntity entity = _targets[i].Entity;
                if (entity == null || !entity.IsActive)
                {
                    continue;
                }
                Vector3 position = Vector3.LerpUnclamped(_targets[i].PreviousPosition, _targets[i].CurrentPosition, a);
                Quaternion rotation = Quaternion.SlerpUnclamped(_targets[i].PreviousRotation, _targets[i].CurrentRotation, a);
                entity.transform.SetPositionAndRotation(position, rotation);
            }
        }
    }
}
