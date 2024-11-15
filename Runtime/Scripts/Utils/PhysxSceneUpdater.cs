using System;
using UnityEngine;

namespace PhysX5ForUnity
{

    [DefaultExecutionOrder(-1)]
    [Obsolete("This is not as efficient. Use PhysxSimulationStarter and PhysxResultFetcher instead.")]
    [AddComponentMenu("PhysX 5/Simulation/PhysX Scene Updater")]
    public class PhysxSceneUpdater : MonoBehaviour
    {
        private void FixedUpdate()
        {
            if (Application.isPlaying)
            {
                Physx.StepPhysics(Time.fixedDeltaTime);
            }
        }
    }

}

