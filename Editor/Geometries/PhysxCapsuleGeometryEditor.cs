using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxCapsuleGeometry))]
    [CanEditMultipleObjects]
    public class PhysxCapsuleGeometryEditor : PhysxEditorBase
    {
        private void OnEnable()
        {
            m_radius = serializedObject.FindProperty("m_radius");
            m_height = serializedObject.FindProperty("m_height");
            m_direction = serializedObject.FindProperty("m_direction");
        }

        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_radius, m_radiusContent);
            EditorGUILayout.PropertyField(m_height, m_heightContent);
            EditorGUILayout.PropertyField(m_direction, m_directionContent);

            serializedObject.ApplyModifiedProperties();
        }

        private SerializedProperty m_radius;
        private SerializedProperty m_height;
        private SerializedProperty m_direction;

        private GUIContent m_radiusContent = new GUIContent("Radius");
        private GUIContent m_heightContent = new GUIContent("Height");
        private GUIContent m_directionContent = new GUIContent("Direction");
    }
}
