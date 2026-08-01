using UnityEngine;

namespace PhysX5ForUnity
{
    /// <summary>
    /// Reusable suspension parameters, mirroring Omniverse's
    /// PhysxVehicleSuspensionAPI. The suspension frame (attachment pose and travel
    /// direction) is authored per-wheel on <see cref="PhysxVehicleWheelAttachment"/>;
    /// this asset carries only the reusable spring characteristics.
    /// </summary>
    [CreateAssetMenu(fileName = "PhysXVehicleSuspension", menuName = "PhysX 5/Vehicle/Suspension", order = 21)]
    public class PhysxVehicleSuspension : ScriptableObject
    {
        [Tooltip("Maximum suspension travel distance in metres.")]
        public float travelDistance = 0.2f;

        [Tooltip("Spring stiffness (N/m). If <= 0 it is derived from the sprung mass and travel at Finalize time.")]
        public float stiffness = 0.0f;

        [Tooltip("Spring damping (Ns/m). If <= 0 it is derived from the sprung mass at Finalize time.")]
        public float damping = 0.0f;

        [Tooltip("Sprung mass carried by this suspension in kilograms. If <= 0 it is distributed automatically.")]
        public float sprungMass = 0.0f;

        /// <summary>
        /// Fills the reusable portion of the descriptor. The per-wheel frame fields
        /// (suspensionAttachment / travelDir / wheelAttachment) are supplied by the
        /// wheel attachment component.
        /// </summary>
        public PxwVehicleSuspensionDesc ToDesc()
        {
            return new PxwVehicleSuspensionDesc
            {
                suspensionAttachment = new PxTransformData(Vector3.zero, Quaternion.identity),
                travelDir = Vector3.down,
                travelDist = travelDistance,
                wheelAttachment = new PxTransformData(Vector3.zero, Quaternion.identity),
                stiffness = stiffness,
                damping = damping,
                sprungMass = sprungMass
            };
        }
    }
}
