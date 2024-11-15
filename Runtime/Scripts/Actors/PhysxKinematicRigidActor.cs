using System;
using UnityEngine;

namespace PhysX5ForUnity
{
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshFilter))]
    [AddComponentMenu("PhysX 5/Actors/PhysX Kinematic Rigid Actor")]
    public class PhysxKinematicRigidActor : PhysxRigidActor
    {
        protected void FixedUpdate()
        {
            if (transform.hasChanged)
            {
                PxTransformData pose = PxTransformData.FromTransform(transform);
                Physx.SetKinematicTarget(m_nativeObjectPtr, ref pose);
            }
        }

        protected override void CreateNativeObject()
        {
            base.CreateNativeObject();
            PxTransformData pose = PxTransformData.FromTransform(transform);
            m_nativeObjectPtr = Physx.CreateKinematicRigidActor(Scene.NativeObjectPtr, ref pose, m_shape.NativeObjectPtr);
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