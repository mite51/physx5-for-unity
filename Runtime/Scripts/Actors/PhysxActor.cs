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

        /// <summary>
        /// Raised immediately before this actor's native object is released, while the handle is
        /// still valid. An external owner that holds the handle — UNDPWR's
        /// <c>DeterministicWorld</c>, which also holds an articulation cache created from it —
        /// must let go of it here, because Unity does not define the order in which it destroys
        /// objects and the owner's own teardown may not have run yet.
        /// </summary>
        /// <remarks>
        /// May be raised more than once for the same actor, since a component can be disabled and
        /// then destroyed. Handlers must be idempotent.
        /// </remarks>
        public event OnBeforeDestroyEventHandeler OnBeforeDestroy;

        /// <summary>
        /// Raises <see cref="OnBeforeDestroy"/>. Every path that releases the native object must
        /// call this first, including the ones that do not run through
        /// <see cref="DestroyActor"/>.
        /// </summary>
        protected void RaiseOnBeforeDestroy()
        {
            OnBeforeDestroy?.Invoke();
        }

        
        public PhysxScene Scene
        {
            get { return m_scene; }
            set { m_scene = value; }
        }

        [Tooltip("When set, an external owner (for example UNDPWR's DeterministicWorld) manages this " +
                 "actor's scene membership and insertion order. The component still creates its native " +
                 "object against Scene, but it does not add or remove itself from the scene; the owner " +
                 "does that in a deterministic, stable-ID order. Set this (and assign Scene to the world's " +
                 "bound scene) before the GameObject is activated.")]
        public bool externalSceneMembership = false;

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
            // Under external membership the owner holds the scene; routing through the
            // PhysxScene refcount here would (a) risk creating a second scene and (b) let a
            // later RemoveActor release the owner's shared scene out from under it.
            if (m_scene && !externalSceneMembership)
            {
                m_scene.AddActor(this);
            }
        }

        void RemoveFromScene()
        {
            if (m_scene && !externalSceneMembership)
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
            RaiseOnBeforeDestroy();
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