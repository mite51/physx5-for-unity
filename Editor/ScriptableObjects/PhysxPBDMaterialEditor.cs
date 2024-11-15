using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxPBDMaterial))]
    [CanEditMultipleObjects]
    public class PhysxPBDMaterialEditor : PhysxEditorBase
    {
        private void OnEnable()
        {
            m_friction = serializedObject.FindProperty("m_friction");
            m_damping = serializedObject.FindProperty("m_damping");
            m_adhesion = serializedObject.FindProperty("m_adhesion");
            m_viscosity = serializedObject.FindProperty("m_viscosity");
            m_vorticityConfinement = serializedObject.FindProperty("m_vorticityConfinement");
            m_surfaceTension = serializedObject.FindProperty("m_surfaceTension");
            m_cohesion = serializedObject.FindProperty("m_cohesion");
            m_lift = serializedObject.FindProperty("m_lift");
            m_drag = serializedObject.FindProperty("m_drag");
            m_cflCoefficient = serializedObject.FindProperty("m_cflCoefficient");
            m_gravityScale = serializedObject.FindProperty("m_gravityScale");
        }

        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_friction, m_frictionContent);
            EditorGUILayout.PropertyField(m_damping, m_dampingContent);
            EditorGUILayout.PropertyField(m_adhesion, m_adhesionContent);
            EditorGUILayout.PropertyField(m_viscosity, m_viscosityContent);
            EditorGUILayout.PropertyField(m_vorticityConfinement, m_vorticityConfinementContent);
            EditorGUILayout.PropertyField(m_surfaceTension, m_surfaceTensionContent);
            EditorGUILayout.PropertyField(m_cohesion, m_cohesionContent);
            EditorGUILayout.PropertyField(m_lift, m_liftContent);
            EditorGUILayout.PropertyField(m_drag, m_dragContent);
            EditorGUILayout.PropertyField(m_cflCoefficient, m_cflCoefficientContent);
            EditorGUILayout.PropertyField(m_gravityScale, m_gravityScaleContent);

            serializedObject.ApplyModifiedProperties();
        }

        private SerializedProperty m_friction;
        private SerializedProperty m_damping;
        private SerializedProperty m_adhesion;
        private SerializedProperty m_viscosity;
        private SerializedProperty m_vorticityConfinement;
        private SerializedProperty m_surfaceTension;
        private SerializedProperty m_cohesion;
        private SerializedProperty m_lift;
        private SerializedProperty m_drag;
        private SerializedProperty m_cflCoefficient;
        private SerializedProperty m_gravityScale;

        private GUIContent m_frictionContent = new GUIContent("Friction");
        private GUIContent m_dampingContent = new GUIContent("Damping");
        private GUIContent m_adhesionContent = new GUIContent("Adhesion");
        private GUIContent m_viscosityContent = new GUIContent("Viscosity");
        private GUIContent m_vorticityConfinementContent = new GUIContent("Vorticity Confinement");
        private GUIContent m_surfaceTensionContent = new GUIContent("Surface Tension");
        private GUIContent m_cohesionContent = new GUIContent("Cohesion");
        private GUIContent m_liftContent = new GUIContent("Lift");
        private GUIContent m_dragContent = new GUIContent("Drag");
        private GUIContent m_cflCoefficientContent = new GUIContent("CFL Coefficient");
        private GUIContent m_gravityScaleContent = new GUIContent("Gravity Scale");

    }
}
