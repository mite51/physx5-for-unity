using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PhysX5ForUnity
{
    // Add these enums before the Physx class
    public enum PxD6Axis
    {
        eX = 0,
        eY = 1,
        eZ = 2,
        eTWIST = 3,
        eSWING1 = 4,
        eSWING2 = 5
    }

    public enum PxD6Motion
    {
        eLOCKED,
        eLIMITED,
        eFREE
    }

    public enum PxD6Drive
    {
        eX = 0,
        eY = 1,
        eZ = 2,
        eSWING = 3,
        eTWIST = 4,
        eSLERP = 5
    }

    public partial class Physx
    {
#if UNITY_EDITOR_LINUX
        const string PHYSX_DLL = "libPhysXUnity";
#elif UNITY_EDITOR_WIN
        const string PHYSX_DLL = "PhysXUnity.dll";
#elif UNITY_STANDALONE_LINUX
        const string PHYSX_DLL = "libPhysXUnity";
#elif  UNITY_STANDALONE_WIN
        const string PHYSX_DLL = "PhysXUnity.dll";
#endif

        /// <summary>
        /// Initialize the PhysX basics.
        /// </summary>
        [DllImport(PHYSX_DLL)]
        public static extern bool InitializePhysX();

        /// <summary>
        /// Get any error messages from PhysX.
        /// </summary>
        [DllImport(PHYSX_DLL)]
        public static extern IntPtr GetPhysxErrors();

        /// <summary>
        /// Get the initialization status of PhysX.
        /// </summary>
        [DllImport(PHYSX_DLL)]
        public static extern bool GetPhysXInitStatus();

        /// <summary>
        /// Create a PhysX scene
        /// </summary>
        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreateScene(in Vector3 gravity, PxPruningStructureType pruningStructureType, PxSolverType solverType, bool useGpu);

        /// <summary>
        /// Step all PhysX scenes for dt
        /// </summary>
        [DllImport(PHYSX_DLL)]
        public static extern void StepPhysics(float dt);

        [DllImport(PHYSX_DLL)]
        public static extern void StepPhysicsStart(float dt);

        [DllImport(PHYSX_DLL)]
        public static extern void StepPhysicsFetchResults();

        /// <summary>
        /// Release a PhysX scene
        /// </summary>
        [DllImport(PHYSX_DLL)]
        public static extern void ReleaseScene(IntPtr scene);

        /// <summary>
        /// Release all PhysX components.
        /// </summary>
        [DllImport(PHYSX_DLL)]
        public static extern void ReleasePhysX();

        // Actor basics

        [DllImport(PHYSX_DLL)]
        public static extern void AddActorToScene(IntPtr scene, IntPtr actor);

        [DllImport(PHYSX_DLL)]
        public static extern void RemoveActorFromScene(IntPtr scene, IntPtr actor);

        // Geometry creation functions
        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreateBoxGeometry(float halfWidth, float halfHeight, float halfDepth);

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreateCapsuleGeometry(float radius, float halfHeight);

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreateSphereGeometry(float radius);

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreateShape(IntPtr geometry, IntPtr material, bool isExclusive);
        
        [DllImport(PHYSX_DLL)]
        public static extern void GetShapeLocalPose(IntPtr shape, out PxTransformData destPose);

        [DllImport(PHYSX_DLL)]
        public static extern void SetShapeLocalPose(IntPtr shape, ref PxTransformData pose);

        [DllImport(PHYSX_DLL)]
        public static extern void ReleaseShape(IntPtr shape);

        [DllImport(PHYSX_DLL)]
        public static extern void AddSoftActorToScene(IntPtr softBodyHelper);

        [DllImport(PHYSX_DLL)]
        public static extern void RemoveSoftActorFromScene(IntPtr softBodyHelper);

        [DllImport(PHYSX_DLL)]
        public static extern void AddPBDParticleSystemToScene(IntPtr particleSystemHelper);

        [DllImport(PHYSX_DLL)]
        public static extern void RemovePBDParticleSystemFromScene(IntPtr particleSystemHelper);

        [DllImport(PHYSX_DLL)]
        public static extern void AddPBDObjectToParticleSystem(IntPtr particleSystemObject);

        [DllImport(PHYSX_DLL)]
        public static extern void RemovePBDObjectFromParticleSystem(IntPtr particleSystemObject);

        [DllImport(PHYSX_DLL)]
        public static extern void AddArticulationToScene(IntPtr articulation);

        [DllImport(PHYSX_DLL)]
        public static extern void RemoveArticulationFromScene(IntPtr articulation);


        // PBD objects

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreatePBDParticleSystem(IntPtr scene, float particleSpacing, int maxNumParticlesForAnisotropy);

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr ReleasePBDParticleSystem(IntPtr particleSystemHelper);

        [DllImport(PHYSX_DLL)]
        public static extern void PBDParticleSystemGetBounds(IntPtr particleSystemHelper, out PxBounds3 bounds);

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreateFluid(
            IntPtr scene,
            IntPtr particleSystem,
            IntPtr material,
            ref Vector4 positions,
            int numParticles,
            float particleSpacing,
            float fluidDensity,
            int maxDiffuseParticles,
            float buoyancy
        );

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreateTriMeshCloth(
            IntPtr scene,
            IntPtr particleSystem,
            IntPtr material,
            ref Vector3 vertices,
            int numVertices,
            ref int indices,
            int numIndices,
            ref Vector3 position,
            float totalMass,
            bool inflatable,
            float blendScale,
            float pressure,
            float particleSpacing
        );

        [DllImport(PHYSX_DLL)]
        public static extern void ResetParticleSystemObject(IntPtr obj);

        [DllImport(PHYSX_DLL)]
        public static extern PxParticleData GetParticleData(IntPtr obj);

        [DllImport(PHYSX_DLL)]
        public static extern PxAnisotropyBuffer GetAnisotropy(IntPtr obj);

        [DllImport(PHYSX_DLL)]
        public static extern PxAnisotropyBuffer GetAnisotropyAll(IntPtr obj);

        [DllImport(PHYSX_DLL)]
        public static extern PxParticleSpringData GetParticleSpringData(IntPtr obj);

        [DllImport(PHYSX_DLL)]
        public static extern void UpdateParticleSprings(IntPtr obj, ref PxParticleSpring springs, int numSprings);

        [DllImport(PHYSX_DLL)]
        public static extern void AttachParticleToRigidBody(IntPtr pbdObj, int particleIdx, IntPtr rigidActor, ref Vector3 position);

        [DllImport(PHYSX_DLL)]
        public static extern void DetachParticleFromRigidBody(IntPtr pbdObj, int particleIdx, IntPtr rigidActor);

        [DllImport(PHYSX_DLL)]
        public static extern void SyncParticleDataHostToDevice(IntPtr obj, bool copyPhase);

        public static void SyncParticleDataHostToDevice(IntPtr obj) { SyncParticleDataHostToDevice(obj, false); }

        [DllImport(PHYSX_DLL)]
        public static extern void AddParticleRigidFilter(IntPtr pbdObj, IntPtr rigidActor, int particleIdx);

        [DllImport(PHYSX_DLL)]
        public static extern void RemoveParticleRigidFilter(IntPtr pbdObj, IntPtr rigidActor, int particleIdx);

        [DllImport(PHYSX_DLL)]
        public static extern void ReleaseParticleSystemObject(IntPtr obj);

        // Rigid body

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreateDynamicRigidActor(IntPtr scene, ref PxTransformData pose, IntPtr shape);

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreateKinematicRigidActor(IntPtr scene, ref PxTransformData pose, IntPtr shape);

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreateStaticRigidActor(IntPtr scene, ref PxTransformData pose, IntPtr shape);

        [DllImport(PHYSX_DLL)]
        public static extern void GetRigidActorPose(IntPtr actor, out PxTransformData destPose);

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr SetKinematicTarget(IntPtr actor, ref PxTransformData pose);

        [DllImport(PHYSX_DLL)]
        public static extern void ReleaseActor(IntPtr actor);

        // Soft Body

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreateFEMSoftBody(
            IntPtr scene,
            int numVertices,
            ref Vector3 triVerts,
            int numTriangles,
            ref int triIndices,
            ref PxTransformData pose,
            IntPtr material,
            float density,
            int iterationCount,
            bool useCollisionMeshForSimulation,
            int numVoxelsAlongLongestAABBAxis
        );

        [DllImport(PHYSX_DLL)]
        public static extern void ResetFEMSoftBody(IntPtr softBodyHelper);

        [DllImport(PHYSX_DLL)]
        public static extern void SyncFEMSoftBodyCollisionMeshDtoH(IntPtr softBodyHelper);

        [DllImport(PHYSX_DLL)]
        public static extern PxFEMSoftBodyMeshData GetFEMSoftBodyCollisionMeshData(IntPtr softBodyHelper);

        [DllImport(PHYSX_DLL)]
        public static extern int AttachFEMSoftBodyVertexToRigidBody(IntPtr softBodyHelper, IntPtr actor, int vertId, ref Vector3 actorSpacePose);

        [DllImport(PHYSX_DLL)]
        public static extern void DetachFEMSoftBodyVertexFromRigidBody(IntPtr softBodyHelper, IntPtr actor, int attachmentHandle);

        [DllImport(PHYSX_DLL)]
        public static extern int AttachFEMSoftBodyOverlappingAreaToRigidBody(IntPtr softBodyHelper0, IntPtr rigidActor, IntPtr rigidGeometry);

        [DllImport(PHYSX_DLL)]
        public static extern int AttachFEMSoftBodyOverlappingAreaToSoftBody(IntPtr softBodyHelper0, IntPtr softBodyHelper1);

        [DllImport(PHYSX_DLL)]
        public static extern int DetachFEMSoftBodyRigidBodyAttachments(IntPtr softBodyHelper);

        [DllImport(PHYSX_DLL)]
        public static extern void ReleaseFEMSoftBody(IntPtr softBodyHelper);

        [DllImport(PHYSX_DLL)]
        public static extern void ReleaseMesh(IntPtr mesh);

        // Robotics

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreateArticulationKinematicTree(IntPtr scene, bool fixBase, bool disableSelfCollision);

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreateArticulationKinematicTreeBase(IntPtr kinematicTree, ref PxTransformData basePose, IntPtr geometry, float density);

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr AddLinkToArticulationKinematicTree(
            IntPtr kinematicTree,
            IntPtr parentLink,
            ref PxTransformData linkPose,
            PxRobotJointType type,
            ref PxTransformData jointPoseParent,
            ref PxTransformData jointPoseChild,
            PxArticulationAxis dofAxis,
            IntPtr geometry,
            float jointLimLower,
            float jointLimUpper,
            bool isDriveJoint,
            float stiffness,
            float damping,
            float driveMaxForce,
            float density
        );

        [DllImport(PHYSX_DLL)]
        public static extern void ResetArticulationKinematicTree(IntPtr kinematicTree);

        [DllImport(PHYSX_DLL)]
        public static extern void DriveArticulationKinematicTreeJoints(IntPtr kinematicTree, ref float targetJointPositions);

        [DllImport(PHYSX_DLL)]
        public static extern void ReleaseArticulationKinematicTree(IntPtr kinematicTree);

        [DllImport(PHYSX_DLL)]
        public static extern void GetArticulationKinematicTreeJointPositions(IntPtr kinematicTree, ref float destArray, int length);

        [DllImport(PHYSX_DLL)]
        public static extern void GetArticulationKinematicTreeLinkPoses(IntPtr kinematicTree, ref PxTransformData destArray, int length);

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreateArticulationRobot(IntPtr scene, ref PxTransformData basePose, float density);

        [DllImport(PHYSX_DLL)]
        public static extern void ResetArticulationRobot(IntPtr robot);

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr AddLinkToRobot(
            IntPtr robot,
            ref PxTransformData linkPose,
            PxRobotJointType type,
            ref PxTransformData jointPoseParent,
            ref PxTransformData jointPoseChild,
            IntPtr geometry,
            float jointLimLower,
            float jointLimUpper,
            float stiffness,
            float damping,
            float driveMaxForce,
            float density
        );

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr AddEndEffectorLinkToRobot(
            IntPtr robot,
            ref PxTransformData linkPose,
            PxRobotJointType type,
            ref PxTransformData jointPoseParent,
            ref PxTransformData jointPoseChild,
            IntPtr geometry,
            float jointLimLower,
            float jointLimUpper,
            float stiffness,
            float damping,
            float driveMaxForce,
            float density
        );

        [DllImport(PHYSX_DLL)]
        public static extern void ReleaseArticulationRobot(IntPtr robot);

        [DllImport(PHYSX_DLL)]
        public static extern void DriveJoints(IntPtr robot, ref float targetJointPositions);

        [DllImport(PHYSX_DLL)]
        public static extern void GetRobotLinkPoses(IntPtr robot, ref PxTransformData destArray, int length);

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr GetRobotJointPositions(IntPtr robot, ref float jointPositions, int length);

        [DllImport(PHYSX_DLL)]
        public static extern void GetRobotLinkIncomingForce(IntPtr robot, int n, out PxSpatialForceData dest);

        [DllImport(PHYSX_DLL)]
        public static extern void GetRobotForwardKinematics(IntPtr robot, ref float q, out Matrix4x4 dest);

        [DllImport(PHYSX_DLL)]
        public static extern void GetRobotJacobianBody(IntPtr robot, ref float q, ref float destArray, int rows, int cols);

        [DllImport(PHYSX_DLL)]
        public static extern void GetRobotJacobianSpatial(IntPtr robot, ref float q, ref float destArray, int rows, int cols);

        /// <summary>
        /// Robot inverse kinematics. The target EE transform is regarding the last joint's transform
        /// </summary>
        [DllImport(PHYSX_DLL)]
        public static extern bool GetRobotInverseKinematics(IntPtr robot, ref float qInit, ref PxTransformData targetTransformEEJoint, float tolerance, int numIterations, float lambda);

        static Physx()
        {
            // When using PhysX 5, disable the default Physics (PhysX 4) in Unity.
            Physics.simulationMode = SimulationMode.Script;
            InitializeIfNecessary();
        }

        // This method will be called whenever a built application starts.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void OnBeforeSceneLoadRuntimeMethod()
        {
            InitializeIfNecessary();
        }

        private static void InitializeIfNecessary()
        {
            if (!GetPhysXInitStatus())
            {
                bool success = InitializePhysX();
                if (!success)
                {
                    Debug.LogError("Failed to initialize the DLL.");
                }
            }
        }

		[DllImport(PHYSX_DLL)]
		public static extern void SetMass(IntPtr actor, float mass);

		[DllImport(PHYSX_DLL)]
		public static extern float GetMass(IntPtr actor);

        [DllImport(PHYSX_DLL)]
        public static extern void SetLinearVelocity(IntPtr actor, ref Vector3 velocity);

        [DllImport(PHYSX_DLL)]
        public static extern void SetAngularVelocity(IntPtr actor, ref Vector3 velocity); 

        [DllImport(PHYSX_DLL)]
        public static extern Vector3 GetLinearVelocity(IntPtr actor);

        [DllImport(PHYSX_DLL)]
        public static extern Vector3 GetAngularVelocity(IntPtr actor);
        
        [DllImport(PHYSX_DLL)]
        public static extern void SetRigidDynamicStabilizationThreshold(IntPtr rigidDynamic, float threshold);

        [DllImport(PHYSX_DLL)]
        public static extern float GetRigidDynamicStabilizationThreshold(IntPtr rigidDynamic);
        
        // D6 Joint functions
        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreateD6Joint(IntPtr actor0, IntPtr actor1);

        [DllImport(PHYSX_DLL)]
        public static extern void ReleaseD6Joint(IntPtr joint);

        [DllImport(PHYSX_DLL)]
        public static extern void SetD6JointDriveMotion(IntPtr joint, PxD6Axis axis, PxD6Motion motionType);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationLinkJointMotion(IntPtr link, PxArticulationAxis axis, PxArticulationMotion motion);

        [DllImport(PHYSX_DLL)]
        public static extern void SetD6JointDrive(IntPtr joint, PxD6Drive index, float driveStiffness, float driveDamping, float driveForceLimit);

        [DllImport(PHYSX_DLL)]
        public static extern void SetD6DriveVelocity(IntPtr joint, ref Vector3 linearVelocity, ref Vector3 angularVelocity);

        [DllImport(PHYSX_DLL)]
        public static extern void GetD6DriveVelocity(IntPtr joint, out Vector3 linearVelocity, out Vector3 angularVelocity);

        
        // Articulation functions
        
        // Articulation Creation and Management
        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreateArticulationRoot(PxArticulationFlag flag, int solverIterationCount);

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr AddArticulationRootToScene(IntPtr scene, IntPtr articulation);

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr RemoveArticulationRootFromScene(IntPtr scene, IntPtr articulation);


        [DllImport(PHYSX_DLL)]
        public static extern void ReleaseArticulation(IntPtr articulation);

        [DllImport(PHYSX_DLL)]
        public static extern uint GetArticulationLinkCount(IntPtr articulation);

        [DllImport(PHYSX_DLL)]
        public static extern void GetArticulationLinks(IntPtr articulation, IntPtr[] userBuffer, uint bufferSize, uint startIndex);

        [DllImport(PHYSX_DLL)]
        public static extern uint GetArticulationDofs(IntPtr articulation);

        // Articulation Link Management
        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreateArticulationLink(IntPtr articulation, IntPtr parentLink, ref PxTransformData pose);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationLinkShape(IntPtr link, IntPtr shape);

        [DllImport(PHYSX_DLL)]
        public static extern void UpdateArticulationLinkMassAndInertia(IntPtr link, float density);

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr GetLinkArticulation(IntPtr link);

        [DllImport(PHYSX_DLL)]
        public static extern void GetArticulationLinkGlobalPose(IntPtr link, out PxTransformData destPose);

        [DllImport(PHYSX_DLL)]
        public static extern uint GetArticulationLinkIndex(IntPtr link);

        [DllImport(PHYSX_DLL)]
        public static extern uint GetArticulationLinkInboundJointDof(IntPtr link);

        // Add these new DllImport declarations after the other articulation joint functions:
        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationJointArmature(IntPtr joint, PxArticulationAxis axis, float armature);

        [DllImport(PHYSX_DLL)]
        public static extern float GetArticulationJointArmature(IntPtr joint, PxArticulationAxis axis);

        // Articulation Link Properties
        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationLinkLinearDamping(IntPtr link, float linearDamping);

        [DllImport(PHYSX_DLL)]
        public static extern float GetArticulationLinkLinearDamping(IntPtr link);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationLinkAngularDamping(IntPtr link, float angularDamping);

        [DllImport(PHYSX_DLL)]
        public static extern float GetArticulationLinkAngularDamping(IntPtr link);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationLinkMaxLinearVelocity(IntPtr link, float maxLinearVelocity);

        [DllImport(PHYSX_DLL)]
        public static extern float GetArticulationLinkMaxLinearVelocity(IntPtr link);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationLinkMaxAngularVelocity(IntPtr link, float maxAngularVelocity);

        [DllImport(PHYSX_DLL)]
        public static extern float GetArticulationLinkMaxAngularVelocity(IntPtr link);

        // Articulation Joint Configuration
        [DllImport(PHYSX_DLL)]
        public static extern IntPtr GetArticulationJoint(IntPtr link);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationJointType(IntPtr joint, PxArticulationJointType type);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationJointParentPose(IntPtr joint, ref PxTransformData pose);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationJointChildPose(IntPtr joint, ref PxTransformData pose);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationJointMotion(IntPtr joint, PxArticulationAxis axis, PxArticulationMotion motion);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationJointLimitParams(IntPtr joint, PxArticulationAxis axis, float lower, float upper);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationJointDriveParams(IntPtr joint, PxArticulationAxis axis, float stiffness, float damping, float maxForce);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationJointDriveTarget(IntPtr joint, PxArticulationAxis axis, float target);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationJointDriveVelocity(IntPtr joint, PxArticulationAxis axis, float velocity);

        // Articulation Simulation Properties
        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationRootGlobalPose(IntPtr articulation, ref PxTransformData pose, bool autowake);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationSolverIterationCounts(IntPtr articulation, uint positionIters, uint velocityIters);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationSleepThreshold(IntPtr articulation, float threshold);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationStabilizationThreshold(IntPtr articulation, float threshold);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationWakeCounter(IntPtr articulation, float wakeCounterValue);

        [DllImport(PHYSX_DLL)]
        public static extern void WakeUpArticulation(IntPtr articulation);

        // New articulation link joint functions
        public static void SetArticulationLinkJointDriveParams(IntPtr link, PxArticulationAxis axis, float stiffness, float damping, float maxForce)
        {
            // Get the joint from the link
            IntPtr joint = GetArticulationJoint(link);
            if (joint != IntPtr.Zero)
            {
                // Call the joint drive params function
                SetArticulationJointDriveParams(joint, axis, stiffness, damping, maxForce);
            }
        }
/*
        public static void SetArticulationLinkJointMotion(IntPtr link, PxArticulationAxis axis, PxArticulationMotion motion)
        {
            // Get the joint from the link
            if (link != IntPtr.Zero)
            {
                IntPtr joint = GetArticulationJoint(link);
                if (joint != IntPtr.Zero)
                {                
                    // Call the joint motion function
                    SetArticulationJointMotion(joint, axis, motion);
                }
            }
        }
*/
        public static void SetArticulationLinkJointLimits(IntPtr link, PxArticulationAxis axis, float lower, float upper)
        {
            // Get the joint from the link
            if (link != IntPtr.Zero)
            {
                IntPtr joint = GetArticulationJoint(link);
                if (joint != IntPtr.Zero)
                {                   
                    SetArticulationJointLimitParams(joint, axis, Mathf.Deg2Rad * lower, Mathf.Deg2Rad * upper);
                }
            }
        }

        public static void SetArticulationLinkJointDriveTarget(IntPtr link, PxArticulationAxis axis, float target)
        {
            // Get the joint from the link
            IntPtr joint = GetArticulationJoint(link);
            if (joint != IntPtr.Zero)
            {
                float radians = Mathf.Deg2Rad * target;
                //Debug.Log($"SetArticulationLinkJointDriveTarget: {axis} {target} {radians}");
                SetArticulationJointDriveTarget(joint, axis, radians);
            }
        }

        public static void SetArticulationLinkJointDriveVelocity(IntPtr link, PxArticulationAxis axis, float velocity)
        {
            // Get the joint from the link
            IntPtr joint = GetArticulationJoint(link);
            if (joint != IntPtr.Zero)
            {
                SetArticulationJointDriveVelocity(joint, axis, velocity);
            }
        }

        [DllImport(PHYSX_DLL)]
        public static extern void PutArticulationToSleep(IntPtr articulation);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationMaxCOMLinearVelocity(IntPtr articulation, float maxLinearVelocity);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationMaxCOMAngularVelocity(IntPtr articulation, float maxAngularVelocity);

        // Articulation Cache Management
        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreateArticulationCache(IntPtr articulation);

        [DllImport(PHYSX_DLL)]
        public static extern void ReleaseArticulationCache(IntPtr cache);

        [DllImport(PHYSX_DLL)]
        public static extern void ApplyArticulationCache(IntPtr articulation, IntPtr cache, PxArticulationCacheFlags flags);

        [DllImport(PHYSX_DLL)]
        public static extern void CopyInternalStateToArticulationCache(IntPtr articulation, IntPtr cache, PxArticulationCacheFlags flags);

        [DllImport(PHYSX_DLL)]
        public static extern void GetArticulationJointPositions(IntPtr articulation, IntPtr cache, float[] positions, uint bufferSize);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationJointPositions(IntPtr articulation, IntPtr cache, float[] positions, uint bufferSize);

        [DllImport(PHYSX_DLL)]
        public static extern void GetArticulationJointVelocities(IntPtr articulation, IntPtr cache, float[] velocities, uint bufferSize);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationJointVelocities(IntPtr articulation, IntPtr cache, float[] velocities, uint bufferSize);

        // For convenience, you might want to add these helper methods that work with links directly:
        public static void SetArticulationLinkJointArmature(IntPtr link, PxArticulationAxis axis, float armature)
        {
            // Get the joint from the link
            IntPtr joint = GetArticulationJoint(link);
            if (joint != IntPtr.Zero)
            {
                SetArticulationJointArmature(joint, axis, armature);
            }
        }

        public static float GetArticulationLinkJointArmature(IntPtr link, PxArticulationAxis axis)
        {
            // Get the joint from the link
            IntPtr joint = GetArticulationJoint(link);
            if (joint != IntPtr.Zero)
            {
                return GetArticulationJointArmature(joint, axis);
            }
            return 0.0f;
        }

        public static string GetPhysxErrorString()
        {
            IntPtr ptr = GetPhysxErrors();
            return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) : null;
        }

        // Articulation Cache Management - New API
        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreateArticulationInternalStateCache(IntPtr articulation);

        [DllImport(PHYSX_DLL)]
        public static extern void ReleaseArticulationInternalStateCache(IntPtr cache);

        [DllImport(PHYSX_DLL)]
        public static extern void CopyArticulationInternalStateToCache(IntPtr articulation, IntPtr cache, uint/*PxArticulationCacheFlags*/ flags);

        [DllImport(PHYSX_DLL)]
        public static extern void ApplyArticulationInternalStateCache(IntPtr articulation, IntPtr cache, uint/*PxArticulationCacheFlags*/ flags);

        // Direct cache access functions
        [DllImport(PHYSX_DLL)]
        public static extern void GetArticulationCacheJointPositions(IntPtr cache, [Out] float[] positions, uint bufferSize);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationCacheJointPositions(IntPtr cache, [In] float[] positions, uint bufferSize);

        [DllImport(PHYSX_DLL)]
        public static extern void GetArticulationCacheJointVelocities(IntPtr cache, [Out] float[] velocities, uint bufferSize);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationCacheJointVelocities(IntPtr cache, [In] float[] velocities, uint bufferSize);

        [DllImport(PHYSX_DLL)]
        public static extern void GetArticulationCacheJointAccelerations(IntPtr cache, [Out] float[] accelerations, uint bufferSize);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationCacheJointAccelerations(IntPtr cache, [In] float[] accelerations, uint bufferSize);

        [DllImport(PHYSX_DLL)]
        public static extern void GetArticulationCacheJointForces(IntPtr cache, [Out] float[] forces, uint bufferSize);

        [DllImport(PHYSX_DLL)]
        public static extern void SetArticulationCacheJointForces(IntPtr cache, [In] float[] forces, uint bufferSize);
	}
}

