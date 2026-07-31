using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxPlaneGeometry))]
    [CanEditMultipleObjects]
    public class PhysxPlaneGeometryEditor : PhysxEditorBase
    {
        private void OnEnable()
        {
            m_gizmoSize = serializedObject.FindProperty("m_gizmoSize");
        }

        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_gizmoSize, m_gizmoSizeContent);

            serializedObject.ApplyModifiedProperties();
        }

        private SerializedProperty m_gizmoSize;

        private GUIContent m_gizmoSizeContent = new GUIContent("Gizmo Size");
    }
}
