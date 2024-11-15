using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    public abstract class PhysxRigidActorEditorBase : PhysxActorEditorBase
    {
        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(m_scene, m_sceneLabelContent);

            if (GUI.changed) serializedObject.ApplyModifiedProperties();
        }
    }
}