using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxTriangleMeshGeometry))]
    [CanEditMultipleObjects]
    public class PhysxTriangleMeshGeometryEditor : PhysxEditorBase
    {
        private void OnEnable()
        {
            m_mesh = serializedObject.FindProperty("m_mesh");
            m_isConvex = serializedObject.FindProperty("m_isConvex");
            m_buildGpuData = serializedObject.FindProperty("m_buildGpuData");
            m_sdfSpacing = serializedObject.FindProperty("m_sdfSpacing");
            m_sdfSubgridSize = serializedObject.FindProperty("m_sdfSubgridSize");
            m_bitsPerSdfSubgridPixel = serializedObject.FindProperty("m_bitsPerSdfSubgridPixel");
            m_drawWireFrame = serializedObject.FindProperty("m_drawWireFrame");
        }

        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_mesh, m_meshContent);
            EditorGUILayout.PropertyField(m_isConvex, m_isConvexContent);
            EditorGUILayout.PropertyField(m_buildGpuData, m_buildGpuDataContent);
            EditorGUILayout.PropertyField(m_sdfSpacing, m_sdfSpacingContent);
            EditorGUILayout.PropertyField(m_sdfSubgridSize, m_sdfSubgridSizeContent);
            EditorGUILayout.PropertyField(m_bitsPerSdfSubgridPixel, m_bitsPerSdfSubgridPixelContent);

            GUI.enabled = true;
            EditorGUILayout.PropertyField(m_drawWireFrame, m_drawWireFrameContent);

            serializedObject.ApplyModifiedProperties();
        }

        private SerializedProperty m_mesh;
        private SerializedProperty m_isConvex;
        private SerializedProperty m_buildGpuData;
        private SerializedProperty m_sdfSpacing;
        private SerializedProperty m_sdfSubgridSize;
        private SerializedProperty m_bitsPerSdfSubgridPixel;
        private SerializedProperty m_drawWireFrame;

        private GUIContent m_meshContent = new GUIContent("Mesh");
        private GUIContent m_isConvexContent = new GUIContent("Convex");
        private GUIContent m_buildGpuDataContent = new GUIContent("Build GPU Data");
        private GUIContent m_sdfSpacingContent = new GUIContent("SDF Spacing");
        private GUIContent m_sdfSubgridSizeContent = new GUIContent("SDF Subgrid Size");
        private GUIContent m_bitsPerSdfSubgridPixelContent = new GUIContent("Bits Per SDF Subgrid Pixel");
        private GUIContent m_drawWireFrameContent = new GUIContent("Draw Wireframe");
    }
}
