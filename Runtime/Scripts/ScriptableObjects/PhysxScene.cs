using System;
using UnityEngine;


namespace PhysX5ForUnity
{
    [CreateAssetMenu(fileName = "PhysX5Scene", menuName = "PhysX 5/Scene", order = 1)]
    public class PhysxScene : PhysxNativeScriptableObjectBase
    {
        public Vector3 Gravity
        {
            get { return m_gravity; }
        }
        public bool UseGpu
        {
            get { return m_useGpu; }
        }

        /// <summary>
        /// Whether this scene reports contacts, making
        /// <see cref="Physx.GetArticulationContactFlags"/> usable for its articulations.
        /// </summary>
        public bool EnableContactEvents
        {
            get { return m_enableContactEvents; }
        }

        public void AddActor(PhysxActor actor)
        {
            if (m_dependencyCount == 0) CreateScene();
            ++m_dependencyCount;
        }

        public void RemoveActor(PhysxActor actor)
        {
            --m_dependencyCount;
            if (m_dependencyCount == 0) ReleaseScene();
        }

        public void AddPBDParticleSystem(PhysxPBDParticleSystem pbdSystem)
        {
            if (m_dependencyCount == 0) CreateScene();
            ++m_dependencyCount;
        }

        public void RemovePBDParticleSystem(PhysxPBDParticleSystem pbdSystem)
        {
            --m_dependencyCount;
            if (m_dependencyCount == 0) ReleaseScene();
        }

        /// <summary>
        /// The scene-wide vehicle simulation context. Every vehicle in this native scene shares one
        /// update mode, one set of tire slip denominators and one substep policy. The context is
        /// pushed to the native scene at creation time and is immutable once the first vehicle is
        /// registered, so it must live on the scene asset rather than on any single vehicle.
        /// </summary>
        public PxwVehicleSceneContextDesc VehicleContext => new PxwVehicleSceneContextDesc
        {
            physxActorUpdateMode = (int)m_vehicleActorUpdateMode,
            minActiveLongSlipDenominator = m_vehicleMinActiveLongSlipDenominator,
            minPassiveLongSlipDenominator = m_vehicleMinPassiveLongSlipDenominator,
            minLatSlipDenominator = m_vehicleMinLatSlipDenominator,
            lowSubstepCount = m_vehicleLowSubstepCount,
            highSubstepCount = m_vehicleHighSubstepCount,
            substepThresholdSpeed = m_vehicleSubstepThresholdSpeed
        };

        void CreateScene()
        {
            if (m_nativeObjectPtr == IntPtr.Zero)
            {
                PxSceneCreateFlags flags = m_enableContactEvents
                    ? PxSceneCreateFlags.EnableContactEvents
                    : PxSceneCreateFlags.None;
                m_nativeObjectPtr = Physx.CreateSceneWithFlags(m_gravity, m_pruningStructureType, m_solverType, m_useGpu, flags);

                // Push the one shared vehicle context before any vehicle can register. This is a
                // no-op for scenes that never hold a vehicle, and it is the only place the context
                // can be set because it becomes immutable at first vehicle registration.
                PxwVehicleSceneContextDesc vehicleContext = VehicleContext;
                Physx.SetVehicleSceneContext(m_nativeObjectPtr, ref vehicleContext);
            }
        }

        void ReleaseScene()
        {
            if (m_nativeObjectPtr != IntPtr.Zero)
            {
                Physx.ReleaseScene(m_nativeObjectPtr);
                m_nativeObjectPtr = IntPtr.Zero;
            }
        }
        [SerializeField]
        private Vector3 m_gravity = new Vector3(0, -9.81f, 0);
        [SerializeField]
        private PxPruningStructureType m_pruningStructureType = PxPruningStructureType.DynamicAABBTree;
        [SerializeField]
        private PxSolverType m_solverType = PxSolverType.TGS;
        [SerializeField]
        private bool m_useGpu = true;
        [SerializeField]
        [Tooltip("Report contacts so per-link contact flags can be read back. Adds only " +
                 "notification flags, so the scene simulates identically either way.")]
        private bool m_enableContactEvents = false;

        [Header("Vehicle context (shared by all vehicles in this scene)")]
        [SerializeField]
        [Tooltip("How the vehicle writes its integrated motion back to the PhysX rigid body. " +
                 "Apply Velocity is the canonical parity setting.")]
        private PxVehiclePhysXActorUpdateMode m_vehicleActorUpdateMode = PxVehiclePhysXActorUpdateMode.ApplyVelocity;

        [SerializeField]
        [Tooltip("Tire minimum active longitudinal slip denominator (0.1 for the canonical profile). " +
                 "<= 0 keeps the PhysX default.")]
        private float m_vehicleMinActiveLongSlipDenominator = 0.1f;

        [SerializeField]
        [Tooltip("Tire minimum passive longitudinal slip denominator (4 for the canonical profile). " +
                 "<= 0 keeps the PhysX default.")]
        private float m_vehicleMinPassiveLongSlipDenominator = 4.0f;

        [SerializeField]
        [Tooltip("Tire minimum lateral slip denominator (1 for the canonical profile). " +
                 "<= 0 keeps the PhysX default.")]
        private float m_vehicleMinLatSlipDenominator = 1.0f;

        [SerializeField]
        [Tooltip("Suspension/tire/wheel substep count below the threshold speed (3 for the canonical profile).")]
        private int m_vehicleLowSubstepCount = 3;

        [SerializeField]
        [Tooltip("Suspension/tire/wheel substep count at or above the threshold speed (3 for the canonical profile).")]
        private int m_vehicleHighSubstepCount = 3;

        [SerializeField]
        [Tooltip("Forward speed (m/s) at which the substep count switches from low to high (5 for the canonical profile).")]
        private float m_vehicleSubstepThresholdSpeed = 5.0f;

        private int m_dependencyCount = 0;
    }
}

