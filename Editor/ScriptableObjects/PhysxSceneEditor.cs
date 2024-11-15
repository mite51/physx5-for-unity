using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxScene))]
    [CanEditMultipleObjects]
    public class PhysxSceneEditor : PhysxEditorBase
    {
        protected virtual void OnEnable()
        {
            m_gravity = serializedObject.FindProperty("m_gravity");
            m_pruningStructureType = serializedObject.FindProperty("m_pruningStructureType");
            m_solverType = serializedObject.FindProperty("m_solverType");
            m_useGpu = serializedObject.FindProperty("m_useGpu");
        }

        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(m_gravity);
            EditorGUILayout.PropertyField(m_pruningStructureType);
            EditorGUILayout.PropertyField(m_solverType);
            EditorGUILayout.PropertyField(m_useGpu);

            serializedObject.ApplyModifiedProperties();
        }

        private SerializedProperty m_gravity;
        private SerializedProperty m_pruningStructureType;
        private SerializedProperty m_solverType;
        private SerializedProperty m_useGpu;
    }
}
