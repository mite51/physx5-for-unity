using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxPBDParticleSystemFluidRenderer))]
    [CanEditMultipleObjects]
    public class PhysxPBDParticleSystemFluidRendererEditor : PhysxEditorBase
    {
        protected virtual void OnEnable()
        {
            m_chunkAddShader = serializedObject.FindProperty("m_chunkAddShader");
            m_fluidShader = serializedObject.FindProperty("m_fluidShader");
            m_fluidMaterial = serializedObject.FindProperty("m_fluidMaterial");
            m_lerpBlend = serializedObject.FindProperty("m_lerpBlend");
            m_depthFilterType = serializedObject.FindProperty("m_depthFilterType");
            m_narrowFilter1D = serializedObject.FindProperty("m_narrowFilter1D");
            m_filterIterations = serializedObject.FindProperty("m_filterIterations");
            m_filterWorldSize = serializedObject.FindProperty("m_filterWorldSize");
            m_filterThresholdRatio = serializedObject.FindProperty("m_filterThresholdRatio");
            m_filterClampRatio = serializedObject.FindProperty("m_filterClampRatio");
            m_filterCleanUpFixedSize = serializedObject.FindProperty("m_filterCleanUpFixedSize");
        }

        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_fluidShader, m_fluidShaderContent);
            EditorGUILayout.PropertyField(m_chunkAddShader, m_chunkAddShaderContent);
            EditorGUILayout.PropertyField(m_fluidMaterial);

            GUI.enabled = true;
            EditorGUILayout.PropertyField(m_lerpBlend);

            sm_filterFoldout = EditorGUILayout.Foldout(sm_filterFoldout, "Depth filter", true, EditorStyles.foldout);
            if (sm_filterFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(m_depthFilterType);
                EditorGUILayout.PropertyField(m_filterWorldSize);
                EditorGUILayout.PropertyField(m_filterIterations);
                // Narrow range filter parameters
                if (m_depthFilterType.enumValueIndex == 1)
                {
                    EditorGUILayout.PropertyField(m_narrowFilter1D);
                    if (m_narrowFilter1D.boolValue)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(m_filterCleanUpFixedSize, m_filterCleanUpFixedSizeContent);
                        EditorGUI.indentLevel--;
                    }
                    EditorGUILayout.PropertyField(m_filterThresholdRatio);
                    EditorGUILayout.PropertyField(m_filterClampRatio);
                }
                EditorGUI.indentLevel--;
            }


            serializedObject.ApplyModifiedProperties();
        }

        protected SerializedProperty m_chunkAddShader;
        protected SerializedProperty m_fluidShader;
        protected SerializedProperty m_fluidMaterial;
        protected SerializedProperty m_lerpBlend;
        protected SerializedProperty m_depthFilterType;
        protected SerializedProperty m_narrowFilter1D;
        protected SerializedProperty m_filterIterations;
        protected SerializedProperty m_filterWorldSize;
        protected SerializedProperty m_filterThresholdRatio;
        protected SerializedProperty m_filterClampRatio;
        protected SerializedProperty m_filterCleanUpFixedSize;

        protected GUIContent m_chunkAddShaderContent = new GUIContent("Chunk Add Compute Shader");
        protected GUIContent m_fluidShaderContent = new GUIContent("Fluid Shader");
        protected GUIContent m_filterCleanUpFixedSizeContent = new GUIContent("Clean-Up Filter Size");

        protected static bool sm_filterFoldout = false;
    }
}
