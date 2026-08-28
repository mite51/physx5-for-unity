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

        [Header("Collision shape")]
        [Tooltip("Where the wheel's collision shape gets its geometry. Cooked Prism is what the " +
                 "vehicle SDK builds and what this package has always used. Cylinder is exact and " +
                 "does not chatter as the wheel spins, which matters if the wheel simulates.")]
        public PxwVehicleWheelGeometryMode geometryMode = PxwVehicleWheelGeometryMode.CookedPrism;

        [Tooltip("Geometry to use when Geometry Mode is Geometry. Ignored otherwise.")]
        public PhysxGeometry collisionGeometry;

        [Tooltip("Convex core margin used by the Cylinder mode. Size-preserving, and so the " +
                 "OPPOSITE of a standalone PhysxCylinderGeometry margin (which grows the shape): " +
                 "here the core SHRINKS by this much so the wheel keeps the size the wheel asset " +
                 "asks for. A small non-zero value rounds the rim and can steady contact.")]
        public float cylinderMargin = 0.0f;

        [Tooltip("Let the wheel collide. OFF by default and deliberately so: the suspension raycast " +
                 "and tire model already resolve the wheel against the ground, so a simulating wheel " +
                 "must be given a collision group that does not collide with the drivable surface, " +
                 "or the road gets resolved twice and the handling model fights the contact solver.")]
        public bool isSimulationShape = false;

        [Tooltip("Let scene queries hit the wheel. Safe to enable on its own: it changes nothing " +
                 "the solver does, it just stops wheels being invisible to raycasts and overlaps.")]
        public bool isSceneQueryShape = false;

        [Tooltip("Collision group (0-31) for the wheel's simulation filter data. Only meaningful " +
                 "when the wheel is a simulation shape. Use PhysxCollisionGroups to stop this group " +
                 "colliding with the group your drivable surface uses.")]
        public int collisionGroup = PhysxCollisionGroups.DefaultGroup;

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

        /// <summary>
        /// Builds the collision shape descriptor for this wheel. The defaults reproduce the
        /// historical behaviour: a cooked prism that neither simulates nor answers queries.
        /// </summary>
        public PxwVehicleWheelShapeDesc GetWheelShapeDesc()
        {
            int group = collisionGroup;
            if (!PhysxCollisionGroups.IsValidGroup(group))
            {
                Debug.LogError(
                    $"PhysxVehicleWheelAttachment '{name}': collision group {group} is out of range; " +
                    $"valid groups are 0..{PhysxCollisionGroups.MaxGroups - 1}. Using " +
                    $"{PhysxCollisionGroups.DefaultGroup}.", this);
                group = PhysxCollisionGroups.DefaultGroup;
            }

            return new PxwVehicleWheelShapeDesc
            {
                geometryMode = (int)geometryMode,
                simulationShape = isSimulationShape ? 1 : 0,
                sceneQueryShape = isSceneQueryShape ? 1 : 0,
                margin = Mathf.Max(0.0f, cylinderMargin),
                // The filter shader reads word0 as a collision group index; the rest feed its
                // group-mask filtering, which is left in its pass-everything default.
                simFilterData = new uint[] { (uint)group, 0, 0, 0 },
                queryFilterData = new uint[] { (uint)group, 0, 0, 0 }
            };
        }

        /// <summary>
        /// Native geometry for <see cref="PxwVehicleWheelGeometryMode.Geometry"/>, or
        /// <see cref="System.IntPtr.Zero"/> when this wheel does not use that mode.
        /// </summary>
        public System.IntPtr GetCollisionGeometryPtr()
        {
            if (geometryMode != PxwVehicleWheelGeometryMode.Geometry || collisionGeometry == null)
            {
                return System.IntPtr.Zero;
            }
            collisionGeometry.CreateOrGetSharedGeometry();
            return collisionGeometry.NativeObjectPtr;
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
