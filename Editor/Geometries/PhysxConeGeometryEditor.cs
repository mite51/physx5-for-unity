using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxConeGeometry))]
    [CanEditMultipleObjects]
    public class PhysxConeGeometryEditor : PhysxConvexCoreGeometryEditor
    {
        protected override void OnEnable()
        {
            base.OnEnable();
            m_height = serializedObject.FindProperty("m_height");
            m_radius = serializedObject.FindProperty("m_radius");
        }

        protected override void DrawCoreGUI()
        {
            EditorGUILayout.PropertyField(m_height, m_heightContent);
            EditorGUILayout.PropertyField(m_radius, m_radiusContent);
        }

        private SerializedProperty m_height;
        private SerializedProperty m_radius;

        private GUIContent m_heightContent = new GUIContent("Height",
            "Length along the local X axis. The base sits at -height/2, the apex at +height/2.");
        private GUIContent m_radiusContent = new GUIContent("Radius", "Base radius.");
    }
}
