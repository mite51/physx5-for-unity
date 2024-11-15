using System;
using UnityEngine;

namespace PhysX5ForUnity
{
    [AddComponentMenu("PhysX 5/Actors/PhysX Articulation Robot")]
    public class PhysxArticulationRobot : PhysxArticulationKinematicTree
    {
        public IntPtr[] EELinkPtrs
        {
            get { return m_pxEELinkPtrs; }
        }

        public override void ResetObject()
        {
            Physx.ResetArticulationRobot(m_nativeObjectPtr);
            Physx.GetRobotJointPositions(m_nativeObjectPtr, ref m_jointPositions[0], m_numDriveJoints);
            Physx.GetRobotLinkPoses(m_nativeObjectPtr, ref m_linkPoses[0], m_linkPoses.Length);
            for (int i = 1; i < m_linkPoses.Length; i++)
            {
                m_allLinks[i - 1].transform.position = m_linkPoses[i].position;
                m_allLinks[i - 1].transform.rotation = m_linkPoses[i].quaternion;
            }
        }

        public override void DriveJoints(float[] jointPositions)
        {
            Physx.DriveJoints(m_nativeObjectPtr, ref jointPositions[0]);
        }

        /// <summary>
        /// Inverse kinematics considering the last attached EE link.
        /// </summary>
        public bool InverseKinematics(float[] qInit, Transform targetTransform)
        {
            PxTransformData t = PxTransformData.FromTransform(targetTransform);
            return InverseKinematics(qInit, t);
        }
        
        /// <summary>
        /// Inverse kinematics considering the last attached EE link.
        /// </summary>
        public bool InverseKinematics(float[] qInit, PxTransformData targetTransform)
        {
            return Physx.GetRobotInverseKinematics(m_nativeObjectPtr, ref qInit[0], ref targetTransform, 1e-3f, 100, 0.01f);
        }

        /// <summary>
        /// Naive FK from the base to the EE's joint on self, 
        /// without considering the robot's scale and its own transformation.
        /// </summary>
        /// <returns></returns>
        public Matrix4x4 ForwardKinematics(float[] q)
        {
            Matrix4x4 t;
            Physx.GetRobotForwardKinematics(m_nativeObjectPtr, ref q[0], out t);
            return t;
        }

