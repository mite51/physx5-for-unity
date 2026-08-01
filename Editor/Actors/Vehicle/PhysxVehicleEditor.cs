using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxVehicle))]
    public class PhysxVehicleEditor : PhysxActorEditorBase
    {
        protected override void OnEnable()
        {
            base.OnEnable();
        }

        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_scene, m_sceneLabelContent);
            DrawPropertiesExcluding(serializedObject, "m_Script", "m_scene");

            serializedObject.ApplyModifiedProperties();

            DrawValidation((PhysxVehicle)target);
        }

        private void DrawValidation(PhysxVehicle vehicle)
        {
            List<PhysxVehicleWheelAttachment> wheels = new List<PhysxVehicleWheelAttachment>();
            vehicle.GetComponentsInChildren(true, wheels);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);

            if (wheels.Count == 0)
            {
                EditorGUILayout.HelpBox("No PhysxVehicleWheelAttachment components found in children. " +
                    "Add one child GameObject per wheel.", MessageType.Error);
                return;
            }

            EditorGUILayout.HelpBox($"{wheels.Count} wheel attachment(s) detected.", MessageType.Info);

            int driven = 0, steering = 0, handbrake = 0;
            foreach (PhysxVehicleWheelAttachment w in wheels)
            {
                if (w.isDriven) driven++;
                if (w.isSteering) steering++;
                if (w.isHandbrake) handbrake++;
                if (w.wheel == null || w.tire == null || w.suspension == null)
                {
                    EditorGUILayout.HelpBox($"Wheel '{w.name}' is missing a wheel, tire or suspension asset; " +
                        "built-in defaults will be used.", MessageType.Warning);
                }
            }

            if (driven == 0)
                EditorGUILayout.HelpBox("No wheel is marked as driven; drive torque will be spread over all wheels.", MessageType.Warning);

            if (vehicle.driveType == PhysxVehicle.VehicleDriveType.Engine)
            {
                if (vehicle.engine == null)
                    EditorGUILayout.HelpBox("Engine drive selected but no Engine asset assigned; a default engine will be used.", MessageType.Warning);

                if (vehicle.differentialType == PxVehicleDifferentialType.eFOURWHEEL && driven < 4)
                    EditorGUILayout.HelpBox("Four-wheel differential requires at least 4 driven wheels.", MessageType.Error);

                if (vehicle.differentialType == PxVehicleDifferentialType.eTANK && wheels.Count < 2)
                    EditorGUILayout.HelpBox("Tank differential requires wheels on both sides of the vehicle.", MessageType.Error);
            }

            if (vehicle.ackermannEnabled && steering < 2)
                EditorGUILayout.HelpBox("Ackermann steering requires at least 2 steering wheels.", MessageType.Warning);

            bool hasWheelController = vehicle.GetComponentInChildren<PhysxVehicleWheelController>(true) != null;
            if (hasWheelController && !vehicle.useDirectWheelControl)
                EditorGUILayout.HelpBox("A PhysxVehicleWheelController is present: direct per-wheel control will be enabled " +
                    "automatically and the PhysxVehicleController path will be bypassed.", MessageType.Info);

            if (vehicle.useDirectWheelControl && vehicle.GetComponent<PhysxVehicleController>() != null)
                EditorGUILayout.HelpBox("Direct wheel control is enabled; the attached PhysxVehicleController will be ignored.", MessageType.Info);
        }
    }
}
