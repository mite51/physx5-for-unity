using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxSphereGeometry))]
    [CanEditMultipleObjects]
    public class PhysxSphereGeometryEditor : PhysxEditorBase
    {
        private void OnEnable()
        {
            m_radius = serializedObject.FindProperty("m_radius");
        }

        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_radius, m_radiusContent);

            serializedObject.ApplyModifiedProperties();
        }

        private SerializedProperty m_radius;
        private GUIContent m_radiusContent = new GUIContent("Radius");
    }
}
