using UnityEngine;

namespace PhysX5ForUnity
{
    /// <summary>
    /// Reusable clutch parameters for engine-drive vehicles, mirroring Omniverse's
    /// PhysxVehicleClutchAPI / clutch accuracy settings.
    /// </summary>
    [CreateAssetMenu(fileName = "PhysXVehicleClutch", menuName = "PhysX 5/Vehicle/Clutch", order = 26)]
    public class PhysxVehicleClutch : ScriptableObject
    {
        public enum ClutchAccuracyMode
        {
            Estimate = 0,
            BestPossible = 1
        }

        [Tooltip("Solver accuracy used to compute clutch coupling between engine and wheels.")]
        public ClutchAccuracyMode accuracyMode = ClutchAccuracyMode.BestPossible;

        [Tooltip("Iteration count used when accuracy mode is Estimate.")]
        public int estimateIterations = 5;

        [Tooltip("Clutch strength: maximum coupling torque per unit clutch command (Nm).")]
        public float strength = 10.0f;

        public PxwVehicleClutchDesc ToDesc()
        {
            return new PxwVehicleClutchDesc
            {
                accuracyMode = (int)accuracyMode,
                estimateIterations = Mathf.Max(1, estimateIterations),
                strength = strength
            };
        }
    }
}
