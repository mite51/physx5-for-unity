using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxPBDParticleSystemFluidDiffuseRenderer))]
    [CanEditMultipleObjects]
    public class PhysxPBDParticleSystemFluidDiffuseRendererEditor : PhysxPBDParticleSystemFluidRendererEditor
    {
        protected override void OnEnable()
        {
            base.OnEnable();
            m_diffuseColorGridRange = serializedObject.FindProperty("m_diffuseColorGridRange");
            m_diffuseColorCellSize = serializedObject.FindProperty("m_diffuseColorCellSize");
            m_diffuseColorMaxCellParticles = serializedObject.FindProperty("m_diffuseColorMaxCellParticles");
            m_colorDiffusionShader = serializedObject.FindProperty("m_colorDiffusionShader");
        }

        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(m_fluidShader, m_fluidShaderContent);
            EditorGUILayout.PropertyField(m_chunkAddShader, m_chunkAddShaderContent);
            EditorGUILayout.PropertyField(m_colorDiffusionShader, m_colorDiffusionShaderContent);
            EditorGUILayout.PropertyField(m_fluidMaterial);

            GUI.enabled = true;

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

            EditorGUILayout.PropertyField(m_lerpBlend);

            GUI.enabled = m_currentGUIEnabled;
            EditorGUILayout.PropertyField(m_diffuseColorGridRange, m_diffuseColorGridRangeContent);
            EditorGUILayout.PropertyField(m_diffuseColorCellSize, m_diffuseColorCellSizeContent);
            EditorGUILayout.PropertyField(m_diffuseColorMaxCellParticles, m_diffuseColorMaxCellParticlesContent);

            serializedObject.ApplyModifiedProperties();
        }

        private SerializedProperty m_diffuseColorGridRange;
        private SerializedProperty m_diffuseColorCellSize;
        private SerializedProperty m_diffuseColorMaxCellParticles;
        private SerializedProperty m_colorDiffusionShader;

        private GUIContent m_diffuseColorGridRangeContent = new GUIContent("Diffusion Grid Range");
        private GUIContent m_diffuseColorCellSizeContent = new GUIContent("Diffusion Cell Size");
        private GUIContent m_diffuseColorMaxCellParticlesContent = new GUIContent("Max Cell Particles");
        private GUIContent m_colorDiffusionShaderContent = new GUIContent("Color Diffusion Compute Shader");
    }
}
