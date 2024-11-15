using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxShape))]
    [CanEditMultipleObjects]
    public class PhysxShapeEditor : PhysxEditorBase
    {
        private void OnEnable()
        {
            m_material = serializedObject.FindProperty("m_material");
            isExclusive = serializedObject.FindProperty("isExclusive");
        }

        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_material, m_materialContent);
            EditorGUILayout.PropertyField(isExclusive, m_isExclusiveContent);

            serializedObject.ApplyModifiedProperties();
        }

        private SerializedProperty m_material;
        private SerializedProperty isExclusive;
        private GUIContent m_materialContent = new GUIContent("Material");
        private GUIContent m_isExclusiveContent = new GUIContent("Exclusive Shape");
    }
}
