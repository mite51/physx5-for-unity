using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    public abstract class PhysxParticleActorEditorBase : PhysxActorEditorBase
    {
        protected override void OnEnable()
        {
            base.OnEnable();
            m_pbdParticleSystem = serializedObject.FindProperty("m_pbdParticleSystem");
            m_pbdMaterial = serializedObject.FindProperty("m_pbdMaterial");
            m_particleShader = serializedObject.FindProperty("m_particleShader");
            m_numParticles = serializedObject.FindProperty("m_numParticles");
            m_renderParticles = serializedObject.FindProperty("m_renderParticles");
        }

        protected SerializedProperty m_pbdParticleSystem;
        protected SerializedProperty m_pbdMaterial;
        protected SerializedProperty m_particleShader;
        protected SerializedProperty m_numParticles;
        protected SerializedProperty m_renderParticles;

        protected GUIContent m_pbdParticleSystemLabelContent = new GUIContent("PBD Particle System");
        protected GUIContent m_pbdMaterialLabelContent = new GUIContent("PBD Material");
    }
}