using UnityEngine;

namespace PhysX5ForUnity
{
    [DefaultExecutionOrder(-10)]
    [AddComponentMenu("PhysX 5/Simulation/PhysX Result Fetcher")]
    public class PhysxResultFetcher : MonoBehaviour
    {
        private void FixedUpdate()
        {
            if (Application.isPlaying)
            {
                Physx.StepPhysicsFetchResults();
            }
        }
    }
}
