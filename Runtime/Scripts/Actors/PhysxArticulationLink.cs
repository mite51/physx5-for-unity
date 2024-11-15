using UnityEngine;

namespace PhysX5ForUnity
{
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    [AddComponentMenu("PhysX 5/Actors/PhysX Articulation Link")]
    public class PhysxArticulationLink : PhysxArticulationLinkBase
    {
        public PhysxArticulationLink ParentLink
        {
            get { return m_parentLink; }
        }

        public PxArticulationAxis ArticulationAxis
        {
            get { return m_articulationAxis; }
        }

        public bool IsDriveJoint
        {
            get { return m_isDriveJoint; }
        }

        [SerializeField]
        private PhysxArticulationLink m_parentLink;
        [SerializeField]
        private PxArticulationAxis m_articulationAxis;
        [SerializeField]
        private bool m_isDriveJoint = false;
    }
}