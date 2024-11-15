using System;
using UnityEngine;

namespace PhysX5ForUnity
{
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshFilter))]
    [AddComponentMenu("PhysX 5/Actors/PhysX Dynamic Rigid Actor")]
    public class PhysxDynamicRigidActor : PhysxRigidActor
    {
        protected void FixedUpdate()
        {
            PxTransformData pose;
            Physx.GetRigidActorPose(m_nativeObjectPtr, out pose);
            transform.position = pose.position;
            transform.rotation = pose.quaternion;
        }

        protected override void CreateNativeObject()
        {
            base.CreateNativeObject();
            PxTransformData pose = PxTransformData.FromTransform(transform);
            m_nativeObjectPtr = Physx.CreateDynamicRigidActor(Scene.NativeObjectPtr, ref pose, m_shape.NativeObjectPtr);
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