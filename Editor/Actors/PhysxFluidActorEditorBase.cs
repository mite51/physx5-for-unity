using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    public class PhysxFluidActorEditorBase : PhysxParticleActorEditorBase
    {
        protected override void OnEnable()
        {
            base.OnEnable();
            m_density = serializedObject.FindProperty("m_density");
            m_buoyancy = serializedObject.FindProperty("m_buoyancy");
            m_fluidColor = serializedObject.FindProperty("m_fluidColor");
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

            if (GUI.changed) serializedObject.ApplyModifiedProperties();
        }
        protected SerializedProperty m_density;
        protected SerializedProperty m_buoyancy;
        protected SerializedProperty m_fluidColor;
    }
}