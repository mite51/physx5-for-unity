using System;

namespace PhysX5ForUnity
{
    public abstract class PhysxMaterial : PhysxNativeScriptableObjectBase
    {
        public virtual void AddActor(PhysxActor actor)
        {
            if (m_dependencyCount == 0) CreateMaterial();
            ++m_dependencyCount;
        }

        public virtual void RemoveActor(PhysxActor actor)
        {
            --m_dependencyCount;
            if (m_dependencyCount == 0) DestroyMaterial();
        }

        public virtual void AddShape(PhysxShape shape)
        {
            if (m_dependencyCount == 0) CreateMaterial();
            ++m_dependencyCount;
        }

        public virtual void RemoveShape(PhysxShape shape)
        {
            --m_dependencyCount;
            if (m_dependencyCount == 0) DestroyMaterial();
        }

        protected abstract void CreateMaterial();

        protected virtual void DestroyMaterial()
        {
            Physx.ReleasePxMaterial(m_nativeObjectPtr);
            m_nativeObjectPtr = IntPtr.Zero;
        }

        protected int m_dependencyCount = 0;
    }
}