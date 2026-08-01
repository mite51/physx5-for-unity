using UnityEditor;
using UnityEngine;

namespace PhysX5ForUnity
{
    [CustomEditor(typeof(PhysxVehicleWheelAttachment))]
    public class PhysxVehicleWheelAttachmentEditor : PhysxEditorBase
    {
        protected override void DrawInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            PhysxVehicleWheelAttachment attach = (PhysxVehicleWheelAttachment)target;
            Transform t = attach.transform;

            // Reference frame: use the parent vehicle if present so travel direction is
            // interpreted consistently with authoring.
            PhysxVehicle vehicle = attach.GetComponentInParent<PhysxVehicle>();
            Transform frame = vehicle != null ? vehicle.transform : t;

            Vector3 dirLocal = attach.travelDirectionLocal.sqrMagnitude > 1e-6f
                ? attach.travelDirectionLocal.normalized
                : Vector3.down;
            Vector3 worldDir = frame.TransformDirection(dirLocal);

            Vector3 top = t.position;
            Vector3 bottom = top + worldDir * attach.SuspensionTravel;

            // Suspension travel.
            Handles.color = Color.cyan;
            Handles.DrawLine(top, bottom);
            Handles.SphereHandleCap(0, top, Quaternion.identity, HandleUtility.GetHandleSize(top) * 0.08f, EventType.Repaint);

            // Wheel radius at the bottom of travel, in the wheel plane (normal = lateral axis).
            Vector3 lateral = frame.right;
            Handles.color = new Color(1.0f, 0.7f, 0.1f, 1.0f);
            Handles.DrawWireDisc(bottom, lateral, attach.WheelRadius);
        }
    }
}
