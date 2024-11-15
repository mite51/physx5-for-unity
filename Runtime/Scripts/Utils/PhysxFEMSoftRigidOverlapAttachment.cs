using UnityEngine;

namespace PhysX5ForUnity
{
    [DefaultExecutionOrder(40)]
    [AddComponentMenu("PhysX 5/Attachments/FEM Soft Rigid Overlap Attachment")]
    public class PhysxFEMSoftRigidOverlapAttachment : MonoBehaviour
    {
        private void OnEnable()
        {
            Physx.AttachFEMSoftBodyOverlappingAreaToRigidBody(m_actor1.NativeObjectPtr, m_actor2.NativeObjectPtr, m_actor2.Shape.Geometry.NativeObjectPtr);
        }

        [SerializeField]
        private PhysxFEMSoftBodyActor m_actor1;
        [SerializeField]
        private PhysxRigidActor m_actor2;
    }
}