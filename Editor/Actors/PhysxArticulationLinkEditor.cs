using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxArticulationLink))]
    [CanEditMultipleObjects]
    public class PhysxArticulationLinkEditor : PhysxArticulationLinkEditorBase
    {
        protected override void OnEnable()
        {
            base.OnEnable();
            m_parentLink = serializedObject.FindProperty("m_parentLink");
            m_articulationAxis = serializedObject.FindProperty("m_articulationAxis");
            m_isDriveJoint = serializedObject.FindProperty("m_isDriveJoint");
        }

        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_parentLink);
            EditorGUILayout.PropertyField(m_jointOnParent);
            EditorGUILayout.PropertyField(m_jointOnSelf);
            EditorGUILayout.PropertyField(m_jointType);

            if (m_jointType.enumValueIndex!=0)
            {
                EditorGUILayout.PropertyField(m_articulationAxis);

                GUILayout.BeginVertical("HelpBox");

                EditorGUILayout.LabelField("Joint Limits", EditorStyles.boldLabel);
                float jointLimMin = m_jointLimLower.floatValue;
                float jointLimMax = m_jointLimUpper.floatValue;
                EditorGUILayout.MinMaxSlider(ref jointLimMin, ref jointLimMax, -2 * Mathf.PI, 2 * Mathf.PI);
                m_jointLimLower.floatValue = jointLimMin;
                m_jointLimUpper.floatValue = jointLimMax;
                EditorGUILayout.PropertyField(m_jointLimLower, m_jointLimLowerLabelContent);
                EditorGUILayout.PropertyField(m_jointLimUpper, m_jointLimUpperLabelContent);

                GUILayout.EndVertical();
                EditorGUILayout.PropertyField(m_isDriveJoint);
                if (m_isDriveJoint.boolValue)
                {
                    EditorGUILayout.PropertyField(m_driveGainP);
                    EditorGUILayout.PropertyField(m_driveGainD);
                    EditorGUILayout.PropertyField(m_driveMaxForce);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        protected SerializedProperty m_parentLink;
        protected SerializedProperty m_articulationAxis;
        protected SerializedProperty m_isDriveJoint;
    }
}