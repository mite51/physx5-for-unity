using UnityEditor;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxPBDParticleSystem))]
    [CanEditMultipleObjects]
    public class PhysxPBDParticleSystemEditor : PhysxActorEditorBase
    {
        protected override void OnEnable()
        {
            base.OnEnable();
            m_particleSpacing = serializedObject.FindProperty("m_particleSpacing");
            m_maxNumParticles = serializedObject.FindProperty("m_maxNumParticles");
            m_maxNumParticlesForAnisotropy = serializedObject.FindProperty("m_maxNumParticlesForAnisotropy");
            m_fluidShadowCastingMode = serializedObject.FindProperty("m_fluidShadowCastingMode");
        }

        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_scene, m_sceneLabelContent);
            EditorGUILayout.PropertyField(m_particleSpacing);
            EditorGUILayout.PropertyField(m_maxNumParticles);
            EditorGUILayout.PropertyField(m_maxNumParticlesForAnisotropy);
            EditorGUILayout.PropertyField(m_fluidShadowCastingMode);

            serializedObject.ApplyModifiedProperties();
        }

        protected SerializedProperty m_particleSpacing;
        protected SerializedProperty m_maxNumParticles;
        protected SerializedProperty m_maxNumParticlesForAnisotropy;
        protected SerializedProperty m_fluidShadowCastingMode;
    }
}
