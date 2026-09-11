using UnityEngine;

namespace PhysX5ForUnity.Samples
{
    /// <summary>
    /// Programmatically builds the one canonical PhysX Vehicle2 example: a four-wheel
    /// <b>Standard Engine Drive</b> car with a rear-wheel-drive differential, front-axle
    /// steering (Ackermann off), explicit reusable part assets, filtered cylinder wheels, and a
    /// ground plane. This is deliberately a single, specific setup rather than a configurable
    /// Direct/Engine matrix: Direct and Tank drive remain supported low-level APIs on
    /// <see cref="PhysxVehicle"/> and in the native tests, but the sample shows the canonical path.
    ///
    /// Reusable part data is created as in-memory ScriptableObjects with every spring/tire value
    /// authored explicitly, mirroring how a project references shared PhysxVehicleWheel / Tire /
    /// Suspension / Engine / Gearbox / Autobox / Clutch assets. Nothing is synthesised.
    /// </summary>
    [AddComponentMenu("PhysX 5/Vehicle/Samples/Vehicle Sample Builder")]
    public class VehicleSampleBuilder : MonoBehaviour
    {
        [Header("Layout")]
        public Vector3 chassisHalfExtents = new Vector3(0.9f, 0.4f, 2.2f);
        public float wheelRadius = 0.35f;
        public float wheelHalfWidth = 0.15f;
        public float trackHalfWidth = 0.85f;
        public float wheelBaseHalf = 1.4f;
        public float wheelVerticalOffset = -0.35f;
        public float chassisMass = 1500.0f;
        public Vector3 spawnPosition = new Vector3(0.0f, 1.0f, 0.0f);

        [Header("Suspension (explicit)")]
        public float suspensionTravel = 0.25f;
        [Tooltip("Target resting suspension compression (m); the spring stiffness is authored to hit it under gravity.")]
        public float restJounce = 0.1f;
        [Tooltip("Spring damping ratio used to author the explicit damping value from stiffness and sprung mass.")]
        public float dampingRatio = 0.35f;

        [Header("Brakes / steering")]
        public float maxBrakeTorque = 3000.0f;
        public float maxHandbrakeTorque = 3000.0f;
        public float maxSteerAngle = 30.0f;

        [Header("Collision groups")]
        [Tooltip("Group for the drivable surface. Wheels are filtered off this group so the road is not resolved twice.")]
        public int roadGroup = 1;
        [Tooltip("Group for the simulating cylinder wheels. Kept off the road group but free to hit walls and obstacles.")]
        public int wheelGroup = 2;

        [Header("Ground")]
        public bool buildGround = true;
        public Vector3 groundSize = new Vector3(200.0f, 1.0f, 200.0f);
        public Vector3 groundPosition = new Vector3(0.0f, -0.5f, 0.0f);

        private PhysxScene m_scene;
        private PhysxMaterial m_material;
        private bool m_filteringConfigured;

        private void Start()
        {
            m_scene = Resources.Load<PhysxScene>("PhysX Scene Assets/PhysXDefaultScene");
            m_material = Resources.Load<PhysxMaterial>("PhysX Physic Materials/PhysXDefaultRigidMaterial");
            if (m_scene == null)
            {
                Debug.LogError("VehicleSampleBuilder: could not load PhysXDefaultScene from Resources.");
                return;
            }

            // The wheels simulate as cylinders but must not collide with the road, which the
            // suspension raycast and tire model already resolve. Configure the process-global
            // group table once during construction, and reset it on teardown.
            PhysxCollisionGroups.SetPairCollides(wheelGroup, roadGroup, false);
            m_filteringConfigured = true;

            if (buildGround) BuildGround();
            BuildVehicle();
        }

        private void OnDestroy()
        {
            if (m_filteringConfigured) PhysxCollisionGroups.ResetToDefault();
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
            shape.CollisionGroup = roadGroup;

            PhysxStaticRigidActor actor = ground.AddComponent<PhysxStaticRigidActor>();
            actor.Scene = m_scene;

            ground.SetActive(true);
        }

