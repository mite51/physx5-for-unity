using System;
using UnityEngine;
using UNDPWR.Core;
using UNDPWR.Interop;

namespace UNDPWR.Gameplay
{
    /// <summary>
    /// Deterministic scene queries against a world: raycasts, overlaps and sweeps that
    /// resolve every hit to a stable ID and return them in a reproducible order.
    /// </summary>
    /// <remarks>
    /// The reproducible order is the whole point. Unity's own <c>Physics.Raycast</c> and
    /// <c>OverlapSphere</c> return hits in an order PhysX does not guarantee, so two peers
    /// iterating the same hits can pick a different "first" one and take different gameplay
    /// decisions from identical physics. These queries push the sort into the native layer:
    /// raycasts and sweeps come back sorted by distance with stable ID breaking ties,
    /// overlaps sorted by stable ID.
    /// <para>
    /// Call these only inside a step handler, where every peer runs the query at the same
    /// point in the tick against the same committed scene. A query run from an ordinary
    /// <c>Update</c> sees a scene that has been stepped a different number of times on each
    /// peer.
    /// </para>
    /// <para>
    /// Each method fills a caller-owned array and returns how many hits were written, so a
    /// query in the tick loop does not allocate. If the array is smaller than the number of
    /// hits, the nearest (for rays and sweeps) or lowest-ID (for overlaps) are kept, since
    /// those are the deterministically-ordered front of the list.
    /// </para>
    /// </remarks>
    public sealed class SimQuery
    {
        private readonly DeterministicWorld _world;

        /// <summary>Creates a query interface over a world.</summary>
        public SimQuery(DeterministicWorld world)
        {
            if (world == null)
            {
                throw new ArgumentNullException("world");
            }
            _world = world;
        }

        /// <summary>Casts a ray and writes the hits, nearest first.</summary>
        /// <param name="origin">World-space start of the ray.</param>
        /// <param name="direction">Ray direction; need not be normalised.</param>
        /// <param name="maxDistance">How far to cast.</param>
        /// <param name="filterMask">
        /// A game-defined mask compared against each shape's query group. Zero matches
        /// everything.
        /// </param>
        /// <param name="hits">Receives the hits, up to its length.</param>
        /// <returns>How many hits were written.</returns>
        public unsafe int Raycast(Vector3 origin, Vector3 direction, float maxDistance, uint filterMask, SimRaycastHit[] hits)
        {
            if (hits == null || hits.Length == 0)
            {
                throw new ArgumentException("hits array must be non-empty", "hits");
            }
            fixed (SimRaycastHit* dst = hits)
            {
                return (int)NativeMethods.PxwWorldRaycast(
                    _world.Handle, ref origin, ref direction, maxDistance, filterMask, dst, (uint)hits.Length);
            }
        }

        /// <summary>Finds every body overlapping a sphere, lowest stable ID first.</summary>
        public unsafe int OverlapSphere(Vector3 center, float radius, uint filterMask, SimOverlapHit[] hits)
        {
            RequireHits(hits);
            Vector3 halfExtents = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            fixed (SimOverlapHit* dst = hits)
            {
                return (int)NativeMethods.PxwWorldOverlap(
                    _world.Handle, (uint)SimQueryShape.Sphere, ref center, ref halfExtents, radius,
                    ref rotation, filterMask, dst, (uint)hits.Length);
            }
        }

        /// <summary>Finds every body overlapping an oriented box, lowest stable ID first.</summary>
        public unsafe int OverlapBox(Vector3 center, Vector3 halfExtents, Quaternion rotation, uint filterMask, SimOverlapHit[] hits)
        {
            RequireHits(hits);
            fixed (SimOverlapHit* dst = hits)
            {
                return (int)NativeMethods.PxwWorldOverlap(
                    _world.Handle, (uint)SimQueryShape.Box, ref center, ref halfExtents, 0.0f,
                    ref rotation, filterMask, dst, (uint)hits.Length);
            }
        }

        /// <summary>
        /// Finds every body overlapping a capsule, lowest stable ID first.
        /// </summary>
        /// <param name="halfHeight">Half the capsule's length along its local up axis.</param>
        public unsafe int OverlapCapsule(Vector3 center, float radius, float halfHeight, Quaternion rotation, uint filterMask, SimOverlapHit[] hits)
        {
            RequireHits(hits);
            Vector3 halfExtents = new Vector3(0.0f, halfHeight, 0.0f);
            fixed (SimOverlapHit* dst = hits)
            {
                return (int)NativeMethods.PxwWorldOverlap(
                    _world.Handle, (uint)SimQueryShape.Capsule, ref center, ref halfExtents, radius,
                    ref rotation, filterMask, dst, (uint)hits.Length);
            }
        }

        /// <summary>Sweeps a sphere along a direction, writing the hits nearest first.</summary>
        public unsafe int SweepSphere(Vector3 origin, float radius, Vector3 direction, float maxDistance, uint filterMask, SimRaycastHit[] hits)
        {
            if (hits == null || hits.Length == 0)
            {
                throw new ArgumentException("hits array must be non-empty", "hits");
            }
            Vector3 halfExtents = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            fixed (SimRaycastHit* dst = hits)
            {
                return (int)NativeMethods.PxwWorldSweep(
                    _world.Handle, (uint)SimQueryShape.Sphere, ref origin, ref halfExtents, radius,
                    ref rotation, ref direction, maxDistance, filterMask, dst, (uint)hits.Length);
            }
        }

        private static void RequireHits(SimOverlapHit[] hits)
        {
            if (hits == null || hits.Length == 0)
            {
                throw new ArgumentException("hits array must be non-empty", "hits");
            }
        }
    }
}
