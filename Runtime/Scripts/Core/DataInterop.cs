using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PhysX5ForUnity
{
    public class ParticleData
    {
        public int NumParticles = 0;
        public Vector4[] SharedPositionInvMass;
        public Vector4[] SharedVelocity;
        public int IndexOffset;
        public IntPtr NativeParticleObjectPtr = IntPtr.Zero;

        public ArraySegment<Vector4> PositionInvMass
        {
            get { return new ArraySegment<Vector4>(SharedPositionInvMass, IndexOffset, NumParticles); }
        }
        public ArraySegment<Vector4> Velocity
        {
            get { return new ArraySegment<Vector4>(SharedVelocity, IndexOffset, NumParticles); }
        }

        public ParticleData(int numParticles, Vector4[] sharedPositionInvMass, Vector4[] sharedVelocity)
        {
            NumParticles = numParticles;
            SharedPositionInvMass = sharedPositionInvMass;
            SharedVelocity = sharedVelocity;
        }

        public void SetParticle(int index, Vector4 p, bool syncImmediately = false)
        {
            PositionInvMass.Array[index + PositionInvMass.Offset] = p;
            if (syncImmediately)
            {
                PhysxUtils.FastCopy(p, m_pxParticleData.positionInvMass, index);
            }
        }

        public void SetVelocity(int index, Vector3 v, bool syncImmediately = false)
        {
            Velocity.Array[index + Velocity.Offset] = v;
            if (syncImmediately)
            {
                PhysxUtils.FastCopy(v, m_pxParticleData.velocity, index);
            }
        }

        public void SyncParticlesManagedToHost()
        {
            PhysxUtils.FastCopy(PositionInvMass, m_pxParticleData.positionInvMass);
            PhysxUtils.FastCopy(Velocity, m_pxParticleData.velocity);
        }

        public void SyncParticlesSet(bool syncToHost = true)
        {
            if (m_pxParticleData.positionInvMass == IntPtr.Zero || m_pxParticleData.velocity == IntPtr.Zero) return;
            if (syncToHost) SyncParticlesManagedToHost();
            Physx.SyncParticleDataHostToDevice(NativeParticleObjectPtr);
        }

        public void SyncParticlesGet()
        {
            m_pxParticleData = Physx.GetParticleData(NativeParticleObjectPtr);
            if (m_pxParticleData.numParticles != NumParticles)
            {
                return;
            }
            PhysxUtils.FastCopy(m_pxParticleData.positionInvMass, PositionInvMass);
            PhysxUtils.FastCopy(m_pxParticleData.velocity, Velocity);
        }
        private PxParticleData m_pxParticleData;
    }

    public static class TransformExtensions
    {
        public static PxTransformData ToPxTransformData(this Transform t)
        {
            if (t == null)
                throw new ArgumentNullException(nameof(t));

            return new PxTransformData(t.position, t.rotation);
        }

        public static PxTransformData ToPxLocalTransformData(this Transform t)
        {
            if (t == null)
                throw new ArgumentNullException(nameof(t));

            return new PxTransformData(t.localPosition, t.localRotation);
        }        

        public static PxTransformData ToPxTransformData(this Matrix4x4 t)
        {
            Vector3 position;
            position.x = t.m03;
            position.y = t.m13;
            position.z = t.m23;

            Vector3 forward;
            forward.x = t.m02;
            forward.y = t.m12;
            forward.z = t.m22;

            Vector3 upwards;
            upwards.x = t.m01;
            upwards.y = t.m11;
            upwards.z = t.m21;

            return new PxTransformData(position, Quaternion.LookRotation(forward, upwards));
        }
    }

    public struct ParticleRigidFilterPair
    {
        public int ParticleIndex;
        public PhysxNativeGameObjectBase RigidActor; // some hack to allow PhysxArticulationLink to be used as well
        public ParticleRigidFilterPair(int particleIndex, PhysxNativeGameObjectBase rigidActor)
        {
            ParticleIndex = particleIndex;
            RigidActor = rigidActor;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxTransformData
    {
        public Vector3 position;
        public Quaternion quaternion;

        public PxTransformData(Vector3 position, Quaternion quaternion)
        {
            this.position = position;
            this.quaternion = quaternion;
        }

        public static PxTransformData FromTransform(Transform transform)
        {
            return new PxTransformData(transform.position, transform.rotation);
        }
    };

    [StructLayout(LayoutKind.Sequential)]
    public struct PxBounds3
    {
        public Vector3 minimum;
        public Vector3 maximum;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxParticleData
    {
        public int numParticles;
        public IntPtr positionInvMass;
        public IntPtr velocity;
        public IntPtr phase;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxAnisotropyBuffer
    {
        public IntPtr anisotropy1;
        public IntPtr anisotropy2;
        public IntPtr anisotropy3;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxParticleSpringData
    {
        public int numSprings;      //!< particle index of first particle
        public IntPtr springs;      //!< particle index of second particle
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxParticleSpring
    {
        public int ind0;           //!< particle index of first particle
        public int ind1;           //!< particle index of second particle
        public float length;       //!< spring length
        public float stiffness;    //!< spring stiffness
        public float damping;      //!< spring damping factor
        public float pad;          //!< padding bytes.
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxFEMSoftBodyMeshData
    {
        public int numVertices;
        public IntPtr positionInvMass;
        public IntPtr velocity;
    };

    [StructLayout(LayoutKind.Sequential)]
    public struct PxSpatialForceData
    {
        public Vector3 force;
        public Vector3 torque;
    };

    public enum PxRobotJointType
    {
        Fix = 0,
        Prismatic = 1,
        Revolute = 2,
        Spherical = 4,
	}

    public enum PxArticulationAxis
    {
		Twist = 0,		//!< Rotational about eX
		Swing1 = 1,	    //!< Rotational about eY
		Swing2 = 2,	    //!< Rotational about eZ
		X = 3,			//!< Linear in eX
		Y = 4,			//!< Linear in eY
		Z = 5,			//!< Linear in eZ
		Count = 6
    }

    public enum PxGeometryType
    {
        Sphere,
        Plane,
        Capsule,
        Box,
        ConvexMesh,
        ParticleSystem,
        TetrahedronMesh,
        TriangleMesh,
        HeightField,
        HairSystem,
        Custom,
    }

    public enum PxSdfBitsPerSubgridPixel
    {
        Bit8PerPixel = 1,   //!< 8 bit per subgrid pixel (values will be stored as normalized integers)
        Bit16PerPixel = 2,  //!< 16 bit per subgrid pixel (values will be stored as normalized integers)
        Bit32PerPixel = 4   //!< 32 bit per subgrid pixel (values will be stored as floats in world scale units)
    }

    public enum PxFEMSoftBodyMaterialModel
    {
        [InspectorName("Co-Rotational")]
        CoRotational,   //!< Default model. Well suited for high stiffness. Does need tetrahedra with good shapes (no extreme slivers) in the rest pose.
        [InspectorName("Neo-Hookean")]
        NeoHookean      //!< Well suited for lower stiffness. Robust to any tetrahedron shape.
    }

    // Scene related

    public enum PxPruningStructureType
    {
        None,                  //!< Using a simple data structure
        DynamicAABBTree,     //!< Using a dynamic AABB tree
        StaticAABBTree,      //!< Using a static AABB tree
        Last
    }

    public enum PxSolverType
    {
        PGS,   //!< Projected Gauss-Seidel iterative solver
        TGS    //!< Temporal Gauss-Seidel solver
    };

    // Articulation related enums
    public enum PxArticulationJointType
    {
        Fix = 0,
        Prismatic = 1,
        Revolute = 2,
        RevoluteUnWrapped = 3,
        Spherical = 4,
        Undefined = 5
    }

    public enum PxArticulationMotion
    {
        Locked,
        Limited,
        Free
    }

    public enum PxArticulationFlag
    {
        FixBase = 1 << 0,
        DriveLimitsAreForces = 1 << 1,
        DisableSelfCollision = 1 << 2
    }
    /*
    public enum PxArticulationCacheFlags
    {
        Position = 1 << 0,
        Velocity = 1 << 1,
        Force = 1 << 2,
        JointTorque = 1 << 3,
        AccelerationRoot = 1 << 4,
        LinkVelocity = 1 << 5,
        JointAcceleration = 1 << 6,
        All = Position | Velocity | Force | JointTorque | AccelerationRoot | LinkVelocity | JointAcceleration
    }
    */
}
