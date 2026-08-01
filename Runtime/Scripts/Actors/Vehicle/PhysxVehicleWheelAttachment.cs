using UnityEngine;

namespace PhysX5ForUnity
{
    /// <summary>
    /// One wheel of a <see cref="PhysxVehicle"/>, mirroring Omniverse's per-wheel
    /// PhysxVehicleWheelAttachmentAPI. References the reusable wheel / tire /
    /// suspension assets and authors the suspension frame from its own transform
    /// (relative to the vehicle chassis). Add one per wheel as a child of the
    /// vehicle GameObject.
    /// </summary>
    [AddComponentMenu("PhysX 5/Vehicle/PhysX Vehicle Wheel Attachment")]
    public class PhysxVehicleWheelAttachment : MonoBehaviour
    {
        [Header("Reusable part assets")]
        public PhysxVehicleWheel wheel;
        public PhysxVehicleTire tire;
        public PhysxVehicleSuspension suspension;

        [Header("Axle / role")]
        [Tooltip("Axle index this wheel belongs to (front = 0, next axle = 1, ...).")]
        public int axle = 0;

        [Tooltip("Ordering of this wheel within its axle (left-to-right). Used to order wheels deterministically.")]
        public int indexInAxle = 0;

        [Tooltip("Wheel receives engine/direct drive torque (part of the differential).")]
        public bool isDriven = true;

        [Tooltip("Wheel responds to the steer command.")]
        public bool isSteering = false;

        [Tooltip("Wheel responds to the second brake command set (handbrake).")]
        public bool isHandbrake = false;

        [Header("Suspension frame")]
        [Tooltip("Suspension travel direction in vehicle-local space (usually straight down).")]
        public Vector3 travelDirectionLocal = Vector3.down;

        [Tooltip("Transform driven by the simulated wheel pose. If null, this component's transform is used.")]
        public Transform wheelVisual;

        [Header("Suspension compliance")]
        public float toeAngle = 0.0f;
        public float camberAngle = 0.0f;
        public Vector3 suspensionForceAppPoint = Vector3.zero;
        public Vector3 tireForceAppPoint = Vector3.zero;

        /// <summary>Wheel id assigned by the owning vehicle at build time.</summary>
        public int WheelId { get; internal set; } = -1;

        public PxwVehicleWheelDesc GetWheelDesc()
        {
            return wheel != null ? wheel.ToDesc() : new PxwVehicleWheelDesc
            {
                radius = 0.35f,
                halfWidth = 0.15f,
                mass = 20.0f,
                moi = 0.5f * 20.0f * 0.35f * 0.35f,
                dampingRate = 0.25f
            };
        }

        public PxwVehicleTireDesc GetTireDesc()
        {
            if (tire != null) return tire.ToDesc();
            return ScriptableObject.CreateInstance<PhysxVehicleTire>().ToDesc();
        }

        /// <summary>
        /// Builds the suspension descriptor, sampling the reusable spring asset and
        /// deriving the attachment pose from this transform relative to the vehicle.
        /// </summary>
        public PxwVehicleSuspensionDesc GetSuspensionDesc(Transform vehicleTransform, float sprungMassFallback)
        {
            PxwVehicleSuspensionDesc desc = suspension != null
                ? suspension.ToDesc()
                : ScriptableObject.CreateInstance<PhysxVehicleSuspension>().ToDesc();

            Vector3 localPos = vehicleTransform.InverseTransformPoint(transform.position);
            Quaternion localRot = Quaternion.Inverse(vehicleTransform.rotation) * transform.rotation;
            desc.suspensionAttachment = new PxTransformData(localPos, localRot);

            Vector3 dir = travelDirectionLocal.sqrMagnitude > 1e-6f ? travelDirectionLocal.normalized : Vector3.down;
            desc.travelDir = dir;
            desc.wheelAttachment = new PxTransformData(Vector3.zero, Quaternion.identity);

            if (desc.sprungMass <= 0.0f) desc.sprungMass = sprungMassFallback;
            return desc;
        }

        public PxwVehicleSuspensionComplianceDesc GetComplianceDesc()
        {
            return new PxwVehicleSuspensionComplianceDesc
            {
                toeAngle = toeAngle,
                camberAngle = camberAngle,
                suspForceAppPoint = suspensionForceAppPoint,
                tireForceAppPoint = tireForceAppPoint
            };
        }

        /// <summary>
        /// Positions the wheel visual from a simulated wheel pose expressed in the
        /// vehicle rigid-body (actor) frame.
        /// </summary>
        public void ApplyWheelLocalPose(Transform vehicleTransform, PxTransformData localPose)
        {
            Transform target = wheelVisual != null ? wheelVisual : transform;
            target.position = vehicleTransform.TransformPoint(localPose.position);
            target.rotation = vehicleTransform.rotation * localPose.quaternion;
        }

        public float WheelRadius => wheel != null ? wheel.radius : 0.35f;

        public float SuspensionTravel => suspension != null ? suspension.travelDistance : 0.2f;
    }
}
