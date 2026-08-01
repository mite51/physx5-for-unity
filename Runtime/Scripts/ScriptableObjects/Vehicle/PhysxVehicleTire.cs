using UnityEngine;

namespace PhysX5ForUnity
{
    /// <summary>
    /// Reusable tire parameters, mirroring Omniverse's PhysxVehicleTireAPI.
    /// The friction-vs-slip graph is authored as an <see cref="AnimationCurve"/>
    /// and sampled into the fixed three-point table PhysX expects.
    /// </summary>
    [CreateAssetMenu(fileName = "PhysXVehicleTire", menuName = "PhysX 5/Vehicle/Tire", order = 22)]
    public class PhysxVehicleTire : ScriptableObject
    {
        [Tooltip("Lateral stiffness graph: normalized load at which the tire reaches its saturated lateral stiffness.")]
        public float lateralStiffnessX = 0.01f;

        [Tooltip("Lateral stiffness graph: maximum lateral stiffness per unit of lateral slip, scaled by rest load.")]
        public float lateralStiffnessY = 18.0f;

        [Tooltip("Longitudinal stiffness per unit of longitudinal slip (N).")]
        public float longitudinalStiffness = 5000.0f;

        [Tooltip("Camber stiffness (N per radian of camber).")]
        public float camberStiffness = 0.0f;

        [Tooltip("Tire load (N) at which the tire behaves nominally. If <= 0 it is derived from the sprung mass at Finalize time.")]
        public float restLoad = 0.0f;

        [Tooltip("Friction multiplier as a function of longitudinal slip. Sampled into three points (low/peak/high slip).")]
        public AnimationCurve frictionVsSlip = new AnimationCurve(
            new Keyframe(0.0f, 1.0f),
            new Keyframe(0.1f, 1.0f),
            new Keyframe(1.0f, 1.0f));

        [Tooltip("Maximum slip value used when sampling the friction-vs-slip curve.")]
        public float maxSlipForFriction = 1.0f;

        [Tooltip("Normalized load filter: below minNormalizedLoad the tire load contribution is zeroed.")]
        public float minNormalizedLoad = 0.0f;

        [Tooltip("Normalized load filter: above maxNormalizedLoad the tire load is clamped to maxFilteredNormalizedLoad.")]
        public float maxNormalizedLoad = 3.0f;

        [Tooltip("Maximum filtered normalized load.")]
        public float maxFilteredNormalizedLoad = 3.0f;

        public PxwVehicleTireDesc ToDesc()
        {
            float maxSlip = Mathf.Max(1e-4f, maxSlipForFriction);
            float slip0 = 0.0f;
            float slip1 = 0.5f * maxSlip;
            float slip2 = maxSlip;

            // frictionVsSlip[3][2] flattened row-major: {slip, friction} per point.
            float[] fvs = new float[6];
            fvs[0] = slip0;
            fvs[1] = Mathf.Max(0.0f, frictionVsSlip.Evaluate(slip0));
            fvs[2] = slip1;
            fvs[3] = Mathf.Max(0.0f, frictionVsSlip.Evaluate(slip1));
            fvs[4] = slip2;
            fvs[5] = Mathf.Max(0.0f, frictionVsSlip.Evaluate(slip2));

            // loadFilter[2][2] flattened row-major: {normalizedLoad, filteredLoad} per point.
            float[] lf = new float[4];
            lf[0] = minNormalizedLoad;
            lf[1] = 0.0f;
            lf[2] = maxNormalizedLoad;
            lf[3] = maxFilteredNormalizedLoad;

            return new PxwVehicleTireDesc
            {
                latStiffX = lateralStiffnessX,
                latStiffY = lateralStiffnessY,
                longStiff = longitudinalStiffness,
                camberStiff = camberStiffness,
                frictionVsSlip = fvs,
                restLoad = restLoad,
                loadFilter = lf
            };
        }
    }
}
