using System;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace PhysX5ForUnity
{
    [AddComponentMenu("PhysX 5/Geometries/PhysX Shape")]
    //[DisallowMultipleComponent]
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
            Physx.SetShapeLocalPose(m_nativeObjectPtr, ref m_createdLocalPose);
            ApplyFilterData();
        }

        /// <summary>
        /// Pushes the authored collision group into the shape's simulation filter data, which the
        /// default filter shader reads as a group index. See <see cref="PhysxCollisionGroups"/>.
        /// </summary>
        private void ApplyFilterData()
        {
            if (m_nativeObjectPtr == IntPtr.Zero) return;

            if (!PhysxCollisionGroups.IsValidGroup(m_collisionGroup))
            {
                Debug.LogError(
                    $"PhysxShape '{name}': collision group {m_collisionGroup} is out of range; " +
                    $"valid groups are 0..{PhysxCollisionGroups.MaxGroups - 1}. Using " +
                    $"{PhysxCollisionGroups.DefaultGroup}.", this);
                m_collisionGroup = PhysxCollisionGroups.DefaultGroup;
            }

            Physx.SetShapeSimulationFilterData(m_nativeObjectPtr, (uint)m_collisionGroup, 0, 0, 0);
        }

        private void CreateShape()
        {
            if (m_nativeObjectPtr != IntPtr.Zero) return;
            m_geometry = GetComponent<PhysxGeometry>();
            m_createdLocalPose = CalculateLocalPose();
            if(m_material == null)
            {
                //m_material = Resources.Load<PhysxRigidMaterial>("PhysX Physic Materials/PhysXDefaultRigidMaterial");
                m_material = ScriptableObject.CreateInstance<PhysxRigidMaterial>();
            }            

            m_material.AddShape(this);
            m_geometry = GetComponent<PhysxGeometry>();
            m_geometry.CreateOrGetSharedGeometry();
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
            // The collision group is part of the key because it is per-shape state: two
            // components sharing one native shape would otherwise each overwrite the other's
            // group, and the last one to initialise would silently decide the filtering for both.
            var p = m_createdLocalPose.position;
            var q = m_createdLocalPose.quaternion;
            return FormattableString.Invariant($"{m_geometry.NativeObjectPtr}_{m_material.GetEntityId()}_{m_collisionGroup}_{p.x:R}_{p.y:R}_{p.z:R}_{q.x:R}_{q.y:R}_{q.z:R}_{q.w:R}");
        }

        // Establish per-shape state before attachment (shared attached shapes cannot be mutated).
        private PxTransformData CalculateLocalPose()
        {
            var owner = GetComponentInParent<PhysxActor>(true);
            var rotation = m_geometry is PhysxCapsuleGeometry capsule ? capsule.NativeLocalRotation : Quaternion.identity;
            if (owner == null || owner.transform == transform)
                return new PxTransformData(Vector3.zero, rotation);
            // Direction-aware capsules use a rigid physical frame: dimensions ignore scale,
            // while child world positions still include the authored hierarchy's scale.
            // Preserve the original offset interpretation for legacy assets.
            var position = m_geometry is PhysxCapsuleGeometry c && c.UseAuthoredDirection
                ? Quaternion.Inverse(owner.transform.rotation) * (transform.position - owner.transform.position)
                : owner.transform.InverseTransformPoint(transform.position);
            return new PxTransformData(position,
                Quaternion.Inverse(owner.transform.rotation) * transform.rotation * rotation);
        }

        private PxTransformData m_createdLocalPose;

        private static Dictionary<string, (IntPtr ptr, int refCount)> sm_sharedShapes = new Dictionary<string, (IntPtr ptr, int refCount)>();
        private string m_uniqueKey;
        private PhysxGeometry m_geometry;

        [SerializeField]
        private PhysxMaterial m_material;
        
        public PhysxMaterial Material
        {
            get { return m_material; }
            set { m_material = value; } // This setter exists for dynamic runtime construction. Setting it after the object is registered with an active simulation will have no effect.
        }
        [SerializeField]
        private bool isExclusive = true;

        [SerializeField]
        [Tooltip("Collision group index (0-31) written to the shape's simulation filter data. " +
                 "Group 0 collides with everything unless the group table says otherwise; use " +
                 "PhysxCollisionGroups to disable specific group pairs.")]
        private int m_collisionGroup = PhysxCollisionGroups.DefaultGroup;

        /// <summary>
        /// Collision group index this shape reports to the filter shader. See
        /// <see cref="PhysxCollisionGroups"/> for what the groups mean.
        /// </summary>
        public int CollisionGroup
        {
            get { return m_collisionGroup; }
            // The group is written to the shape's simulation filter data once, in ApplyFilterData
            // during CreateShape. There is no path that re-applies it, and quietly accepting a
            // change here would leave the field disagreeing with the live shape -- and, because
            // the group feeds the construction hash, with what peers were told at admission. So
            // once the native shape exists this is rejected loudly rather than silently ignored.
            set
            {
                if (value == m_collisionGroup) return;
                if (m_nativeObjectPtr != IntPtr.Zero)
                {
                    Debug.LogError(
                        $"PhysxShape '{name}': CollisionGroup cannot change from {m_collisionGroup} " +
                        $"to {value} after the native shape has been created; set it before creation. " +
                        "Ignoring.", this);
                    return;
                }
                m_collisionGroup = value;
            }
        }
    }
}
