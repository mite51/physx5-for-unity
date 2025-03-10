using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    public abstract class PhysxArticulationLinkEditorBase : PhysxEditorBase
    {
        protected virtual void OnEnable()
        {
            m_jointOnParent = serializedObject.FindProperty("m_jointOnParent");
            m_jointOnSelf = serializedObject.FindProperty("m_jointOnSelf");
            m_jointType = serializedObject.FindProperty("m_jointType");
            m_jointLimLower = serializedObject.FindProperty("m_jointLimLower");
            m_jointLimUpper = serializedObject.FindProperty("m_jointLimUpper");
            m_stiffness = serializedObject.FindProperty("m_stiffness");
            m_damping = serializedObject.FindProperty("m_damping");
            m_driveMaxForce = serializedObject.FindProperty("m_driveMaxForce");
        }

        protected SerializedProperty m_jointOnParent;
        protected SerializedProperty m_jointOnSelf;
        protected SerializedProperty m_jointType;
        protected SerializedProperty m_jointLimLower;
        protected SerializedProperty m_jointLimUpper;
        protected SerializedProperty m_stiffness;
        protected SerializedProperty m_damping;
        protected SerializedProperty m_driveMaxForce;

        protected GUIContent m_jointLimLowerLabelContent = new GUIContent("Lower Limit");
        protected GUIContent m_jointLimUpperLabelContent = new GUIContent("Upper Limit");
    }
}
