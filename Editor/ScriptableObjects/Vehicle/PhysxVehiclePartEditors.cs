using UnityEditor;

namespace PhysX5ForUnity
{
    // Shared IMGUI base for the reusable vehicle part assets. Draws every serialized
    // field (curves included) and disables editing during play mode, matching the
    // rest of the package's editors.
    public abstract class PhysxVehiclePartEditor : PhysxEditorBase
    {
        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(PhysxVehicleWheel))]
    [CanEditMultipleObjects]
    public class PhysxVehicleWheelEditor : PhysxVehiclePartEditor { }

    [CustomEditor(typeof(PhysxVehicleSuspension))]
    [CanEditMultipleObjects]
    public class PhysxVehicleSuspensionEditor : PhysxVehiclePartEditor { }

    [CustomEditor(typeof(PhysxVehicleTire))]
    [CanEditMultipleObjects]
    public class PhysxVehicleTireEditor : PhysxVehiclePartEditor { }

    [CustomEditor(typeof(PhysxVehicleEngine))]
    [CanEditMultipleObjects]
    public class PhysxVehicleEngineEditor : PhysxVehiclePartEditor { }

    [CustomEditor(typeof(PhysxVehicleGearbox))]
    [CanEditMultipleObjects]
    public class PhysxVehicleGearboxEditor : PhysxVehiclePartEditor { }

    [CustomEditor(typeof(PhysxVehicleAutobox))]
    [CanEditMultipleObjects]
    public class PhysxVehicleAutoboxEditor : PhysxVehiclePartEditor { }

    [CustomEditor(typeof(PhysxVehicleClutch))]
    [CanEditMultipleObjects]
    public class PhysxVehicleClutchEditor : PhysxVehiclePartEditor { }

    [CustomEditor(typeof(PhysxVehicleTireFrictionTable))]
    [CanEditMultipleObjects]
    public class PhysxVehicleTireFrictionTableEditor : PhysxVehiclePartEditor { }
}
