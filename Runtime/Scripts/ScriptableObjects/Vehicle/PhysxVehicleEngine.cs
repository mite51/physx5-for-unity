using UnityEngine;

namespace PhysX5ForUnity
{
    /// <summary>
    /// Reusable engine parameters for engine-drive vehicles, mirroring Omniverse's
    /// PhysxVehicleEngineAPI. The torque curve is authored as a normalized
    /// <see cref="AnimationCurve"/> (x: normalized rev fraction 0..1, y: normalized
    /// torque 0..1) and sampled into the fixed table PhysX expects.
    /// </summary>
    [CreateAssetMenu(fileName = "PhysXVehicleEngine", menuName = "PhysX 5/Vehicle/Engine", order = 23)]
    public class PhysxVehicleEngine : ScriptableObject
    {
        public const int MaxTorquePoints = 8;

        [Tooltip("Engine moment of inertia (kg m^2).")]
        public float moi = 1.0f;

        [Tooltip("Peak torque produced by the engine (Nm).")]
        public float peakTorque = 500.0f;

        [Tooltip("Idle rotation speed (radians/s).")]
        public float idleOmega = 75.0f;

        [Tooltip("Maximum rotation speed (radians/s).")]
        public float maxOmega = 600.0f;

        [Tooltip("Damping rate applied at full throttle.")]
        public float dampingRateFullThrottle = 0.15f;

        [Tooltip("Damping rate at zero throttle with the clutch engaged.")]
        public float dampingRateZeroThrottleClutchEngaged = 2.0f;

        [Tooltip("Damping rate at zero throttle with the clutch disengaged.")]
        public float dampingRateZeroThrottleClutchDisengaged = 0.35f;

        [Tooltip("Number of points sampled from the torque curve (2..8).")]
        [Range(2, MaxTorquePoints)]
        public int torquePointCount = MaxTorquePoints;

        [Tooltip("Normalized torque as a function of normalized engine speed. X and Y are both in [0,1].")]
        public AnimationCurve normalizedTorqueCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.8f),
            new Keyframe(0.33f, 1.0f),
            new Keyframe(1.0f, 0.8f));

        public PxwVehicleEngineDesc ToDesc()
        {
            int count = Mathf.Clamp(torquePointCount, 2, MaxTorquePoints);
            float[] x = new float[MaxTorquePoints];
            float[] y = new float[MaxTorquePoints];
            for (int i = 0; i < count; ++i)
            {
                float t = (count > 1) ? (float)i / (count - 1) : 0.0f;
                x[i] = t;
                y[i] = Mathf.Clamp01(normalizedTorqueCurve.Evaluate(t));
            }

            return new PxwVehicleEngineDesc
            {
                torqueCurveX = x,
                torqueCurveY = y,
                nbTorquePoints = count,
                moi = moi,
                peakTorque = peakTorque,
                idleOmega = idleOmega,
                maxOmega = maxOmega,
                dampingRateFullThrottle = dampingRateFullThrottle,
                dampingRateZeroThrottleClutchEngaged = dampingRateZeroThrottleClutchEngaged,
                dampingRateZeroThrottleClutchDisengaged = dampingRateZeroThrottleClutchDisengaged
            };
        }
    }
}
