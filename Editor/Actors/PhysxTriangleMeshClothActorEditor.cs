using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxTriangleMeshClothActor))]
    [CanEditMultipleObjects]
    public class PhysxTriangleMeshClothActorEditor : PhysxParticleActorEditorBase
    {
        protected override void OnEnable()
        {
            base.OnEnable();
            m_pbdParticleSystem = serializedObject.FindProperty("m_pbdParticleSystem");
            m_totalMass = serializedObject.FindProperty("m_totalMass");
            m_fixBoundary = serializedObject.FindProperty("m_fixBoundary");
            m_inflatable = serializedObject.FindProperty("m_inflatable");
            m_pressure = serializedObject.FindProperty("m_pressure");
            m_recalculateMesh = serializedObject.FindProperty("m_recalculateMesh");
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
            EditorGUILayout.PropertyField(m_totalMass);
            EditorGUILayout.PropertyField(m_inflatable);
            EditorGUILayout.PropertyField(m_pressure);
            EditorGUILayout.PropertyField(m_fixBoundary);

            GUI.enabled = true;
            EditorGUILayout.PropertyField(m_recalculateMesh);

            if (GUI.changed) serializedObject.ApplyModifiedProperties();
        }

        private SerializedProperty m_totalMass;
        private SerializedProperty m_fixBoundary;
        private SerializedProperty m_inflatable;
        private SerializedProperty m_pressure;
        private SerializedProperty m_recalculateMesh;
    }
}
