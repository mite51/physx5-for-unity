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
            m_halfHeight = serializedObject.FindProperty("m_halfHeight");
        }

        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_radius, m_radiusContent);
            EditorGUILayout.PropertyField(m_halfHeight, m_halfHeightContent);

            serializedObject.ApplyModifiedProperties();
        }

        private SerializedProperty m_radius;
        private SerializedProperty m_halfHeight;

        private GUIContent m_radiusContent = new GUIContent("Radius");
        private GUIContent m_halfHeightContent = new GUIContent("Half Height");
    }
}
