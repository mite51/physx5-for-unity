using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxEllipsoidGeometry))]
    [CanEditMultipleObjects]
    public class PhysxEllipsoidGeometryEditor : PhysxConvexCoreGeometryEditor
    {
        protected override void OnEnable()
        {
            base.OnEnable();
            m_radii = serializedObject.FindProperty("m_radii");
        }

        protected override void DrawCoreGUI()
        {
            EditorGUILayout.PropertyField(m_radii, m_radiiContent);
        }

        private SerializedProperty m_radii;

        private GUIContent m_radiiContent = new GUIContent("Radii",
            "Radii along the local X, Y and Z axes.");
    }
}
