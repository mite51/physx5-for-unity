using UnityEngine;

namespace PhysX5ForUnity
{
    [CreateAssetMenu(fileName = "PhysXFEMSoftBodyMaterial", menuName = "PhysX 5/FEM Soft Body Material", order = 4)]
    public class PhysxFEMSoftBodyMaterial : PhysxMaterial
    {
        protected override void CreateMaterial()
        {
            m_nativeObjectPtr = Physx.CreatePxFEMSoftBodyMaterial(m_youngs, m_poisson, m_dynamicFriction, m_damping, m_model);
        }

        [SerializeField] protected float m_youngs = 1000;
        [SerializeField] protected float m_poisson = 0.4f;
        [SerializeField] protected float m_dynamicFriction = 0.5f;
        [SerializeField] protected float m_damping = 0.005f;
        [SerializeField] protected PxFEMSoftBodyMaterialModel m_model = PxFEMSoftBodyMaterialModel.CoRotational;
    }
}
