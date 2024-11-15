using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxBoxGeometry))]
    [CanEditMultipleObjects]
    public class PhysxBoxGeometryEditor : PhysxEditorBase
    {
        private void OnEnable()
        {
            m_size = serializedObject.FindProperty("m_size");
        }

        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_size, m_sizeContent);

            serializedObject.ApplyModifiedProperties();
        }

        private SerializedProperty m_size;

        private GUIContent m_sizeContent = new GUIContent("Size");
    }
}
