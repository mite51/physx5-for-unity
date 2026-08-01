using UnityEngine;

namespace PhysX5ForUnity
{
    /// <summary>
    /// Reusable automatic gearbox parameters for engine-drive vehicles, mirroring
    /// Omniverse's PhysxVehicleAutoGearBoxAPI. Up/down shift thresholds are
    /// expressed as a fraction (0..1) of the engine's max rotation speed per gear.
    /// </summary>
    [CreateAssetMenu(fileName = "PhysXVehicleAutobox", menuName = "PhysX 5/Vehicle/Autobox", order = 25)]
    public class PhysxVehicleAutobox : ScriptableObject
    {
        public const int MaxRatios = 32;

        [Tooltip("Per-gear normalized engine speed above which the autobox shifts up.")]
        [Range(0.0f, 1.0f)]
        public float upRatio = 0.65f;

        [Tooltip("Per-gear normalized engine speed below which the autobox shifts down.")]
        [Range(0.0f, 1.0f)]
        public float downRatio = 0.5f;

        [Tooltip("Minimum time in seconds between automatic gear changes.")]
        public float latency = 2.0f;

        public PxwVehicleAutoboxDesc ToDesc()
        {
            float[] up = new float[MaxRatios];
            float[] down = new float[MaxRatios];
            for (int i = 0; i < MaxRatios; ++i)
            {
                up[i] = upRatio;
                down[i] = downRatio;
            }

            return new PxwVehicleAutoboxDesc
            {
                upRatios = up,
                downRatios = down,
                latency = latency
            };
        }
    }
}
