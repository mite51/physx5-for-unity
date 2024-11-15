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
                Physx.StepPhysicsStart(Time.fixedDeltaTime);
            }
        }
    }

}

