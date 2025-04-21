using System;
using UnityEngine;

namespace PhysX5ForUnity
{
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshFilter))]
    [AddComponentMenu("PhysX 5/Actors/PhysX Dynamic Rigid Actor")]
    public class PhysxDynamicRigidActor : PhysxRigidActor
    {
        public string test = "test";

        [SerializeField]
        private float _mass = 1.0f;
        public float mass
        {
            get { return _mass; }
            set
            {
                _mass = value;
                if (m_nativeObjectPtr != IntPtr.Zero)
                {
                    Physx.SetMass(m_nativeObjectPtr, _mass);
                }
            }
        }

		[SerializeField]
		protected Vector3 _linearVelocity = Vector3.zero;
        public Vector3 linearVelocity
        {
            get { return _linearVelocity; }
            set
            {
                _linearVelocity = value;
                if (m_nativeObjectPtr != IntPtr.Zero)
                {
                    Physx.SetLinearVelocity(m_nativeObjectPtr, ref _linearVelocity);
                }
            }
        }

		[SerializeField]
		protected Vector3 _angularVelocity = Vector3.zero;
        public Vector3 angularVelocity
        {
            get { return _angularVelocity; }
            set
            {
                _angularVelocity = value;
                if (m_nativeObjectPtr != IntPtr.Zero)
                {
                    Physx.SetAngularVelocity(m_nativeObjectPtr, ref _angularVelocity);
                }
            }
        }

        protected void FixedUpdate()
        {
            PxTransformData pose;
            Physx.GetRigidActorPose(m_nativeObjectPtr, out pose);
            transform.position = pose.position;
            transform.rotation = pose.quaternion;

			_linearVelocity = Physx.GetLinearVelocity(m_nativeObjectPtr);
            _angularVelocity = Physx.GetAngularVelocity(m_nativeObjectPtr);
		}

        protected override void CreateNativeObject()
        {
            base.CreateNativeObject();
            PxTransformData pose = PxTransformData.FromTransform(transform);
            m_nativeObjectPtr = Physx.CreateDynamicRigidActor(Scene.NativeObjectPtr, ref pose, m_shape.NativeObjectPtr);
			Physx.SetMass(m_nativeObjectPtr, _mass);
			Physx.SetLinearVelocity(m_nativeObjectPtr, ref _linearVelocity);
			Physx.SetAngularVelocity(m_nativeObjectPtr, ref _angularVelocity);
		}
        
        protected override void DestroyNativeObject()
        {
            if (m_nativeObjectPtr != IntPtr.Zero)
            {
                Physx.ReleaseActor(m_nativeObjectPtr);
                m_nativeObjectPtr = IntPtr.Zero;
            }
        }
    }
}