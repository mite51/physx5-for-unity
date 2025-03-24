using UnityEngine;

namespace PhysX5ForUnity
{

    [DefaultExecutionOrder(1000)]
    [AddComponentMenu("PhysX 5/Simulation/PhysX Simulation Starter")]
    public class PhysxSimulationStarter : MonoBehaviour
    {
        private void FixedUpdate()
        {
            if (Application.isPlaying)
            {
                string errors = Physx.GetPhysxErrorString();
                if (!string.IsNullOrEmpty(errors))
                {
                    Debug.LogError($"PhysX Errors: {errors}");
                }
                Physx.StepPhysicsStart(Time.fixedDeltaTime);
            }
        }
    }

}

