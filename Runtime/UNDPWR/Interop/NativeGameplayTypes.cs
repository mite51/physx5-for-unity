using System.Runtime.InteropServices;
using UnityEngine;

namespace UNDPWR.Interop
{
    /// <summary>
    /// How a force or torque is applied to a body. Mirrors <c>physx::PxForceMode</c>.
    /// </summary>
    /// <remarks>
    /// The numeric values are PhysX's own, so the managed enum can be cast straight to the
    /// native argument without a translation table that could drift out of sync.
    /// </remarks>
    public enum SimForceMode : uint
    {
        /// <summary>A continuous force in mass units, applied over the timestep.</summary>
        Force = 0,

        /// <summary>An instantaneous impulse in mass-distance-per-time units.</summary>
        Impulse = 1,

        /// <summary>An instantaneous change in velocity, ignoring mass.</summary>
        VelocityChange = 2,

        /// <summary>A continuous acceleration, ignoring mass.</summary>
        Acceleration = 3
    }

    /// <summary>
    /// One hit from a raycast or sweep, resolved to the stable ID of the body it struck.
    /// Mirrors <c>pxw::PxwRaycastHit</c>.
    /// </summary>
    /// <remarks>
    /// Hits are returned sorted by <see cref="Distance"/> ascending, with
    /// <see cref="StableId"/> breaking ties. PhysX does not guarantee an order for touching
    /// hits, and an unsorted list is a determinism hazard: two peers that iterate the same
    /// hits in different orders take different gameplay decisions and desync. Sorting in the
    /// native layer, where the distances are already known, is cheaper and less error-prone
    /// than asking every caller to remember to sort.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct SimRaycastHit
    {
        /// <summary>The stable ID of the body that was hit.</summary>
        public uint StableId;

        /// <summary>The hit body's <see cref="SimHandleKind"/>.</summary>
        public uint Kind;

        /// <summary>The world-space contact point.</summary>
        public Vector3 Point;

        /// <summary>The world-space surface normal at the hit.</summary>
        public Vector3 Normal;

        /// <summary>Distance along the ray or sweep to the hit.</summary>
        public float Distance;

        /// <summary>The struck triangle's index for a mesh, or <c>0xFFFFFFFF</c> otherwise.</summary>
        public uint FaceIndex;
    }

    /// <summary>
    /// One body found by an overlap query, resolved to its stable ID.
    /// Mirrors <c>pxw::PxwOverlapHit</c>.
    /// </summary>
    /// <remarks>
    /// Returned sorted by <see cref="StableId"/> ascending, for the same reason raycast
    /// hits are sorted: an overlap has no natural order, so one is imposed that every peer
    /// computes identically.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct SimOverlapHit
    {
        /// <summary>The stable ID of the overlapping body.</summary>
        public uint StableId;

        /// <summary>The overlapping body's <see cref="SimHandleKind"/>.</summary>
        public uint Kind;
    }

    /// <summary>
    /// One contact between two bodies, reported after a step. Mirrors
    /// <c>pxw::PxwContactEvent</c>.
    /// </summary>
    /// <remarks>
    /// The pair is always ordered so that <see cref="IdA"/> is the smaller stable ID and
    /// <see cref="IdB"/> the larger, and the whole buffer is sorted by (IdA, IdB). Contact
    /// reports arrive from PhysX in an order that depends on internal bookkeeping a
    /// snapshot cannot carry, so the order is normalised in the native layer before the
    /// buffer crosses the boundary. Gameplay must not rely on which of the two bodies is
    /// "first" meaning anything beyond ID order.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct SimContactEvent
    {
        /// <summary>The smaller of the two stable IDs in contact.</summary>
        public uint IdA;

        /// <summary>The larger of the two stable IDs in contact.</summary>
        public uint IdB;

        /// <summary>A representative world-space contact point.</summary>
        public Vector3 Point;

        /// <summary>The world-space contact normal, pointing from A toward B.</summary>
        public Vector3 Normal;

        /// <summary>The total normal impulse applied to resolve the contact.</summary>
        public float Impulse;
    }

    /// <summary>
    /// Whether a trigger overlap began or ended this step. Mirrors
    /// <c>pxw::PxwTriggerStatus</c>.
    /// </summary>
    public enum SimTriggerStatus : uint
    {
        /// <summary>The other body stopped overlapping the trigger this step.</summary>
        Lost = 0,

        /// <summary>The other body began overlapping the trigger this step.</summary>
        Found = 1
    }

    /// <summary>
    /// One trigger-volume overlap change, reported after a step. Mirrors
    /// <c>pxw::PxwTriggerEvent</c>.
    /// </summary>
    /// <remarks>
    /// Sorted by (<see cref="TriggerId"/>, <see cref="OtherId"/>) for the same determinism
    /// reason as contacts. Note that the gameplay layer largely prefers explicit overlap
    /// queries in a step handler over trigger events, because a query is evaluated at a
    /// known point in the tick; triggers are provided for cases where polling every volume
    /// would be wasteful.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct SimTriggerEvent
    {
        /// <summary>The stable ID of the trigger shape's body.</summary>
        public uint TriggerId;

        /// <summary>The stable ID of the body that entered or left the trigger.</summary>
        public uint OtherId;

        /// <summary>Whether the overlap began or ended, a <see cref="SimTriggerStatus"/>.</summary>
        public uint Status;
    }

    /// <summary>
    /// The shape of a query volume for overlaps and sweeps. Mirrors
    /// <c>pxw::PxwQueryShape</c>.
    /// </summary>
    public enum SimQueryShape : uint
    {
        /// <summary>A sphere, using the radius field only.</summary>
        Sphere = 0,

        /// <summary>An axis-configurable box, using the half-extents and rotation.</summary>
        Box = 1,

        /// <summary>A capsule, using the radius and half-height along its local axis.</summary>
        Capsule = 2
    }
}
