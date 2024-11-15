using System;
using UnityEngine;


namespace PhysX5ForUnity
{
    [CreateAssetMenu(fileName = "PhysX5Scene", menuName = "PhysX 5/Scene", order = 1)]
    public class PhysxScene : PhysxNativeScriptableObjectBase
    {
        public Vector3 Gravity
        {
            get { return m_gravity; }
        }
        public bool UseGpu
        {
            get { return m_useGpu; }
        }

        public void AddActor(PhysxActor actor)
        {
            if (m_dependencyCount == 0) CreateScene();
            ++m_dependencyCount;
        }

        public void RemoveActor(PhysxActor actor)
        {
            --m_dependencyCount;
            if (m_dependencyCount == 0) ReleaseScene();
        }

        public void AddPBDParticleSystem(PhysxPBDParticleSystem pbdSystem)
        {
            if (m_dependencyCount == 0) CreateScene();
            ++m_dependencyCount;
        }

        public void RemovePBDParticleSystem(PhysxPBDParticleSystem pbdSystem)
        {
            --m_dependencyCount;
            if (m_dependencyCount == 0) ReleaseScene();
        }

        void CreateScene()
        {
            if (m_nativeObjectPtr == IntPtr.Zero)
            {
                m_nativeObjectPtr = Physx.CreateScene(m_gravity, m_pruningStructureType, m_solverType, m_useGpu);
            }
        }

        void ReleaseScene()
        {
            if (m_nativeObjectPtr != IntPtr.Zero)
            {
                Physx.ReleaseScene(m_nativeObjectPtr);
                m_nativeObjectPtr = IntPtr.Zero;
            }
        }
        [SerializeField]
        private Vector3 m_gravity = new Vector3(0, -9.81f, 0);
        [SerializeField]
        private PxPruningStructureType m_pruningStructureType = PxPruningStructureType.DynamicAABBTree;
        [SerializeField]
        private PxSolverType m_solverType = PxSolverType.TGS;
        [SerializeField]
        private bool m_useGpu = true;

        private int m_dependencyCount = 0;
    }
}

