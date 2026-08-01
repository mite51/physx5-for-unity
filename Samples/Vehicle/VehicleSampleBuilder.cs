using UnityEngine;

namespace PhysX5ForUnity.Samples
{
    /// <summary>
    /// Programmatically builds a drivable four-wheel PhysX vehicle plus a ground
    /// plane at play time. Used by the Vehicle sample scenes so the demo does not
    /// depend on fragile hand-authored component wiring. Reusable part data is
    /// created as in-memory ScriptableObjects, mirroring how a project would
    /// normally reference PhysxVehicleWheel / Tire / Suspension / Engine assets.
    /// </summary>
    [AddComponentMenu("PhysX 5/Vehicle/Samples/Vehicle Sample Builder")]
    public class VehicleSampleBuilder : MonoBehaviour
    {
        [Header("Drive model")]
        public PhysxVehicle.VehicleDriveType driveType = PhysxVehicle.VehicleDriveType.Engine;
        public PxVehicleDifferentialType differentialType = PxVehicleDifferentialType.eMULTIWHEEL;

        [Tooltip("Drive each wheel directly (adds a PhysxVehicleWheelController per wheel) instead of using the controller/differential path.")]
        public bool useDirectWheelControl = false;

        [Header("Layout")]
        public Vector3 chassisHalfExtents = new Vector3(0.9f, 0.4f, 2.2f);
        public float wheelRadius = 0.35f;
        public float wheelHalfWidth = 0.15f;
        public float trackHalfWidth = 0.85f;
        public float wheelBaseHalf = 1.4f;
        public float wheelVerticalOffset = -0.35f;
        public float chassisMass = 1500.0f;
        public Vector3 spawnPosition = new Vector3(0.0f, 1.0f, 0.0f);

        [Header("Ground")]
        public bool buildGround = true;
        public Vector3 groundSize = new Vector3(200.0f, 1.0f, 200.0f);
        public Vector3 groundPosition = new Vector3(0.0f, -0.5f, 0.0f);

        private PhysxScene m_scene;
        private PhysxMaterial m_material;

        private void Start()
        {
            m_scene = Resources.Load<PhysxScene>("PhysX Scene Assets/PhysXDefaultScene");
            m_material = Resources.Load<PhysxMaterial>("PhysX Physic Materials/PhysXDefaultRigidMaterial");
            if (m_scene == null)
            {
                Debug.LogError("VehicleSampleBuilder: could not load PhysXDefaultScene from Resources.");
                return;
            }

            if (buildGround) BuildGround();
            BuildVehicle();
        }

        private void BuildGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Vehicle Ground";
            ground.SetActive(false);
            Destroy(ground.GetComponent<Collider>());
            ground.transform.position = groundPosition;
            ground.transform.localScale = groundSize;

            PhysxBoxGeometry geo = ground.AddComponent<PhysxBoxGeometry>();
            geo.Size = groundSize;

            PhysxShape shape = ground.AddComponent<PhysxShape>();
            shape.Material = m_material;

            PhysxStaticRigidActor actor = ground.AddComponent<PhysxStaticRigidActor>();
            actor.Scene = m_scene;

            ground.SetActive(true);
        }

