using UnityEditor;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxArticulationKinematicTree))]
    [CanEditMultipleObjects]
    public class PhysxArticulationKinematicTreeEditor : PhysxActorEditorBase
    {
        protected override void OnEnable()
        {
            base.OnEnable();
            m_fixBase = serializedObject.FindProperty("m_fixBase");
            m_disableSelfCollision = serializedObject.FindProperty("m_disableSelfCollision");
            m_links = serializedObject.FindProperty("m_links");
            m_density = serializedObject.FindProperty("m_density");
        }

        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_scene, m_sceneLabelContent);
            EditorGUILayout.PropertyField(m_fixBase);
            EditorGUILayout.PropertyField(m_disableSelfCollision);
            EditorGUILayout.PropertyField(m_density);
            EditorGUILayout.PropertyField(m_links);

            serializedObject.ApplyModifiedProperties();
        }

        protected SerializedProperty m_fixBase;
        protected SerializedProperty m_disableSelfCollision;
        protected SerializedProperty m_links;
        protected SerializedProperty m_density;
    }
}