        private void BuildVehicle()
        {
            float gravity = Mathf.Max(0.01f, m_scene.Gravity.magnitude);
            float sprungMass = chassisMass / 4.0f;

            // Author the spring explicitly: pick the stiffness that rests at the requested jounce
            // under gravity, and the damping from the requested ratio. No runtime synthesis.
            float clampedRest = Mathf.Clamp(restJounce, 0.001f, suspensionTravel);
            float stiffness = sprungMass * gravity / clampedRest;
            float damping = 2.0f * dampingRatio * Mathf.Sqrt(stiffness * sprungMass);
            float restLoad = sprungMass * gravity;

            PhysxVehicleWheel wheelAsset = ScriptableObject.CreateInstance<PhysxVehicleWheel>();
            wheelAsset.radius = wheelRadius;
            wheelAsset.halfWidth = wheelHalfWidth;
            wheelAsset.mass = 20.0f;

            PhysxVehicleTire tireAsset = ScriptableObject.CreateInstance<PhysxVehicleTire>();
            tireAsset.restLoad = restLoad;

            PhysxVehicleSuspension suspAsset = ScriptableObject.CreateInstance<PhysxVehicleSuspension>();
            suspAsset.travelDistance = suspensionTravel;
            suspAsset.stiffness = stiffness;
            suspAsset.damping = damping;
            suspAsset.sprungMass = sprungMass;

            GameObject vehicleGO = new GameObject("Standard Engine Drive Vehicle");
            vehicleGO.SetActive(false);
            vehicleGO.transform.position = spawnPosition;

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
            vehicle.driveType = PhysxVehicle.VehicleDriveType.Engine;
            vehicle.differentialType = PxVehicleDifferentialType.eMULTIWHEEL;
            vehicle.useDirectWheelControl = false;
            vehicle.mass = chassisMass;
            vehicle.boxHalfExtents = chassisHalfExtents;
            vehicle.centerOfMass = new Vector3(0.0f, -0.25f, 0.0f);
            vehicle.maxBrakeTorque = maxBrakeTorque;
            vehicle.maxHandbrakeTorque = maxHandbrakeTorque;
            vehicle.maxSteerAngle = maxSteerAngle;
            vehicle.ackermannEnabled = false;

            // Engine-drive requires its full drivetrain; the wrapper validates it, so assign all four.
            vehicle.engine = ScriptableObject.CreateInstance<PhysxVehicleEngine>();
            vehicle.gearbox = ScriptableObject.CreateInstance<PhysxVehicleGearbox>();
            vehicle.autobox = ScriptableObject.CreateInstance<PhysxVehicleAutobox>();
            vehicle.clutch = ScriptableObject.CreateInstance<PhysxVehicleClutch>();

            // Front axle steers and is not driven; rear axle is driven and handbrakes (RWD).
            CreateWheel(vehicleGO.transform, "Wheel FL", new Vector3(-trackHalfWidth, wheelVerticalOffset, wheelBaseHalf), 0, 0, driven: false, steering: true, handbrake: false, wheelAsset, tireAsset, suspAsset);
            CreateWheel(vehicleGO.transform, "Wheel FR", new Vector3(trackHalfWidth, wheelVerticalOffset, wheelBaseHalf), 0, 1, driven: false, steering: true, handbrake: false, wheelAsset, tireAsset, suspAsset);
            CreateWheel(vehicleGO.transform, "Wheel RL", new Vector3(-trackHalfWidth, wheelVerticalOffset, -wheelBaseHalf), 1, 0, driven: true, steering: false, handbrake: true, wheelAsset, tireAsset, suspAsset);
            CreateWheel(vehicleGO.transform, "Wheel RR", new Vector3(trackHalfWidth, wheelVerticalOffset, -wheelBaseHalf), 1, 1, driven: true, steering: false, handbrake: true, wheelAsset, tireAsset, suspAsset);

            PhysxVehicleController controller = vehicleGO.AddComponent<PhysxVehicleController>();
            controller.readKeyboardInput = true;

            vehicleGO.SetActive(true);
        }

        private void CreateWheel(Transform parent, string wheelName, Vector3 localPos, int axle, int indexInAxle,
            bool driven, bool steering, bool handbrake, PhysxVehicleWheel wheelAsset, PhysxVehicleTire tireAsset,
            PhysxVehicleSuspension suspAsset)
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
            attach.isDriven = driven;
            attach.isSteering = steering;
            attach.isHandbrake = handbrake;
            attach.wheelVisual = visual.transform;

            // Exact, rotation-invariant cylinder wheels, filtered off the road group.
            attach.geometryMode = PxwVehicleWheelGeometryMode.Cylinder;
            attach.isSimulationShape = true;
            attach.isSceneQueryShape = false;
            attach.collisionGroup = wheelGroup;
        }
    }
}
