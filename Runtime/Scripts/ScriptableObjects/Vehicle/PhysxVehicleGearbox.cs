using UnityEngine;

namespace PhysX5ForUnity
{
    /// <summary>
    /// Reusable gearbox parameters for engine-drive vehicles, mirroring Omniverse's
    /// PhysxVehicleMultiWheelDifferentialAPI companion gearbox settings.
    /// The ratios list is authored including reverse and neutral; the neutral gear
    /// index selects which entry is neutral (ratio 0).
    /// </summary>
    [CreateAssetMenu(fileName = "PhysXVehicleGearbox", menuName = "PhysX 5/Vehicle/Gearbox", order = 24)]
    public class PhysxVehicleGearbox : ScriptableObject
    {
        public const int MaxRatios = 32;

        [Tooltip("Gear ratios ordered from reverse through neutral to the forward gears.")]
        public float[] ratios = new float[] { -4.0f, 0.0f, 4.0f, 2.0f, 1.5f, 1.1f, 1.0f };

        [Tooltip("Index into 'ratios' that represents neutral (ratio 0).")]
        public int neutralGear = 1;

        [Tooltip("Final drive ratio multiplying every gear ratio.")]
        public float finalRatio = 4.0f;

        [Tooltip("Time in seconds required to change gear.")]
        public float switchTime = 0.5f;

        public PxwVehicleGearboxDesc ToDesc()
        {
            int count = Mathf.Min(ratios != null ? ratios.Length : 0, MaxRatios);
            float[] r = new float[MaxRatios];
            for (int i = 0; i < count; ++i) r[i] = ratios[i];

            return new PxwVehicleGearboxDesc
            {
                neutralGear = Mathf.Clamp(neutralGear, 0, Mathf.Max(0, count - 1)),
                ratios = r,
                nbRatios = count,
                finalRatio = finalRatio,
                switchTime = switchTime
            };
        }
    }
}
