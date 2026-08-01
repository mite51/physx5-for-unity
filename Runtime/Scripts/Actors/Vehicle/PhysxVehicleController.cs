using UnityEngine;

namespace PhysX5ForUnity
{
    /// <summary>
    /// Pushes throttle / brake / steer / transmission commands into a
    /// <see cref="PhysxVehicle"/>, mirroring Omniverse's PhysxVehicleControllerAPI.
    /// Attach alongside a PhysxVehicle. Ignored when the vehicle is in direct
    /// per-wheel control mode.
    /// </summary>
    [AddComponentMenu("PhysX 5/Vehicle/PhysX Vehicle Controller")]
    [RequireComponent(typeof(PhysxVehicle))]
    [DefaultExecutionOrder(41)]
    public class PhysxVehicleController : MonoBehaviour
    {
        [Header("Inputs")]
        [Range(0.0f, 1.0f)] public float throttle = 0.0f;
        [Range(0.0f, 1.0f)] public float brake = 0.0f;
        [Range(0.0f, 1.0f)] public float handbrake = 0.0f;
        [Range(-1.0f, 1.0f)] public float steer = 0.0f;

        [Header("Transmission (engine drive)")]
        [Tooltip("Let the autobox pick gears automatically.")]
        public bool automaticGear = true;

        [Tooltip("Target gear when not automatic (0 = reverse .. neutral .. forward, matching the gearbox ratios).")]
        public int targetGear = 2;

        [Range(0.0f, 1.0f)] public float clutch = 0.0f;

        [Header("Tank thrusts (tank differential)")]
        [Range(-1.0f, 1.0f)] public float leftThrust = 0.0f;
        [Range(-1.0f, 1.0f)] public float rightThrust = 0.0f;

        [Header("Convenience input")]
        [Tooltip("Drive throttle/brake/steer from the default Input axes (Vertical/Horizontal) and Space for handbrake.")]
        public bool readKeyboardInput = false;

        // eAUTOMATIC_GEAR = PX_MAX_U32; passing -1 casts to that on the native side.
        private const int AutomaticGear = -1;

        private PhysxVehicle m_vehicle;

        private void Awake()
        {
            m_vehicle = GetComponent<PhysxVehicle>();
        }

        private void Update()
        {
            if (!readKeyboardInput) return;
            float v = Input.GetAxis("Vertical");
            throttle = Mathf.Clamp01(v);
            brake = Mathf.Clamp01(-v);
            steer = Input.GetAxis("Horizontal");
            handbrake = Input.GetKey(KeyCode.Space) ? 1.0f : 0.0f;
        }

        private void FixedUpdate()
        {
            if (m_vehicle == null || !m_vehicle.IsFinalized || m_vehicle.useDirectWheelControl)
                return;

            m_vehicle.SetCommands(brake, handbrake, throttle, steer);

            if (m_vehicle.driveType == PhysxVehicle.VehicleDriveType.Engine)
            {
                m_vehicle.SetTransmissionCommand(automaticGear ? AutomaticGear : targetGear, clutch);
                if (m_vehicle.differentialType == PxVehicleDifferentialType.eTANK)
                    m_vehicle.SetTankThrusts(leftThrust, rightThrust);
            }
        }
    }
}
