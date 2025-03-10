using UnityEngine;

namespace PhysX5ForUnity
{
    [ExecuteInEditMode]
    public abstract class PhysxArticulationLinkBase : PhysxNativeGameObjectBase
    {
        public PhysxArticulationKinematicTree ArticulationKinematicTree
        {
            get { return m_physxArticulationKinematicTree; }
            set { m_physxArticulationKinematicTree = value; }
        }

        public struct PxArticulatrionJointLimit
        {
            public float lower;
            public float upper;
        }

        public Transform JointOnParent
        {
            get { return m_jointOnParent; }
        }

        public Transform JointOnSelf
        {
            get { return m_jointOnSelf; }
        }

        public PxRobotJointType JointType
        {
            get { return m_jointType; }
        }

        public float JointLimLower
        {
            get { return m_jointLimLower; }
        }

        public float JointLimUpper
        {
            get { return m_jointLimUpper; }
        }

        public float Stiffness
        {
            get { return m_stiffness; }
        }

        public float Damping
        {
            get { return m_damping; }
        }

        public float DriveMaxForce
        {
            get { return m_driveMaxForce; }
        }

        protected void Update()
        {
            if (!Application.isPlaying)
            {
                if (m_jointOnParent != null && m_jointOnSelf != null && (m_jointOnParent.hasChanged || m_jointOnSelf.hasChanged))
                {
                    AlignWithParent();
                }
            }
        }

        void AlignWithParent()
        {
            // Calculate the difference in position and rotation between the two joints
            Vector3 positionDifference = m_jointOnParent.transform.position - m_jointOnSelf.transform.position;
            Quaternion rotationDifference = m_jointOnParent.transform.rotation * Quaternion.Inverse(m_jointOnSelf.transform.rotation);

            // Apply the position difference
            transform.position += positionDifference;

            // Align rotation - this assumes the joints are oriented in the same way
            transform.rotation = rotationDifference * transform.rotation;

            // Adjust local position and rotation considering the scale of the parent
            if (transform.parent != null)
            {
                Vector3 scaleInv = transform.parent.localScale;
                scaleInv.x = 1 / scaleInv.x;
                scaleInv.y = 1 / scaleInv.y;
                scaleInv.z = 1 / scaleInv.z;
                transform.localPosition = Vector3.Scale(transform.localPosition, scaleInv);
            }
        }
        
        [SerializeField]
        private Transform m_jointOnParent;  // Joint pose on the parent link
        [SerializeField]
        private Transform m_jointOnSelf;    // Joint pose on this link
        [SerializeField]
        private PxRobotJointType m_jointType;
        [SerializeField]
        private float m_jointLimLower = -3.14f;
        [SerializeField]
        private float m_jointLimUpper = 3.14f;
        [SerializeField]
        private float m_stiffness = 100.0f;
        [SerializeField]
        private float m_damping = 1.0f;
        [SerializeField]
        private float m_driveMaxForce = float.MaxValue;
        private PhysxArticulationKinematicTree m_physxArticulationKinematicTree;
    }
}