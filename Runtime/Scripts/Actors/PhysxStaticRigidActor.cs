using System;
using UnityEngine;

namespace PhysX5ForUnity
{
    [AddComponentMenu("PhysX 5/Actors/PhysX Static Rigid Actor")]
    public class PhysxStaticRigidActor : PhysxRigidActor
    {
        protected override void CreateNativeObject()
        {
            base.CreateNativeObject();
            PxTransformData pose = PxTransformData.FromTransform(transform);
            m_nativeObjectPtr = Physx.CreateStaticRigidActor(Scene.NativeObjectPtr, ref pose, m_shape.NativeObjectPtr);
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
