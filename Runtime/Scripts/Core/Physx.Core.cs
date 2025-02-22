using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PhysX5ForUnity
{
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

        [DllImport(PHYSX_DLL)]
        public static extern IntPtr CreateShape(IntPtr geometry, IntPtr material, bool isExclusive);

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
            float driveGainP,
            float driveGainD,
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
            float driveGainP,
            float driveGainD,
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
            float driveGainP,
            float driveGainD,
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
	}
}

