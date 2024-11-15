using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxFEMSoftBodyActor))]
    [CanEditMultipleObjects]
    public class PhysxFEMSoftBodyActorEditor : PhysxActorEditorBase
    {
        protected override void OnEnable()
        {
            base.OnEnable();
            m_material = serializedObject.FindProperty("m_material");
            m_density = serializedObject.FindProperty("m_density");
            m_iterationCount = serializedObject.FindProperty("m_iterationCount");
            m_useCollisionMeshForSimulation = serializedObject.FindProperty("m_useCollisionMeshForSimulation");
            m_numVoxelsAlongLongestAABBAxis = serializedObject.FindProperty("m_numVoxelsAlongLongestAABBAxis");
            m_recalculateMesh = serializedObject.FindProperty("m_recalculateMesh");
        }

        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_scene, m_sceneLabelContent);
            EditorGUILayout.PropertyField(m_material);
            EditorGUILayout.PropertyField(m_density);
            EditorGUILayout.PropertyField(m_iterationCount);
            EditorGUILayout.PropertyField(m_density);
            EditorGUILayout.PropertyField(m_useCollisionMeshForSimulation);
            if (!m_useCollisionMeshForSimulation.boolValue)
            {
                EditorGUILayout.IntSlider(m_numVoxelsAlongLongestAABBAxis, 4, 64, "Voxels Along Longest Axis");
            }
            
            GUI.enabled = true;
            EditorGUILayout.PropertyField(m_recalculateMesh);

            if (GUI.changed) serializedObject.ApplyModifiedProperties();
        }

        protected SerializedProperty m_material;
        protected SerializedProperty m_density;
        protected SerializedProperty m_iterationCount;
        protected SerializedProperty m_useCollisionMeshForSimulation;
        protected SerializedProperty m_numVoxelsAlongLongestAABBAxis;
        protected SerializedProperty m_recalculateMesh;
    }
}
