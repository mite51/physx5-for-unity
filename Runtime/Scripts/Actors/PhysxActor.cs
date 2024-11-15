using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PhysX5ForUnity
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(35)]
    public abstract class PhysxActor : PhysxNativeGameObjectBase
    {
        public delegate void OnBeforeDestroyEventHandeler();
        public event OnBeforeDestroyEventHandeler OnBeforeDestroy;
        
        public PhysxScene Scene
        {
            get { return m_scene; }
            set { m_scene = value; }
        }

        public virtual void Recreate()
        {
            Physx.StepPhysicsFetchResults(); // in case the simulation is running
            DisableActor();
            DestroyActor();
            CreateActor();
            if (isActiveAndEnabled)
            {
                EnableActor();
            }
        }

        protected virtual void Awake()
        {
            CreateActor();
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload -= DestroyActor; // avoid duplicate
            AssemblyReloadEvents.beforeAssemblyReload += DestroyActor;
#endif
        }

        protected virtual void OnEnable()
        {
            EnableActor();
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload -= DestroyActor; // avoid duplicate
            AssemblyReloadEvents.beforeAssemblyReload += DestroyActor;
#endif
        }

        protected virtual void OnDisable()
        {
            Physx.StepPhysicsFetchResults(); // always finish the simulation before modifying the scenes
            DisableActor();
        }

        protected virtual void OnDestroy()
        {
            DestroyActor();
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload -= DestroyActor;
#endif
        }

        void AddToScene()
        {
            if (m_scene)
            {
                m_scene.AddActor(this);
            }
        }

        void RemoveFromScene()
        {
            if (m_scene)
            {
                m_scene.RemoveActor(this);
            }
        }

        protected virtual void CreateActor()
        {
            AddToScene();
            CreateNativeObject();
        }

        protected virtual void DestroyActor()
        {
            OnBeforeDestroy?.Invoke();
            DestroyNativeObject();
            RemoveFromScene();
        }

        protected abstract void EnableActor();

        protected abstract void DisableActor();

        protected abstract void CreateNativeObject();

        protected abstract void DestroyNativeObject();

        [SerializeField]
        protected PhysxScene m_scene;
    }
}