        private void BuildVehicle()
        {
            // Reusable part data (would normally be shared ScriptableObject assets).
            PhysxVehicleWheel wheelAsset = ScriptableObject.CreateInstance<PhysxVehicleWheel>();
            wheelAsset.radius = wheelRadius;
            wheelAsset.halfWidth = wheelHalfWidth;
            wheelAsset.mass = 20.0f;

            PhysxVehicleTire tireAsset = ScriptableObject.CreateInstance<PhysxVehicleTire>();
            PhysxVehicleSuspension suspAsset = ScriptableObject.CreateInstance<PhysxVehicleSuspension>();
            suspAsset.travelDistance = 0.25f;

            GameObject vehicleGO = new GameObject(driveType + " Vehicle");
            vehicleGO.SetActive(false);
            vehicleGO.transform.position = spawnPosition;

            // Chassis visual.
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(vehicleGO.transform, false);
            body.transform.localScale = 2.0f * chassisHalfExtents;

            PhysxBoxGeometry chassisGeo = vehicleGO.AddComponent<PhysxBoxGeometry>();
            chassisGeo.Size = 2.0f * chassisHalfExtents;

            PhysxVehicle vehicle = vehicleGO.AddComponent<PhysxVehicle>();
            vehicle.Scene = m_scene;
            vehicle.chassisMaterial = m_material;
            vehicle.driveType = driveType;
            vehicle.differentialType = differentialType;
            vehicle.useDirectWheelControl = useDirectWheelControl;
            vehicle.mass = chassisMass;
            vehicle.boxHalfExtents = chassisHalfExtents;
            vehicle.centerOfMass = new Vector3(0.0f, -0.25f, 0.0f);

            if (driveType == PhysxVehicle.VehicleDriveType.Engine)
            {
                vehicle.engine = ScriptableObject.CreateInstance<PhysxVehicleEngine>();
                vehicle.gearbox = ScriptableObject.CreateInstance<PhysxVehicleGearbox>();
                vehicle.autobox = ScriptableObject.CreateInstance<PhysxVehicleAutobox>();
                vehicle.clutch = ScriptableObject.CreateInstance<PhysxVehicleClutch>();
            }

            // Four wheels: front axle steers, rear axle handbrakes; all driven.
            CreateWheel(vehicleGO.transform, "Wheel FL", new Vector3(-trackHalfWidth, wheelVerticalOffset, wheelBaseHalf), 0, 0, true, false, wheelAsset, tireAsset, suspAsset, vehicle);
            CreateWheel(vehicleGO.transform, "Wheel FR", new Vector3(trackHalfWidth, wheelVerticalOffset, wheelBaseHalf), 0, 1, true, false, wheelAsset, tireAsset, suspAsset, vehicle);
            CreateWheel(vehicleGO.transform, "Wheel RL", new Vector3(-trackHalfWidth, wheelVerticalOffset, -wheelBaseHalf), 1, 0, false, true, wheelAsset, tireAsset, suspAsset, vehicle);
            CreateWheel(vehicleGO.transform, "Wheel RR", new Vector3(trackHalfWidth, wheelVerticalOffset, -wheelBaseHalf), 1, 1, false, true, wheelAsset, tireAsset, suspAsset, vehicle);

            if (!useDirectWheelControl)
            {
                PhysxVehicleController controller = vehicleGO.AddComponent<PhysxVehicleController>();
                controller.readKeyboardInput = true;
            }

            vehicleGO.SetActive(true);
        }

        private void CreateWheel(Transform parent, string wheelName, Vector3 localPos, int axle, int indexInAxle,
            bool steering, bool handbrake, PhysxVehicleWheel wheelAsset, PhysxVehicleTire tireAsset,
            PhysxVehicleSuspension suspAsset, PhysxVehicle vehicle)
        {
            GameObject wheelGO = new GameObject(wheelName);
            wheelGO.transform.SetParent(parent, false);
            wheelGO.transform.localPosition = localPos;

            // Wheel visual (a flat cylinder oriented along the lateral axis).
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            visual.name = "Visual";
            Destroy(visual.GetComponent<Collider>());
            visual.transform.SetParent(wheelGO.transform, false);
            visual.transform.localRotation = Quaternion.Euler(0.0f, 0.0f, 90.0f);
            visual.transform.localScale = new Vector3(wheelAsset.radius * 2.0f, wheelAsset.halfWidth, wheelAsset.radius * 2.0f);

            PhysxVehicleWheelAttachment attach = wheelGO.AddComponent<PhysxVehicleWheelAttachment>();
            attach.wheel = wheelAsset;
            attach.tire = tireAsset;
            attach.suspension = suspAsset;
            attach.axle = axle;
            attach.indexInAxle = indexInAxle;
            attach.isDriven = true;
            attach.isSteering = steering;
            attach.isHandbrake = handbrake;
            attach.wheelVisual = visual.transform;

            if (vehicle.useDirectWheelControl)
                wheelGO.AddComponent<PhysxVehicleWheelController>();
        }
    }
}
