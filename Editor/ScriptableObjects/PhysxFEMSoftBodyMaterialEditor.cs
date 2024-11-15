using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxFEMSoftBodyMaterial))]
    [CanEditMultipleObjects]
    public class PhysxFEMSoftBodyMaterialEditor : PhysxEditorBase
    {
        private void OnEnable()
        {
            m_youngs = serializedObject.FindProperty("m_youngs");
            m_poisson = serializedObject.FindProperty("m_poisson");
            m_dynamicFriction = serializedObject.FindProperty("m_dynamicFriction");
            m_damping = serializedObject.FindProperty("m_damping");
            m_model = serializedObject.FindProperty("m_model");
        }

        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_youngs, m_youngsContent);
            EditorGUILayout.PropertyField(m_poisson, m_poissonContent);
            EditorGUILayout.PropertyField(m_dynamicFriction, m_dynamicFrictionContent);
            EditorGUILayout.PropertyField(m_damping, m_dampingContent);
            EditorGUILayout.PropertyField(m_model, m_modelContent);

            serializedObject.ApplyModifiedProperties();
        }

        private SerializedProperty m_youngs;
        private SerializedProperty m_poisson;
        private SerializedProperty m_dynamicFriction;
        private SerializedProperty m_damping;
        private SerializedProperty m_model;
        private GUIContent m_youngsContent = new GUIContent("Young's Modulus");
        private GUIContent m_poissonContent = new GUIContent("Poisson's Ratio");
        private GUIContent m_dynamicFrictionContent = new GUIContent("Dynamic Friction");
        private GUIContent m_dampingContent = new GUIContent("Damping");
        private GUIContent m_modelContent = new GUIContent("Material Model");
    }
}
