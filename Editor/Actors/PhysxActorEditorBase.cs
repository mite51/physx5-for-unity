using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    public abstract class PhysxActorEditorBase : PhysxEditorBase
    {
        protected virtual void OnEnable()
        {
            m_scene = serializedObject.FindProperty("m_scene");
        }

        protected SerializedProperty m_scene;
        
        protected GUIContent m_sceneLabelContent = new GUIContent("PhysX Scene");
    }
}