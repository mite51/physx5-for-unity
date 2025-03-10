using System;
using UnityEngine;

namespace PhysX5ForUnity
{
    [AddComponentMenu("PhysX 5/Actors/PhysX Articulation Kinematic Tree")]
    public class PhysxArticulationKinematicTree : PhysxRigidActor
    {
        public int NumJoints
        {
            get { return m_numDriveJoints; }
        }

        public PhysxArticulationLinkBase.PxArticulatrionJointLimit[] JointLimits
        {
            get { return m_jointLimits; }
        }

        public float[] JointPositions
        {
            get { return m_jointPositions; }
        }

        public IntPtr[] LinkPtrs
        {
            get { return m_pxLinkPtrs; }
        }

        public virtual void ResetObject()
        {
            Physx.ResetArticulationKinematicTree(m_nativeObjectPtr);
            Physx.GetArticulationKinematicTreeJointPositions(m_nativeObjectPtr, ref m_jointPositions[0], m_linkPoses.Length);
            Physx.GetArticulationKinematicTreeLinkPoses(m_nativeObjectPtr, ref m_linkPoses[0], m_linkPoses.Length);
            for (int i = 1; i < m_linkPoses.Length; i++)
            {
                m_links[i - 1].transform.position = m_linkPoses[i].position;
                m_links[i - 1].transform.rotation = m_linkPoses[i].quaternion;
            }
        }

        public virtual void DriveJoints(float[] jointPositions)
        {
            Physx.DriveArticulationKinematicTreeJoints(m_nativeObjectPtr, ref jointPositions[0]);
        }

        protected override void CreateNativeObject()
        {
            m_nativeObjectPtr = Physx.CreateArticulationKinematicTree(m_scene.NativeObjectPtr, m_fixBase, m_disableSelfCollision);
            m_pxLinkPtrs = new IntPtr[m_links.Length + 1];
            // add the first link as the base link
            PxTransformData basePose = PxTransformData.FromTransform(m_links[0].transform);
            m_pxLinkPtrs[0] = Physx.CreateArticulationKinematicTreeBase(m_nativeObjectPtr, ref basePose, GetLinkShape(m_links[0]), 1.0f);
            if (m_links != null)
            {
                for (int i = 1; i < m_links.Length; i++)
                {
                    PhysxArticulationLink link = (PhysxArticulationLink)m_links[i];
                    link.ArticulationKinematicTree = this;
                    int parentIndex = Array.IndexOf(m_links, link.ParentLink);
                    if (parentIndex < 0 || parentIndex >= i)
                    {
                        Debug.Log("Parent link not found.");
                        return;
                    }
                    
                    Vector3 parentPosition = Vector3.Scale(link.JointOnParent.localPosition, link.JointOnParent.lossyScale);
                    IntPtr linkShape = GetLinkShape(link);
                    PxTransformData parentPose = new PxTransformData(parentPosition, link.JointOnParent.localRotation);
                    PxTransformData linkPose = new PxTransformData(link.gameObject.transform.localPosition, link.gameObject.transform.localRotation);
                    PxTransformData jointChildPose = new PxTransformData(Vector3.Scale(link.JointOnSelf.localPosition, link.transform.lossyScale), link.JointOnSelf.localRotation);
                    m_pxLinkPtrs[i] = Physx.AddLinkToArticulationKinematicTree(
                        m_nativeObjectPtr,
                        m_pxLinkPtrs[parentIndex],
                        ref linkPose,
                        link.JointType,
                        ref parentPose,
                        ref jointChildPose,
                        link.ArticulationAxis,
                        linkShape,
                        link.JointLimLower,
                        link.JointLimUpper,
                        link.IsDriveJoint,
                        link.Stiffness,
                        link.Damping,
                        link.DriveMaxForce,
                        m_density
                    );
                    if (link.JointType != PxRobotJointType.Fix)
                    {
                        m_numDriveJoints++;
                    }
                }
            }

            m_linkPoses = new PxTransformData[m_links.Length]; // including the base link
            m_jointPositions = new float[m_links.Length - 1];

            // Set joint limits
            m_jointLimits = new PhysxArticulationLinkBase.PxArticulatrionJointLimit[m_numDriveJoints];
            if (m_links != null)
            {
                int activeJointsCount = 0;
                for (int i = 0; i < m_links.Length; i++)
                {
                    PhysxArticulationLink link = (PhysxArticulationLink)m_links[i];
                    if (link.JointType != PxRobotJointType.Fix)
                    {
                        m_jointLimits[activeJointsCount].lower = link.JointLimLower;
                        m_jointLimits[activeJointsCount].upper = link.JointLimUpper;
                        activeJointsCount++;
                    }
                }
            }
        }

        protected override void DestroyNativeObject()
        {
            if (m_nativeObjectPtr != IntPtr.Zero)
            {
                Physx.ReleaseArticulationKinematicTree(m_nativeObjectPtr);
                m_nativeObjectPtr = IntPtr.Zero;
                m_pxLinkPtrs = null;
                // m_links = null; // TODO: it seems that not setting this to null causes occasional crashing during GC
                m_linkPoses = null;
                m_numDriveJoints = 0;
                m_jointPositions = null;
                m_jointLimits = null;
            }
        }

        protected virtual void FixedUpdate()
        {
            Physx.GetArticulationKinematicTreeJointPositions(m_nativeObjectPtr, ref m_jointPositions[0], m_linkPoses.Length);
            Physx.GetArticulationKinematicTreeLinkPoses(m_nativeObjectPtr, ref m_linkPoses[0], m_linkPoses.Length);
            for (int i = 0; i < m_linkPoses.Length; i++)
            {
                m_links[i].transform.position = m_linkPoses[i].position;
                m_links[i].transform.rotation = m_linkPoses[i].quaternion;
            }
        }

        protected IntPtr GetLinkShape(PhysxArticulationLinkBase link)
        {
            PhysxShape shape = link.gameObject.GetComponent<PhysxShape>();
            if (shape == null || (shape.Geometry is PhysxTriangleMeshGeometry && ((PhysxTriangleMeshGeometry)shape.Geometry).IsConvex == false)) { return IntPtr.Zero; }
            return shape.NativeObjectPtr;
        }

        protected override void EnableActor()
        {
            if (m_nativeObjectPtr == IntPtr.Zero) CreateActor();
            Physx.AddArticulationToScene(m_nativeObjectPtr);
        }
        protected override void DisableActor()
        {
            if (m_nativeObjectPtr != IntPtr.Zero) Physx.RemoveArticulationFromScene(m_nativeObjectPtr);
        }

        [SerializeField]
        protected bool m_fixBase = true;
        [SerializeField]
        protected bool m_disableSelfCollision = false;
        [SerializeField]
        protected PhysxArticulationLinkBase[] m_links;
        [SerializeField]
        protected float m_density = 1f;

        protected PxTransformData[] m_linkPoses;
        protected IntPtr[] m_pxLinkPtrs = null;

        protected int m_numDriveJoints = 0;
        protected float[] m_jointPositions;
        protected PhysxArticulationLinkBase.PxArticulatrionJointLimit[] m_jointLimits;
    }
}
