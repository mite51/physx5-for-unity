using UnityEngine;

namespace PhysX5ForUnity
{
    /// <summary>
    /// Reusable wheel rigid-body parameters, mirroring Omniverse's
    /// PhysxVehicleWheelAPI. Referenced by <see cref="PhysxVehicleWheelAttachment"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "PhysXVehicleWheel", menuName = "PhysX 5/Vehicle/Wheel", order = 20)]
    public class PhysxVehicleWheel : ScriptableObject
    {
        [Tooltip("Wheel radius in metres.")]
        public float radius = 0.35f;

        [Tooltip("Half of the wheel width in metres.")]
        public float halfWidth = 0.15f;

        [Tooltip("Wheel mass in kilograms.")]
        public float mass = 20.0f;

        [Tooltip("Wheel moment of inertia about the rolling axis. If <= 0 it is computed from mass and radius.")]
        public float moi = 0.0f;

        [Tooltip("Rotational damping applied to the wheel.")]
        public float dampingRate = 0.25f;

        public PxwVehicleWheelDesc ToDesc()
        {
            float computedMoi = moi > 0.0f ? moi : 0.5f * mass * radius * radius;
            return new PxwVehicleWheelDesc
            {
                radius = radius,
                halfWidth = halfWidth,
                mass = mass,
                moi = computedMoi,
                dampingRate = dampingRate
            };
        }
    }
}
