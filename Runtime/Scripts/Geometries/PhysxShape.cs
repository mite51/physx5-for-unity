using System;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace PhysX5ForUnity
{
    [AddComponentMenu("PhysX 5/Geometries/PhysX Shape")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-9)]
    public class PhysxShape : PhysxNativeGameObjectBase
    {
        public PhysxGeometry Geometry
        {
            get {return m_geometry; }
        }

        public void Recreate()
        {
            if (m_nativeObjectPtr != IntPtr.Zero) DestroyShape();
            if (m_nativeObjectPtr == IntPtr.Zero) CreateShape();
        }

        private void Awake()
        {
            CreateShape();
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload -= DestroyShape;
            AssemblyReloadEvents.beforeAssemblyReload += DestroyShape;
#endif
        }

        private void OnEnable()
        {
            if (m_nativeObjectPtr == IntPtr.Zero) CreateShape();
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload -= DestroyShape;
            AssemblyReloadEvents.beforeAssemblyReload += DestroyShape;
#endif
        }

        private void OnDestroy()
        {
            if (m_nativeObjectPtr != IntPtr.Zero) DestroyShape();
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload -= DestroyShape;
#endif
        }

        private void CreateOrGetSharedShape()
        {
            m_uniqueKey = GenerateUniqueKey();
            if (sm_sharedShapes.TryGetValue(m_uniqueKey, out (IntPtr ptr, int refCount) entry))
            {
                m_nativeObjectPtr = entry.ptr;
                sm_sharedShapes[m_uniqueKey] = (entry.ptr, entry.refCount + 1);
            }
            else
            {
                CreateExclusiveShape();
                sm_sharedShapes[m_uniqueKey] = (m_nativeObjectPtr, 1);
            }
        }

        private void CreateExclusiveShape()
        {
            m_nativeObjectPtr = Physx.CreateShape(m_geometry.NativeObjectPtr, m_material.NativeObjectPtr, isExclusive);
        }

        private void CreateShape()
        {
            m_material.AddShape(this);
            m_geometry = GetComponent<PhysxGeometry>();
            if (isExclusive) CreateExclusiveShape();
            else CreateOrGetSharedShape();
        }

        private void DestroyShape()
        {
            if (isExclusive)
            {
                if (m_nativeObjectPtr != IntPtr.Zero) Physx.ReleaseShape(m_nativeObjectPtr);
            }
            else if (sm_sharedShapes.TryGetValue(m_uniqueKey, out (IntPtr ptr, int refCount) entry))
            {
                if (entry.refCount > 1)
                {
                    sm_sharedShapes[m_uniqueKey] = (entry.ptr, entry.refCount - 1);
                }
                else
                {
                    Physx.ReleaseShape(entry.ptr);
                    sm_sharedShapes.Remove(m_uniqueKey);
                }
            }
            m_material.RemoveShape(this);
            m_nativeObjectPtr = IntPtr.Zero;
        }

        private string GenerateUniqueKey()
        {
            return $"{m_geometry.NativeObjectPtr}_{m_material.GetInstanceID()}"; // this is ugly but seems to work.
        }

        private static Dictionary<string, (IntPtr ptr, int refCount)> sm_sharedShapes = new Dictionary<string, (IntPtr ptr, int refCount)>();
        private string m_uniqueKey;
        private PhysxGeometry m_geometry;

        [SerializeField]
        private PhysxMaterial m_material;
        [SerializeField]
        private bool isExclusive = true;
    }
}
