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
            m_useAuthoredDirection = serializedObject.FindProperty("m_useAuthoredDirection");
        }

        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                EditorGUILayout.PropertyField(m_radius, m_radiusContent);
                EditorGUILayout.PropertyField(m_height, m_heightContent);
                EditorGUILayout.PropertyField(m_useAuthoredDirection);
                using (new EditorGUI.DisabledScope(!m_useAuthoredDirection.boolValue))
                    EditorGUILayout.PropertyField(m_direction, m_directionContent);
            }
            EditorGUILayout.HelpBox("Height is cylinder length; total length is Height + 2 × Radius. Transform scale does not change dimensions. Rebuild the owning actor and shape after editing.", MessageType.Info);
            if (!m_useAuthoredDirection.boolValue)
                EditorGUILayout.HelpBox("Legacy geometry uses local X. To migrate, remove any compensating Transform rotation, then enable Use Authored Direction. Review the resulting physical pose before saving.", MessageType.Warning);

            serializedObject.ApplyModifiedProperties();
        }

        private SerializedProperty m_radius;
        private SerializedProperty m_height;
        private SerializedProperty m_direction;
        private SerializedProperty m_useAuthoredDirection;

        private GUIContent m_radiusContent = new GUIContent("Radius");
        private GUIContent m_heightContent = new GUIContent("Cylinder Height");
        private GUIContent m_directionContent = new GUIContent("Direction");
    }
}
