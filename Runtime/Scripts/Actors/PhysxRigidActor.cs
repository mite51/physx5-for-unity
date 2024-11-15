using System;
using UnityEngine;

namespace PhysX5ForUnity
{
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshFilter))]
    public abstract class PhysxRigidActor : PhysxActor
    {
        public PhysxShape Shape
        {
            get { return m_shape; }
        }

        protected override void CreateNativeObject()
        {
            m_shape = GetComponent<PhysxShape>();
            if (m_shape == null) { throw new NullReferenceException(); }
        }

        protected override void EnableActor()
        {
            // some hack for reloading assembly
            if (m_nativeObjectPtr == IntPtr.Zero) CreateActor();
            Physx.AddActorToScene(m_scene.NativeObjectPtr, m_nativeObjectPtr);
        }

        protected override void DisableActor()
        {
            if (m_nativeObjectPtr != IntPtr.Zero) Physx.RemoveActorFromScene(m_scene.NativeObjectPtr, m_nativeObjectPtr);
        }

        protected PhysxGeometry m_geometry;
        protected PhysxShape m_shape;

    }
}
