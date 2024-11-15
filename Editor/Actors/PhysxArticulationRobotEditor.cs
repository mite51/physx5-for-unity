using UnityEditor;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxArticulationRobot))]
    [CanEditMultipleObjects]
    public class PhysxArticulationRobotEditor : PhysxArticulationKinematicTreeEditor
    {
        protected override void OnEnable()
        {
            base.OnEnable();
            m_basePose = serializedObject.FindProperty("m_basePose");
            m_eeLinks = serializedObject.FindProperty("m_eeLinks");
        }

        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_scene, m_sceneLabelContent);
            EditorGUILayout.PropertyField(m_fixBase);
            EditorGUILayout.PropertyField(m_disableSelfCollision);
            EditorGUILayout.PropertyField(m_basePose);
            EditorGUILayout.PropertyField(m_density);
            EditorGUILayout.PropertyField(m_links);
            EditorGUILayout.PropertyField(m_eeLinks);
            
            serializedObject.ApplyModifiedProperties();
        }

        protected SerializedProperty m_basePose;
        protected SerializedProperty m_eeLinks;
    }
}