        protected override void CreateNativeObject()
        {
            PxTransformData basePose = PxTransformData.FromTransform(transform);
            m_nativeObjectPtr = Physx.CreateArticulationRobot(m_scene.NativeObjectPtr, ref basePose, m_density);
            if (m_links != null)
            {
                m_pxLinkPtrs = new IntPtr[m_links.Length];
                for (int i = 0; i < m_links.Length; i++)
                {
                    PhysxArticulationRobotLink link = (PhysxArticulationRobotLink)m_links[i];
                    link.ArticulationKinematicTree = this;
                    PxTransformData parentPose;
                    Vector3 parentPosition = Vector3.Scale(link.JointOnParent.localPosition, link.JointOnParent.lossyScale);
                    parentPose = new PxTransformData(parentPosition, link.JointOnParent.localRotation);
                    IntPtr linkShape = GetLinkShape(link);
                    PxTransformData linkPose = new PxTransformData(link.gameObject.transform.localPosition, link.gameObject.transform.localRotation);
                    PxTransformData jointChildPose = new PxTransformData(Vector3.Scale(link.JointOnSelf.localPosition, link.transform.lossyScale), link.JointOnSelf.localRotation);
                    m_pxLinkPtrs[i] = Physx.AddLinkToRobot(
                        m_nativeObjectPtr,
                        ref linkPose,
                        link.JointType,
                        ref parentPose,
                        ref jointChildPose,
                        linkShape,
                        link.JointLimLower,
                        link.JointLimUpper,
                        link.DriveGainP,
                        link.DriveGainD,
                        link.DriveMaxForce,
                        m_density
                    );
                    link.NativeObjectPtr = m_pxLinkPtrs[i];
                    if (link.JointType != PxRobotJointType.Fix)
                    {
                        m_numDriveJoints++;
                    }
                }
            }

            if (m_eeLinks != null)
            {
                m_pxEELinkPtrs = new IntPtr[m_eeLinks.Length];
                for (int i = 0; i < m_eeLinks.Length; i++)
                {
                    PhysxArticulationRobotLink link = m_eeLinks[i];
                    link.ArticulationKinematicTree = this;
                    PxTransformData parentPose;
                    Vector3 parentPosition = Vector3.Scale(link.JointOnParent.localPosition, link.JointOnParent.lossyScale);
                    parentPose = new PxTransformData(parentPosition, link.JointOnParent.localRotation);
                    IntPtr linkShape = GetLinkShape(link);
                    //if (linkGeometry != IntPtr.Zero)
                    {
                        PxTransformData linkPose = new PxTransformData(link.gameObject.transform.localPosition, link.gameObject.transform.localRotation);
                        PxTransformData jointChildPose = new PxTransformData(Vector3.Scale(link.JointOnSelf.localPosition, link.transform.lossyScale), link.JointOnSelf.localRotation);
                        m_pxEELinkPtrs[i] = Physx.AddEndEffectorLinkToRobot(
                            m_nativeObjectPtr,
                            ref linkPose,
                            link.JointType,
                            ref parentPose,
                            ref jointChildPose,
                            linkShape,
                            link.JointLimLower,
                            link.JointLimUpper,
                            link.DriveGainP,
                            link.DriveGainD,
                            link.DriveMaxForce,
                            m_density
                        );
                        link.NativeObjectPtr = m_pxEELinkPtrs[i];
                        if (link.JointType != PxRobotJointType.Fix)
                        {
                            m_numDriveJoints++;
                        }
                    }
                }
            }

            int linksLength = 0;
            int eeLinksLength = 0;
            if (m_links != null)
            {
                linksLength = m_links.Length;
            }
            if (m_eeLinks != null)
            {
                eeLinksLength = m_eeLinks.Length;
            }

            // Now create the new array with the total length
            m_allLinks = new PhysxArticulationRobotLink[linksLength + eeLinksLength];
            m_links?.CopyTo(m_allLinks, 0);
            m_eeLinks?.CopyTo(m_allLinks, m_links.Length);
            m_linkPoses = new PxTransformData[m_allLinks.Length + 1]; // including the base link
            m_jointPositions = new float[m_numDriveJoints];

            // Set joint limits
            m_jointLimits = new PhysxArticulationLinkBase.PxArticulatrionJointLimit[m_numDriveJoints];
            int activeJointsCount = 0;
            if (m_links != null)
            {
                for (int i = 0; i < m_links.Length; i++)
                {
                    PhysxArticulationRobotLink link = (PhysxArticulationRobotLink)m_links[i];
                    if (link.JointType != PxRobotJointType.Fix)
                    {
                        m_jointLimits[activeJointsCount].lower = link.JointLimLower;
                        m_jointLimits[activeJointsCount].upper = link.JointLimUpper;
                        activeJointsCount++;
                    }
                }
            }

            if (m_eeLinks != null)
            {
                for (int i = 0; i < m_eeLinks.Length; i++)
                {
                    PhysxArticulationRobotLink link = m_eeLinks[i];
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
                Physx.ReleaseArticulationRobot(m_nativeObjectPtr);
                m_nativeObjectPtr = IntPtr.Zero;
                m_pxLinkPtrs = null;
                m_pxEELinkPtrs = null;
                m_allLinks = null;
                m_linkPoses = null;
                m_numDriveJoints = 0;
                m_jointPositions = null;
                m_jointLimits = null;
            }
        }

        protected override void FixedUpdate()
        {
            Physx.GetRobotJointPositions(m_nativeObjectPtr, ref m_jointPositions[0], m_linkPoses.Length);
            Physx.GetRobotLinkPoses(m_nativeObjectPtr, ref m_linkPoses[0], m_linkPoses.Length);
            for (int i = 1; i < m_linkPoses.Length; i++)
            {
                m_allLinks[i - 1].transform.position = m_linkPoses[i].position;
                m_allLinks[i - 1].transform.rotation = m_linkPoses[i].quaternion;
            }
        }

        [SerializeField]
        protected Transform m_basePose;
        [SerializeField]
        protected PhysxArticulationRobotLink[] m_eeLinks; // end-effectors

        protected PhysxArticulationRobotLink[] m_allLinks;
        protected IntPtr[] m_pxEELinkPtrs = null;

    }
}
