using System;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace PhysX5ForUnity
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-10)]
    public abstract class PhysxGeometry : PhysxNativeGameObjectBase
    {
        public virtual void Recreate()
        {
            if (m_nativeObjectPtr != IntPtr.Zero) DestroyGeometry();
            if (m_nativeObjectPtr == IntPtr.Zero) CreateOrGetSharedGeometry();
        }

        protected virtual void Awake()
        {
            // To avoid duplicate creation of the same geometry
            CreateOrGetSharedGeometry();
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload -= DestroyGeometry;
            AssemblyReloadEvents.beforeAssemblyReload += DestroyGeometry;
#endif
        }

        protected virtual void OnEnable()
        {
            if (m_nativeObjectPtr == IntPtr.Zero) CreateOrGetSharedGeometry();;
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload -= DestroyGeometry;
            AssemblyReloadEvents.beforeAssemblyReload += DestroyGeometry;
#endif
        }

        protected virtual void OnDestroy()
        {
            if (m_nativeObjectPtr != IntPtr.Zero) DestroyGeometry();
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload -= DestroyGeometry;
#endif
        }

        private void CreateOrGetSharedGeometry()
        {
            m_uniqueKey = GenerateUniqueKey();
            if (sm_sharedGeometries.TryGetValue(m_uniqueKey, out (IntPtr ptr, int refCount) entry))
            {
                m_nativeObjectPtr = entry.ptr;
                sm_sharedGeometries[m_uniqueKey] = (entry.ptr, entry.refCount + 1);
            }
            else
            {
                CreateGeometry();
                sm_sharedGeometries[m_uniqueKey] = (m_nativeObjectPtr, 1);
            }
        }

        protected abstract void CreateGeometry();

        protected virtual void DestroyGeometry()
        {
            if (sm_sharedGeometries.TryGetValue(m_uniqueKey, out (IntPtr ptr, int refCount) entry))
            {
                if (entry.refCount > 1)
                {
                    sm_sharedGeometries[m_uniqueKey] = (entry.ptr, entry.refCount - 1);
                }
                else
                {
                    PhysxUtils.DeletePxGeometry(entry.ptr);
                    sm_sharedGeometries.Remove(m_uniqueKey);
                }
            }
            m_nativeObjectPtr = IntPtr.Zero;
        }

        protected abstract string GenerateUniqueKey();
        
        private static Dictionary<string, (IntPtr ptr, int refCount)> sm_sharedGeometries = new Dictionary<string, (IntPtr ptr, int refCount)>();
        private string m_uniqueKey;
    }
}
