using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxArticulationRobotLink))]
    [CanEditMultipleObjects]
    public class PhysxArticulationRobotLinkEditor : PhysxArticulationLinkEditorBase
    {
        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_jointOnParent);
            EditorGUILayout.PropertyField(m_jointOnSelf);
            EditorGUILayout.PropertyField(m_jointType);

            if (m_jointType.enumValueIndex!=0)
            {
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
                EditorGUILayout.PropertyField(m_driveGainP);
                EditorGUILayout.PropertyField(m_driveGainD);
                EditorGUILayout.PropertyField(m_driveMaxForce);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}