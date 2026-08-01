using UnityEditor;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxVehicleController))]
    public class PhysxVehicleControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            // Controller inputs are live values, so this editor stays enabled during
            // play mode (unlike the setup-time PhysxEditorBase editors).
            DrawDefaultInspector();
        }
    }

    [CustomEditor(typeof(PhysxVehicleWheelController))]
    public class PhysxVehicleWheelControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
        }
    }
}
