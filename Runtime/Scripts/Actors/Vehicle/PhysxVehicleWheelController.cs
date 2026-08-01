using UnityEngine;

namespace PhysX5ForUnity
{
    /// <summary>
    /// Optional per-wheel raw controller, mirroring Omniverse's
    /// PhysxVehicleWheelControllerAPI. Attach to the same GameObject as a
    /// <see cref="PhysxVehicleWheelAttachment"/>. Its mere presence flips the owning
    /// vehicle into direct wheel-control mode (bypassing the differential, steering
    /// and brake command path) so drive torque / brake torque / steer angle are
    /// applied to this wheel directly.
    /// </summary>
    [AddComponentMenu("PhysX 5/Vehicle/PhysX Vehicle Wheel Controller")]
    [RequireComponent(typeof(PhysxVehicleWheelAttachment))]
    [DefaultExecutionOrder(41)]
    public class PhysxVehicleWheelController : MonoBehaviour
    {
        [Tooltip("Drive torque applied to this wheel (Nm).")]
        public float driveTorque = 0.0f;

        [Tooltip("Brake torque applied to this wheel (Nm).")]
        public float brakeTorque = 0.0f;

        [Tooltip("Steer angle applied to this wheel (degrees).")]
        public float steerAngle = 0.0f;

        private PhysxVehicleWheelAttachment m_attachment;
        private PhysxVehicle m_vehicle;

        private void Awake()
        {
            m_attachment = GetComponent<PhysxVehicleWheelAttachment>();
            m_vehicle = GetComponentInParent<PhysxVehicle>();
        }

        private void FixedUpdate()
        {
            if (m_vehicle == null || !m_vehicle.IsFinalized || m_attachment == null || m_attachment.WheelId < 0)
                return;

            m_vehicle.SetWheelControl(m_attachment.WheelId, driveTorque, brakeTorque, steerAngle * Mathf.Deg2Rad);
        }
    }
}
