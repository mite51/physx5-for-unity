using UnityEngine;

namespace UNDPWR.Gameplay
{
    /// <summary>
    /// Supplies the horizontal reference frame that a player's raw movement input is resolved
    /// against, so "forward" means "away from the camera" rather than "along world +Z".
    /// </summary>
    /// <remarks>
    /// Camera-relative movement is what almost every game wants and what the previous system
    /// hard-wired to its orbit camera. Generalising it to an interface is what lets any camera
    /// — orbit, first-person, fixed isometric, or none — drive the same encoder.
    /// <para>
    /// The determinism-critical point is what this does <i>not</i> touch. The reference frame
    /// is read only on the local peer, only to turn that peer's own raw input into a
    /// world-space direction. The camera orientation never enters the networked payload; only
    /// the resolved, quantized direction does. So two peers with wildly different camera
    /// angles still simulate identically, because by the time input crosses the wire the
    /// camera is already out of the picture.
    /// </para>
    /// <para>
    /// Both vectors are expected to be horizontal (y = 0) and unit length. Returning false
    /// tells the encoder to fall back to the world frame for this tick, for the frame or two
    /// before a camera has a valid orientation.
    /// </para>
    /// </remarks>
    public interface ISimInputFrameProvider
    {
        /// <summary>Gets this peer's horizontal forward and right, or false to use world axes.</summary>
        bool TryGetReferenceFrame(out Vector3 forward, out Vector3 right);
    }

    /// <summary>The identity frame: forward is world +Z, right is world +X.</summary>
    /// <remarks>For top-down or twin-stick games where input is already world-relative.</remarks>
    public sealed class SimWorldSpaceInputFrame : ISimInputFrameProvider
    {
        /// <inheritdoc/>
        public bool TryGetReferenceFrame(out Vector3 forward, out Vector3 right)
        {
            forward = Vector3.forward;
            right = Vector3.right;
            return true;
        }
    }

    /// <summary>A constant frame at a fixed yaw, for a fixed isometric or angled camera.</summary>
    public sealed class SimFixedInputFrame : ISimInputFrameProvider
    {
        private readonly Vector3 _forward;
        private readonly Vector3 _right;

        /// <summary>Creates a frame rotated by a yaw about the world up axis.</summary>
        public SimFixedInputFrame(float yawDegrees)
        {
            Quaternion rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            _forward = rotation * Vector3.forward;
            _right = rotation * Vector3.right;
        }

        /// <inheritdoc/>
        public bool TryGetReferenceFrame(out Vector3 forward, out Vector3 right)
        {
            forward = _forward;
            right = _right;
            return true;
        }
    }

    /// <summary>
    /// A frame derived from a transform's facing, flattened onto the ground plane. The shared
    /// base for the orbit and first-person providers.
    /// </summary>
    public abstract class SimTransformInputFrame : ISimInputFrameProvider
    {
        private readonly Transform _source;

        /// <summary>Creates a frame that follows a transform.</summary>
        protected SimTransformInputFrame(Transform source)
        {
            _source = source;
        }

        /// <inheritdoc/>
        public bool TryGetReferenceFrame(out Vector3 forward, out Vector3 right)
        {
            if (_source == null)
            {
                forward = Vector3.forward;
                right = Vector3.right;
                return false;
            }

            Vector3 flat = _source.forward;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-6f)
            {
                // Looking straight up or down: the facing has no horizontal component, so fall
                // back to the transform's up flattened, which points where the top of the view
                // maps onto the ground.
                flat = _source.up;
                flat.y = 0f;
                if (flat.sqrMagnitude < 1e-6f)
                {
                    forward = Vector3.forward;
                    right = Vector3.right;
                    return false;
                }
            }

            flat.Normalize();
            forward = flat;
            // Forward rotated -90 degrees about world up: (x,0,z) -> (z,0,-x).
            right = new Vector3(flat.z, 0f, -flat.x);
            return true;
        }
    }

    /// <summary>
    /// A frame from an orbit camera that circles the avatar: movement is relative to the way
    /// the camera currently faces the world.
    /// </summary>
    public sealed class SimOrbitInputFrame : SimTransformInputFrame
    {
        /// <summary>Creates an orbit frame following a camera transform.</summary>
        public SimOrbitInputFrame(Transform camera) : base(camera) { }
    }

    /// <summary>
    /// A frame from a first-person view: movement is relative to where the player is looking,
    /// flattened so looking up or down does not slow them.
    /// </summary>
    public sealed class SimFirstPersonInputFrame : SimTransformInputFrame
    {
        /// <summary>Creates a first-person frame following a head or eye transform.</summary>
        public SimFirstPersonInputFrame(Transform head) : base(head) { }
    }
}
