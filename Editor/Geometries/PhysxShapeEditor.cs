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
            m_collisionGroup = serializedObject.FindProperty("m_collisionGroup");
        }

        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_material, m_materialContent);
            EditorGUILayout.PropertyField(isExclusive, m_isExclusiveContent);
            EditorGUILayout.IntSlider(m_collisionGroup, 0, PhysxCollisionGroups.MaxGroups - 1, m_collisionGroupContent);

            serializedObject.ApplyModifiedProperties();
        }

        private SerializedProperty m_material;
        private SerializedProperty isExclusive;
        private SerializedProperty m_collisionGroup;
        private GUIContent m_materialContent = new GUIContent("Material");
        private GUIContent m_isExclusiveContent = new GUIContent("Exclusive Shape");
        private GUIContent m_collisionGroupContent = new GUIContent("Collision Group",
            "Group index this shape reports to the filter shader. All pairs collide by default; " +
            "use PhysxCollisionGroups.SetPairCollides to disable a pair.");
    }
}
