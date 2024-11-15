using UnityEngine;

namespace PhysX5ForUnity
{
    [DefaultExecutionOrder(40)]
    [AddComponentMenu("PhysX 5/Attachments/FEM Soft Body Overlap Attachment")]
    public class PhysxFEMSoftBodyOverlapAttachment : MonoBehaviour
    {
        private void OnEnable()
        {
            Physx.AttachFEMSoftBodyOverlappingAreaToSoftBody(actor1.NativeObjectPtr, actor2.NativeObjectPtr);
            Physx.AttachFEMSoftBodyOverlappingAreaToSoftBody(actor2.NativeObjectPtr, actor1.NativeObjectPtr);
        }

        [SerializeField]
        private PhysxFEMSoftBodyActor actor1;
        [SerializeField]
        private PhysxFEMSoftBodyActor actor2;
    }
}
