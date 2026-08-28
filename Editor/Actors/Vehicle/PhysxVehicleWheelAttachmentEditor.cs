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
            DrawWheelCollisionHelp();
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// Explains the consequences of making a wheel collide, in the inspector where the
        /// decision is made rather than only in the console once the scene runs.
        /// </summary>
        private void DrawWheelCollisionHelp()
        {
            PhysxVehicleWheelAttachment attach = target as PhysxVehicleWheelAttachment;
            if (attach == null || !attach.isSimulationShape) return;

            if (attach.collisionGroup == PhysxCollisionGroups.DefaultGroup)
            {
                EditorGUILayout.HelpBox(
                    "This wheel simulates but sits in the default collision group, so it will " +
                    "collide with the road on top of the suspension raycast and tire model that " +
                    "already handle the road. The vehicle will ride high and steer badly. Give " +
                    "wheels their own group and disable that group against the road's group with " +
                    "PhysxCollisionGroups.SetPairCollides during world setup.",
                    MessageType.Warning);
            }

            if (attach.geometryMode == PxwVehicleWheelGeometryMode.CookedPrism)
            {
                EditorGUILayout.HelpBox(
                    "The cooked prism's facets turn with the wheel, so contacts chatter as it " +
                    "spins. Switch Geometry Mode to Cylinder for a shape that is the same from " +
                    "every angle.",
                    MessageType.Info);
            }
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
