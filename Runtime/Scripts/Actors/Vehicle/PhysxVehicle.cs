using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysX5ForUnity
{
    /// <summary>
    /// A PhysX Vehicle2 chassis, mirroring Omniverse's PhysxVehicleAPI. Collects its
    /// child <see cref="PhysxVehicleWheelAttachment"/>s, translates the referenced
    /// reusable part assets into native descriptors, and drives the underlying
    /// PxwVehicle through the standard PhysxActor lifecycle.
    /// </summary>
    [AddComponentMenu("PhysX 5/Vehicle/PhysX Vehicle")]
    [DefaultExecutionOrder(40)]
    public class PhysxVehicle : PhysxActor
    {
        public enum VehicleDriveType
        {
            None = 0,   // no throttle response; only coasts or is driven per-wheel
            Direct = 1, // Omniverse DriveBasic
            Engine = 2  // Omniverse DriveStandard
        }

        [Header("Drive model")]
        public VehicleDriveType driveType = VehicleDriveType.Engine;

        [Tooltip("Differential layout for engine-drive vehicles.")]
        public PxVehicleDifferentialType differentialType = PxVehicleDifferentialType.eMULTIWHEEL;

        [Tooltip("Bypass the differential/steer/brake command path and drive each wheel directly " +
                 "(Omniverse PhysxVehicleWheelControllerAPI). Automatically enabled when a " +
                 "PhysxVehicleWheelController is present on any wheel.")]
        public bool useDirectWheelControl = false;

        [Header("Chassis")]
        [Tooltip("Chassis rigid-body mass in kilograms.")]
        public float mass = 1500.0f;

        [Tooltip("Chassis moment of inertia. If any component is <= 0 it is estimated from the box extents.")]
        public Vector3 momentOfInertia = Vector3.zero;

        [Tooltip("Centre of mass offset in vehicle-local space.")]
        public Vector3 centerOfMass = new Vector3(0.0f, -0.25f, 0.0f);

        [Tooltip("Half extents of the fallback collision box, used only when no PhysxGeometry is present.")]
        public Vector3 boxHalfExtents = new Vector3(0.9f, 0.4f, 2.2f);

        [Tooltip("Chassis physics material. If empty a default material is created.")]
        public PhysxMaterial chassisMaterial;

        [Tooltip("Optional chassis collision geometry. If empty the fallback box (boxHalfExtents) is used.")]
        public PhysxGeometry chassisGeometry;

        [Header("Frame")]
        public PxVehicleAxes longitudinalAxis = PxVehicleAxes.ePosZ;
        public PxVehicleAxes lateralAxis = PxVehicleAxes.ePosX;
        public PxVehicleAxes verticalAxis = PxVehicleAxes.ePosY;
        public float lengthScale = 1.0f;

        [Header("Brakes / steering")]
        [Tooltip("Maximum brake torque applied by the primary brake command (Nm).")]
        public float maxBrakeTorque = 3000.0f;

        [Tooltip("Maximum brake torque applied by the handbrake command to handbrake wheels (Nm).")]
        public float maxHandbrakeTorque = 3000.0f;

        [Tooltip("Maximum steer angle applied to steering wheels (degrees).")]
        public float maxSteerAngle = 30.0f;

        [Tooltip("Enable Ackermann steer correction between the two steering wheels.")]
        public bool ackermannEnabled = false;

        [Tooltip("Ackermann strength (0..1). 1 = full geometric correction.")]
        public float ackermannStrength = 1.0f;

        [Header("Direct drive")]
        [Tooltip("Maximum drive torque per wheel for the Direct drive throttle response (Nm).")]
        public float maxDriveTorque = 1000.0f;

        [Header("Road geometry")]
        public PxVehicleRoadGeometryQueryType roadQueryType = PxVehicleRoadGeometryQueryType.eRAYCAST;

        public PhysxVehicleTireFrictionTable tireFrictionTable;

        [Header("Drivetrain assets (engine drive)")]
        public PhysxVehicleEngine engine;
        public PhysxVehicleGearbox gearbox;
        public PhysxVehicleAutobox autobox;
        public PhysxVehicleClutch clutch;

        private readonly List<PhysxVehicleWheelAttachment> m_wheels = new List<PhysxVehicleWheelAttachment>();
        private PxwVehicleWheelState[] m_wheelStates;
        private readonly List<PhysxMaterial> m_addedMaterials = new List<PhysxMaterial>();
        private bool m_finalized = false;

        public IntPtr VehiclePtr => m_nativeObjectPtr;
        public bool IsFinalized => m_finalized;
        public IReadOnlyList<PhysxVehicleWheelAttachment> Wheels => m_wheels;

        public void CollectWheels()
        {
            m_wheels.Clear();
            GetComponentsInChildren(true, m_wheels);
            // Deterministic order: axle first, then explicit index within the axle.
            m_wheels.Sort((a, b) =>
            {
                int c = a.axle.CompareTo(b.axle);
                return c != 0 ? c : a.indexInAxle.CompareTo(b.indexInAxle);
            });
            for (int i = 0; i < m_wheels.Count; ++i)
                m_wheels[i].WheelId = i;
        }

        protected override void CreateNativeObject()
        {
            CollectWheels();
            if (m_wheels.Count == 0)
            {
                Debug.LogWarning($"PhysxVehicle '{name}' has no PhysxVehicleWheelAttachment children; skipping creation.");
                return;
            }

            if (!useDirectWheelControl && GetComponentInChildren<PhysxVehicleWheelController>(true) != null)
                useDirectWheelControl = true;

            IntPtr materialPtr = ResolveMaterial(chassisMaterial);

            if (chassisGeometry == null)
                chassisGeometry = GetComponent<PhysxGeometry>();
            IntPtr geometryPtr = (chassisGeometry != null) ? chassisGeometry.NativeObjectPtr : IntPtr.Zero;

            PxwVehicleChassisDesc chassis = BuildChassisDesc();
            int nativeDriveMode = (driveType == VehicleDriveType.Engine)
                ? (int)PxVehicleDriveMode.eENGINE
                : (int)PxVehicleDriveMode.eDIRECT;

            m_nativeObjectPtr = Physx.CreateVehicle(Scene.NativeObjectPtr, nativeDriveMode, ref chassis, geometryPtr, materialPtr);
            if (m_nativeObjectPtr == IntPtr.Zero)
            {
                Debug.LogError($"PhysxVehicle '{name}': native CreateVehicle failed.");
                return;
            }

            ConfigureFrame();
            ConfigureAxlesAndWheels();
            ConfigureBrakesAndSteering();
            if (driveType == VehicleDriveType.Engine)
                ConfigureEngineDrivetrain();
            else
                ConfigureDirectDrive();
            ConfigureTireFriction();

            Physx.SetVehicleRoadGeometryQueryType(m_nativeObjectPtr, (int)roadQueryType);
            Physx.SetVehicleUseDirectWheelControl(m_nativeObjectPtr, useDirectWheelControl);

            m_finalized = Physx.FinalizeVehicle(m_nativeObjectPtr);
            if (!m_finalized)
                Debug.LogError($"PhysxVehicle '{name}': FinalizeVehicle failed (check wheel/axle/differential configuration).");

            m_wheelStates = new PxwVehicleWheelState[m_wheels.Count];
        }

        protected override void DestroyNativeObject()
        {
            if (m_nativeObjectPtr != IntPtr.Zero)
            {
                Physx.ReleaseVehicle(m_nativeObjectPtr);
                m_nativeObjectPtr = IntPtr.Zero;
            }
            m_finalized = false;

            foreach (PhysxMaterial mat in m_addedMaterials)
            {
                if (mat != null) mat.RemoveActor(this);
            }
            m_addedMaterials.Clear();
        }

        protected override void EnableActor()
        {
            if (m_nativeObjectPtr == IntPtr.Zero) CreateActor();
            if (m_finalized) Physx.AddVehicleToScene(m_nativeObjectPtr);
        }

        protected override void DisableActor()
        {
            if (m_nativeObjectPtr != IntPtr.Zero && m_finalized)
                Physx.RemoveVehicleFromScene(m_nativeObjectPtr);
        }

        protected void FixedUpdate()
        {
            if (!m_finalized || m_nativeObjectPtr == IntPtr.Zero) return;

            Physx.GetVehicleRigidBodyPose(m_nativeObjectPtr, out PxTransformData pose);
            transform.position = pose.position;
            transform.rotation = pose.quaternion;

            if (m_wheelStates != null && m_wheelStates.Length == m_wheels.Count)
            {
                Physx.GetVehicleWheelStates(m_nativeObjectPtr, m_wheelStates, m_wheelStates.Length);
                for (int i = 0; i < m_wheels.Count; ++i)
                    m_wheels[i].ApplyWheelLocalPose(transform, m_wheelStates[i].localPose);
            }
        }

        // --- Control passthrough (used by PhysxVehicleController / PhysxVehicleWheelController) ---

        public void SetCommands(float brake, float handbrake, float throttle, float steer)
        {
            if (m_finalized) Physx.SetVehicleCommands(m_nativeObjectPtr, brake, handbrake, throttle, steer);
        }

        public void SetTransmissionCommand(int targetGear, float clutch)
        {
            if (m_finalized) Physx.SetVehicleTransmissionCommand(m_nativeObjectPtr, targetGear, clutch);
        }

        public void SetTankThrusts(float thrust0, float thrust1)
        {
            if (m_finalized) Physx.SetVehicleTankThrusts(m_nativeObjectPtr, thrust0, thrust1);
        }

        public void SetWheelControl(int wheelId, float driveTorque, float brakeTorque, float steerAngle)
        {
            if (m_finalized) Physx.SetVehicleWheelControl(m_nativeObjectPtr, wheelId, driveTorque, brakeTorque, steerAngle);
        }

        public PxwVehicleDriveState GetDriveState()
        {
            PxwVehicleDriveState state = default;
            if (m_finalized) Physx.GetVehicleDriveState(m_nativeObjectPtr, out state);
            return state;
        }

        // --- Build helpers ---

        private IntPtr ResolveMaterial(PhysxMaterial material)
        {
            if (material == null)
                material = ScriptableObject.CreateInstance<PhysxRigidMaterial>();
            material.AddActor(this);
            m_addedMaterials.Add(material);
            return material.NativeObjectPtr;
        }

        private PxwVehicleChassisDesc BuildChassisDesc()
        {
            Vector3 moi = momentOfInertia;
            if (moi.x <= 0.0f || moi.y <= 0.0f || moi.z <= 0.0f)
            {
                // Solid box inertia estimate from the fallback extents.
                Vector3 d = 2.0f * boxHalfExtents;
                float k = mass / 12.0f;
                moi = new Vector3(
                    k * (d.y * d.y + d.z * d.z),
                    k * (d.x * d.x + d.z * d.z),
                    k * (d.x * d.x + d.y * d.y));
            }

            return new PxwVehicleChassisDesc
            {
                mass = mass,
                moi = moi,
                cmassLocalPose = new PxTransformData(centerOfMass, Quaternion.identity),
                boxHalfExtents = boxHalfExtents,
                boxLocalPose = new PxTransformData(Vector3.zero, Quaternion.identity)
            };
        }

        private void ConfigureFrame()
        {
            PxwVehicleFrameDesc frame = new PxwVehicleFrameDesc
            {
                lngAxis = (int)longitudinalAxis,
                latAxis = (int)lateralAxis,
                vrtAxis = (int)verticalAxis,
                scale = lengthScale
            };
            Physx.SetVehicleFrame(m_nativeObjectPtr, ref frame);
        }

        private void ConfigureAxlesAndWheels()
        {
            // Axle description grouped by the wheels' axle index (already sorted).
            List<int> nbWheelsPerAxle = new List<int>();
            List<int> wheelIdsInAxleOrder = new List<int>();
            int currentAxle = int.MinValue;
            int countInAxle = 0;
            foreach (PhysxVehicleWheelAttachment w in m_wheels)
            {
                if (w.axle != currentAxle)
                {
                    if (countInAxle > 0) nbWheelsPerAxle.Add(countInAxle);
                    currentAxle = w.axle;
                    countInAxle = 0;
                }
                wheelIdsInAxleOrder.Add(w.WheelId);
                countInAxle++;
            }
            if (countInAxle > 0) nbWheelsPerAxle.Add(countInAxle);

            Physx.SetVehicleAxleDescription(m_nativeObjectPtr, nbWheelsPerAxle.Count,
                nbWheelsPerAxle.ToArray(), wheelIdsInAxleOrder.ToArray());

            float gravMag = (Scene != null) ? Mathf.Max(0.01f, Scene.Gravity.magnitude) : 9.81f;
            float sprungMassFallback = mass / m_wheels.Count;

            foreach (PhysxVehicleWheelAttachment w in m_wheels)
            {
                int id = w.WheelId;

                PxwVehicleWheelDesc wheelDesc = w.GetWheelDesc();
                Physx.SetVehicleWheelParams(m_nativeObjectPtr, id, ref wheelDesc);

                PxwVehicleSuspensionDesc suspDesc = w.GetSuspensionDesc(transform, sprungMassFallback);
                if (suspDesc.sprungMass <= 0.0f) suspDesc.sprungMass = sprungMassFallback;
                if (suspDesc.stiffness <= 0.0f)
                    suspDesc.stiffness = suspDesc.sprungMass * gravMag / Mathf.Max(0.01f, suspDesc.travelDist) * 2.0f;
                if (suspDesc.damping <= 0.0f)
                    suspDesc.damping = Mathf.Sqrt(suspDesc.stiffness * suspDesc.sprungMass);
                Physx.SetVehicleSuspensionParams(m_nativeObjectPtr, id, ref suspDesc);

                PxwVehicleSuspensionComplianceDesc compDesc = w.GetComplianceDesc();
                Physx.SetVehicleSuspensionCompliance(m_nativeObjectPtr, id, ref compDesc);

                PxwVehicleTireDesc tireDesc = w.GetTireDesc();
                if (tireDesc.restLoad <= 0.0f) tireDesc.restLoad = suspDesc.sprungMass * gravMag;
                Physx.SetVehicleTireParams(m_nativeObjectPtr, id, ref tireDesc);
            }
        }

        private void ConfigureBrakesAndSteering()
        {
            int n = m_wheels.Count;

            // Primary brake: all wheels.
            PxwVehicleBrakeDesc brake0 = new PxwVehicleBrakeDesc
            {
                maxResponse = maxBrakeTorque,
                wheelResponseMultipliers = new float[PxVehicleLimits.MaxNbWheels],
                nbWheels = n
            };
            for (int i = 0; i < n; ++i) brake0.wheelResponseMultipliers[i] = 1.0f;
            Physx.SetVehicleBrakeParams(m_nativeObjectPtr, 0, ref brake0);

            // Handbrake: flagged wheels only.
            PxwVehicleBrakeDesc brake1 = new PxwVehicleBrakeDesc
            {
                maxResponse = maxHandbrakeTorque,
                wheelResponseMultipliers = new float[PxVehicleLimits.MaxNbWheels],
                nbWheels = n
            };
            for (int i = 0; i < n; ++i) brake1.wheelResponseMultipliers[i] = m_wheels[i].isHandbrake ? 1.0f : 0.0f;
            Physx.SetVehicleBrakeParams(m_nativeObjectPtr, 1, ref brake1);

            // Steering: flagged wheels, response = max steer angle in radians.
            PxwVehicleSteerDesc steer = new PxwVehicleSteerDesc
            {
                maxResponse = maxSteerAngle * Mathf.Deg2Rad,
                wheelResponseMultipliers = new float[PxVehicleLimits.MaxNbWheels],
                nbWheels = n
            };
            for (int i = 0; i < n; ++i) steer.wheelResponseMultipliers[i] = m_wheels[i].isSteering ? 1.0f : 0.0f;
            Physx.SetVehicleSteerParams(m_nativeObjectPtr, ref steer);

            // Ackermann across the first two steering wheels.
            List<int> steerIds = new List<int>();
            foreach (PhysxVehicleWheelAttachment w in m_wheels)
                if (w.isSteering) steerIds.Add(w.WheelId);

            PxwVehicleAckermannDesc ack = new PxwVehicleAckermannDesc
            {
                wheelIds = new int[2] { steerIds.Count > 0 ? steerIds[0] : 0, steerIds.Count > 1 ? steerIds[1] : 1 },
                wheelBase = EstimateWheelBase(),
                trackWidth = EstimateTrackWidth(steerIds),
                strength = ackermannStrength,
                enabled = (ackermannEnabled && steerIds.Count >= 2) ? 1 : 0
            };
            Physx.SetVehicleAckermannParams(m_nativeObjectPtr, ref ack);
        }

        private void ConfigureDirectDrive()
        {
            int n = m_wheels.Count;
            float[] multipliers = new float[PxVehicleLimits.MaxNbWheels];
            int nbDriven = 0;
            for (int i = 0; i < n; ++i)
            {
                bool driven = m_wheels[i].isDriven;
                multipliers[i] = driven ? 1.0f : 0.0f;
                if (driven) nbDriven++;
            }
            float maxResponse = (driveType == VehicleDriveType.None) ? 0.0f : maxDriveTorque;
            Physx.SetVehicleDirectDriveThrottleParams(m_nativeObjectPtr, maxResponse, multipliers, n);
        }

        private void ConfigureEngineDrivetrain()
        {
            PxwVehicleDifferentialDesc diff = BuildDifferentialDesc();
            Physx.SetVehicleDifferentialParams(m_nativeObjectPtr, ref diff);

            PxwVehicleEngineDesc engineDesc = (engine != null)
                ? engine.ToDesc()
                : ScriptableObject.CreateInstance<PhysxVehicleEngine>().ToDesc();
            Physx.SetVehicleEngineParams(m_nativeObjectPtr, ref engineDesc);

            PxwVehicleGearboxDesc gearboxDesc = (gearbox != null)
                ? gearbox.ToDesc()
                : ScriptableObject.CreateInstance<PhysxVehicleGearbox>().ToDesc();
            Physx.SetVehicleGearboxParams(m_nativeObjectPtr, ref gearboxDesc);

            PxwVehicleAutoboxDesc autoboxDesc = (autobox != null)
                ? autobox.ToDesc()
                : ScriptableObject.CreateInstance<PhysxVehicleAutobox>().ToDesc();
            Physx.SetVehicleAutoboxParams(m_nativeObjectPtr, ref autoboxDesc);

            PxwVehicleClutchDesc clutchDesc = (clutch != null)
                ? clutch.ToDesc()
                : ScriptableObject.CreateInstance<PhysxVehicleClutch>().ToDesc();
            Physx.SetVehicleClutchParams(m_nativeObjectPtr, ref clutchDesc);
        }

        private PxwVehicleDifferentialDesc BuildDifferentialDesc()
        {
            int n = m_wheels.Count;
            PxwVehicleDifferentialDesc diff = new PxwVehicleDifferentialDesc
            {
                type = (int)differentialType,
                torqueRatios = new float[PxVehicleLimits.MaxNbWheels],
                aveWheelSpeedRatios = new float[PxVehicleLimits.MaxNbWheels],
                frontWheelIds = new int[2] { 0, 1 },
                rearWheelIds = new int[2] { 2, 3 },
                thrustIdPerTrack = new int[PxVehicleLimits.MaxNbWheels],
                nbWheelsPerTrack = new int[PxVehicleLimits.MaxNbWheels],
                trackToWheelIds = new int[PxVehicleLimits.MaxNbWheels],
                wheelIdsInTrackOrder = new int[PxVehicleLimits.MaxNbWheels]
            };

            List<int> driven = new List<int>();
            for (int i = 0; i < n; ++i)
                if (m_wheels[i].isDriven) driven.Add(m_wheels[i].WheelId);
            if (driven.Count == 0)
                for (int i = 0; i < n; ++i) driven.Add(m_wheels[i].WheelId);

            float share = 1.0f / driven.Count;
            foreach (int id in driven)
            {
                diff.torqueRatios[id] = share;
                diff.aveWheelSpeedRatios[id] = share;
            }

            if (differentialType == PxVehicleDifferentialType.eFOURWHEEL && driven.Count >= 4)
            {
                diff.frontWheelIds = new int[2] { driven[0], driven[1] };
                diff.rearWheelIds = new int[2] { driven[2], driven[3] };
                diff.frontBias = 1.3f; diff.frontTarget = 1.29f;
                diff.rearBias = 1.3f; diff.rearTarget = 1.29f;
                diff.centerBias = 1.3f; diff.centerTarget = 1.29f;
                diff.rate = 10.0f;
            }
            else if (differentialType == PxVehicleDifferentialType.eTANK)
            {
                // Two tracks split by lateral position of each driven wheel.
                List<int> left = new List<int>();
                List<int> right = new List<int>();
                foreach (PhysxVehicleWheelAttachment w in m_wheels)
                {
                    if (!w.isDriven) continue;
                    float lateral = transform.InverseTransformPoint(w.transform.position).x;
                    if (lateral < 0.0f) left.Add(w.WheelId); else right.Add(w.WheelId);
                }

                diff.nbTracks = 2;
                diff.thrustIdPerTrack[0] = 0;
                diff.thrustIdPerTrack[1] = 1;
                diff.nbWheelsPerTrack[0] = left.Count;
                diff.nbWheelsPerTrack[1] = right.Count;
                diff.trackToWheelIds[0] = 0;
                diff.trackToWheelIds[1] = left.Count;
                int idx = 0;
                foreach (int id in left) diff.wheelIdsInTrackOrder[idx++] = id;
                foreach (int id in right) diff.wheelIdsInTrackOrder[idx++] = id;
            }

            return diff;
        }

        private void ConfigureTireFriction()
        {
            if (tireFrictionTable == null || tireFrictionTable.entries.Count == 0)
            {
                Physx.SetVehicleTireFrictionTable(m_nativeObjectPtr, new IntPtr[0], new float[0], 0,
                    tireFrictionTable != null ? tireFrictionTable.defaultFriction : 1.0f);
                return;
            }

            List<IntPtr> mats = new List<IntPtr>();
            List<float> frictions = new List<float>();
            foreach (PhysxVehicleTireFrictionTable.FrictionEntry entry in tireFrictionTable.entries)
            {
                if (entry.material == null) continue;
                entry.material.AddActor(this);
                m_addedMaterials.Add(entry.material);
                mats.Add(entry.material.NativeObjectPtr);
                frictions.Add(entry.friction);
            }

            Physx.SetVehicleTireFrictionTable(m_nativeObjectPtr, mats.ToArray(), frictions.ToArray(),
                mats.Count, tireFrictionTable.defaultFriction);
        }

        private float EstimateWheelBase()
        {
            // Distance along the longitudinal axis between front-most and rear-most wheels.
            if (m_wheels.Count < 2) return 1.0f;
            float min = float.MaxValue, max = float.MinValue;
            foreach (PhysxVehicleWheelAttachment w in m_wheels)
            {
                float lng = transform.InverseTransformPoint(w.transform.position).z;
                min = Mathf.Min(min, lng);
                max = Mathf.Max(max, lng);
            }
            return Mathf.Max(0.1f, max - min);
        }

        private float EstimateTrackWidth(List<int> steerIds)
        {
            if (steerIds.Count < 2) return 1.0f;
            PhysxVehicleWheelAttachment a = m_wheels[steerIds[0]];
            PhysxVehicleWheelAttachment b = m_wheels[steerIds[1]];
            float xa = transform.InverseTransformPoint(a.transform.position).x;
            float xb = transform.InverseTransformPoint(b.transform.position).x;
            return Mathf.Max(0.1f, Mathf.Abs(xa - xb));
        }
    }
}
