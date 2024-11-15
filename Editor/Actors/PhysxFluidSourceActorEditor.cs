using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxFluidSourceActor))]
    [CanEditMultipleObjects]
    public class PhysxFluidSourceActorEditor : PhysxFluidActorEditorBase
    {
        protected override void OnEnable()
        {
            base.OnEnable();
            m_nozzleSurface = serializedObject.FindProperty("m_nozzleSurface");
            m_nozzleDensity = serializedObject.FindProperty("m_nozzleDensity");
            m_maxNumParticles = serializedObject.FindProperty("m_maxNumParticles");
            m_inactiveParticlePosition = serializedObject.FindProperty("m_inactiveParticlePosition");
            m_startSpeed = serializedObject.FindProperty("m_startSpeed");
        }

        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(m_scene, m_sceneLabelContent);
            EditorGUILayout.PropertyField(m_pbdParticleSystem, m_pbdParticleSystemLabelContent);
            EditorGUILayout.PropertyField(m_pbdMaterial, m_pbdMaterialLabelContent);
            EditorGUILayout.PropertyField(m_renderParticles);
            if (m_renderParticles.boolValue)
            {
                EditorGUILayout.PropertyField(m_particleShader);
            }
            EditorGUILayout.PropertyField(m_fluidColor);

            EditorGUILayout.PropertyField(m_density);
            EditorGUILayout.PropertyField(m_buoyancy);

            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            EditorGUILayout.PropertyField(m_maxNumParticles);

            GUI.enabled = true;
            EditorGUILayout.PropertyField(m_startSpeed);
            GUI.enabled = m_currentGUIEnabled;
            
            EditorGUILayout.PropertyField(m_nozzleSurface);
            EditorGUILayout.PropertyField(m_nozzleDensity);
            EditorGUILayout.PropertyField(m_inactiveParticlePosition);

            if (GUI.changed) serializedObject.ApplyModifiedProperties();
        }
        
        private SerializedProperty m_nozzleSurface;
        private SerializedProperty m_nozzleDensity;
        private SerializedProperty m_maxNumParticles;
        private SerializedProperty m_inactiveParticlePosition;
        private SerializedProperty m_startSpeed;
    }
}