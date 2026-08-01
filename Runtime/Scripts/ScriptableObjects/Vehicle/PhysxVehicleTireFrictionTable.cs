using System.Collections.Generic;
using UnityEngine;

namespace PhysX5ForUnity
{
    /// <summary>
    /// Reusable mapping from drivable-surface <see cref="PhysxMaterial"/> to a tire
    /// friction multiplier, mirroring Omniverse's PhysxVehicleTireFrictionTable.
    /// Materials not present in the table use <see cref="defaultFriction"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "PhysXVehicleTireFrictionTable", menuName = "PhysX 5/Vehicle/Tire Friction Table", order = 27)]
    public class PhysxVehicleTireFrictionTable : ScriptableObject
    {
        [System.Serializable]
        public struct FrictionEntry
        {
            public PhysxMaterial material;
            public float friction;
        }

        [Tooltip("Friction used for any surface material not listed below.")]
        public float defaultFriction = 1.0f;

        [Tooltip("Per-material friction multipliers.")]
        public List<FrictionEntry> entries = new List<FrictionEntry>();
    }
}
