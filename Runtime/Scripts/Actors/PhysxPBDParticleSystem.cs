using System;
using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    [AddComponentMenu("PhysX 5/Actors/PhysX PBD Particle System")]
    public class PhysxPBDParticleSystem : PhysxNativeGameObjectBase
    {
        public PhysxScene Scene
        {
            get { return m_scene; }
            set { m_scene = value; }
        }

        public float ParticleSpacing
        {
            get { return m_particleSpacing; }
            set { m_particleSpacing = Mathf.Max(value, 0.001f); }
        }

        public Vector4[] SharedPositionInvMass
        {
            get { return m_sharedPositionInvMass; }
        }

        public Vector4[] SharedVelocity
        {
            get { return m_sharedVelocity; }
        }

#if UNITY_EDITOR
        [MenuItem("GameObject/PhysX 5/PBD Particle System")]
        public static void AddGameObjectInScene()
        {
            GameObject parent = Selection.activeGameObject;
            GameObject gameObject = new GameObject("PBD Particle System");
            gameObject.AddComponent<PhysxPBDParticleSystem>();
            Undo.RegisterCreatedObjectUndo(gameObject, "Create PBD Particle System");  // Register the creation for undo

            if (parent != null)
            {
                // Make the new object a child of the selected object
                gameObject.transform.SetParent(parent.transform);
            }
                gameObject.transform.position = Vector3.zero;
                gameObject.transform.rotation = Quaternion.identity;
                gameObject.transform.localScale = Vector3.one;

            Selection.activeObject = gameObject;

            // Open the rename mode for the new GameObject
            EditorApplication.delayCall += () =>
            {
                Selection.activeObject = gameObject;
                EditorApplication.ExecuteMenuItem("Edit/Rename");
            };
        }
# endif

        public void AddActor(PhysxParticleActor actor)
        {
            // In case it has not been created
            if (m_nativeObjectPtr == IntPtr.Zero)
            {
                CreatePBDParticleSystem();
            }

            if (actor is PhysxFluidActor)
            {
                // for rendering
                if (!m_isHelperInitialized)
                {
                    m_sharedFluidColors = new Color[m_maxNumParticles];
                    m_pbdParticleSystemHelper = GetComponent<PhysxPBDParticleSystemFluidRenderer>();
                    gameObject.GetComponent<MeshRenderer>().shadowCastingMode = m_fluidShadowCastingMode;
                    gameObject.GetComponent<MeshFilter>().sharedMesh = new Mesh()
                    {
                        // hack to make it always visible initially, this ensures OnWillRenderObject be called at least once to initialize the resources
                        bounds = new Bounds(new Vector3(), new Vector3(float.MaxValue, float.MaxValue, float.MaxValue))
                    };
                    m_pbdParticleSystemHelper.PBDParticleSystem = this;
                    m_pbdParticleSystemHelper.SharedPositionInvMass = m_sharedPositionInvMass;
                    m_pbdParticleSystemHelper.SharedFluidColors = m_sharedFluidColors;
                    m_pbdParticleSystemHelper.ActiveFluidIndices = new int[0];
                    m_isHelperInitialized = true;
                }

                m_pbdParticleSystemHelper.AddActor((PhysxFluidActor)actor);
                for (int i = 0; i < actor.NumParticles; ++i)
                {
                    m_sharedFluidColors[m_numParticles + i] = ((PhysxFluidActor)actor).FluidColor;
                }
                ((PhysxFluidActor)actor).SetColors += SetFluidActorColor;
            }

            actor.ParticleData = new ParticleData(actor.NumParticles, m_sharedPositionInvMass, m_sharedVelocity)
            {
                IndexOffset = m_numParticles
            };
            m_numParticles += actor.NumParticles;
            if (m_pbdParticleSystemHelper) m_pbdParticleSystemHelper.NumParticles = m_numParticles;
            ++m_actorCount;
        }

        public void RemoveActor(PhysxParticleActor actor)
        {
            --m_actorCount;
            if (m_actorCount == 0)
            {
                ReleasePBDParticleSystem();
            }
        }

        public void EnableActor(PhysxParticleActor actor)
        {
            if (m_enabledActorCount==0 && m_nativeObjectPtr != IntPtr.Zero)
            {
                Physx.AddPBDParticleSystemToScene(m_nativeObjectPtr);
                m_inScene = true;
            }
            m_enabledActorCount++;
        }

        public void DisableActor(PhysxParticleActor actor)
        {
            --m_enabledActorCount;
            if (m_inScene && m_enabledActorCount == 0 && m_nativeObjectPtr != IntPtr.Zero)
            {
                Physx.RemovePBDParticleSystemFromScene(m_nativeObjectPtr);
                m_inScene = false;
            }
        }

        private void SetFluidActorColor(int indexOffset, int numParticles, Color color)
        {
            for (int i = indexOffset; i < indexOffset + numParticles; ++i)
            {
                m_sharedFluidColors[i] = color;
            }
            m_pbdParticleSystemHelper.UpdateColorsBuffer();
        }

        private void OnEnable()
        {
            if (m_enabledActorCount > 0)
            {
                CreatePBDParticleSystem();
                Physx.AddPBDParticleSystemToScene(m_nativeObjectPtr);
                m_inScene = true;
            }
        }

        private void OnDisable()
        {
            if (m_enabledActorCount > 0 && m_nativeObjectPtr != IntPtr.Zero)
            {
                Physx.RemovePBDParticleSystemFromScene(m_nativeObjectPtr);
                m_inScene = false;
            }
        }

        void CreatePBDParticleSystem()
        {
            if (m_nativeObjectPtr == IntPtr.Zero)
            {
                m_scene.AddPBDParticleSystem(this);
                m_nativeObjectPtr = Physx.CreatePBDParticleSystem(m_scene.NativeObjectPtr, m_particleSpacing, m_maxNumParticlesForAnisotropy);
                m_sharedPositionInvMass = new Vector4[m_maxNumParticles];
                m_sharedVelocity = new Vector4[m_maxNumParticles];
                m_numParticles = 0;
            }
        }

        void ReleasePBDParticleSystem()
        {
            if (m_nativeObjectPtr != IntPtr.Zero)
            {
                Physx.ReleasePBDParticleSystem(m_nativeObjectPtr);
                m_nativeObjectPtr = IntPtr.Zero;
                m_sharedPositionInvMass = null;
                m_sharedVelocity = null;
                m_numParticles = 0;
                m_isHelperInitialized = false;
                m_scene.RemovePBDParticleSystem(this);
            }
        }

        [SerializeField]
        private PhysxScene m_scene;
        [SerializeField]
        private float m_particleSpacing = 0.1f;
        [SerializeField]
        private int m_maxNumParticles = 30000;
        [SerializeField]
        private int m_maxNumParticlesForAnisotropy = 10000;
        [SerializeField]
        private UnityEngine.Rendering.ShadowCastingMode m_fluidShadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        private int m_actorCount = 0;
        private int m_enabledActorCount = 0;
        private bool m_isHelperInitialized = false;
        private PhysxPBDParticleSystemFluidRenderer m_pbdParticleSystemHelper = null;
        private Vector4[] m_sharedPositionInvMass;
        private Vector4[] m_sharedVelocity;
        private Color[] m_sharedFluidColors;
        private int m_numParticles = 0;
        private bool m_inScene = false;
    }
}
