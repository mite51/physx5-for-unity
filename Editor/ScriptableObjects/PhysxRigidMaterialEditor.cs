using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxRigidMaterial))]
    [CanEditMultipleObjects]
    public class PhysxRigidMaterialEditor : PhysxEditorBase
    {
        private void OnEnable()
        {
            m_staticFriction = serializedObject.FindProperty("m_staticFriction");
            m_dynamicFriction = serializedObject.FindProperty("m_dynamicFriction");
            m_restitution = serializedObject.FindProperty("m_restitution");
        }

        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_staticFriction, m_staticFrictionContent);
            EditorGUILayout.PropertyField(m_dynamicFriction, m_dynamicFrictionContent);
            EditorGUILayout.PropertyField(m_restitution, m_restitutionContent);

            serializedObject.ApplyModifiedProperties();
        }
        
        private SerializedProperty m_staticFriction;
        private SerializedProperty m_dynamicFriction;
        private SerializedProperty m_restitution;

        private GUIContent m_staticFrictionContent = new GUIContent("Static Friction");
        private GUIContent m_dynamicFrictionContent = new GUIContent("Dynamic Friction");
        private GUIContent m_restitutionContent = new GUIContent("Restitution");
    }
}
