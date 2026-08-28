using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    /// <summary>
    /// Shared inspector for the convex core geometries: draws the core's own fields, then the
    /// margin, then warns about the transform scale PhysX cannot honour for these shapes.
    /// </summary>
    public abstract class PhysxConvexCoreGeometryEditor : PhysxEditorBase
    {
        protected virtual void OnEnable()
        {
            m_margin = serializedObject.FindProperty("m_margin");
        }

        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();

            DrawCoreGUI();
            EditorGUILayout.PropertyField(m_margin, m_marginContent);
            DrawScaleWarning();

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>Draws the fields specific to this core.</summary>
        protected abstract void DrawCoreGUI();

        /// <summary>
        /// Convex core geometry has no scale term, unlike the mesh geometries which bake
        /// lossyScale into the cooked shape. A scaled transform therefore renders at one size
        /// and collides at another, which is worth saying out loud.
        /// </summary>
        private void DrawScaleWarning()
        {
            Component component = target as Component;
            if (component == null) return;

            Vector3 scale = component.transform.lossyScale;
            if (Mathf.Approximately(scale.x, 1.0f) && Mathf.Approximately(scale.y, 1.0f) && Mathf.Approximately(scale.z, 1.0f))
            {
                return;
            }

            EditorGUILayout.HelpBox(
                $"Transform scale is {scale.x:0.###}, {scale.y:0.###}, {scale.z:0.###}. PhysX has no scale " +
                "term for convex core geometry, so the collision shape uses the dimensions above " +
                "unscaled. Author the dimensions directly instead of scaling the transform.",
                MessageType.Warning);
        }

        protected SerializedProperty m_margin;

        private GUIContent m_marginContent = new GUIContent("Margin",
            "Distance the core is expanded by. Rounds the shape's edges and can steady contact, " +
            "but it grows the shape.");
    }
}
