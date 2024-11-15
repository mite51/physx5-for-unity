using UnityEngine;

namespace PhysX5ForUnity
{
    [CreateAssetMenu(fileName = "PhysXRigidMaterial", menuName = "PhysX 5/Rigid Material", order = 3)]
    public class PhysxRigidMaterial : PhysxMaterial
    {
        protected override void CreateMaterial()
        {
            m_nativeObjectPtr = Physx.CreatePxMaterial(m_staticFriction, m_dynamicFriction, m_restitution);
        }

        [SerializeField]
        private float m_staticFriction = 0.0f;
        [SerializeField]
        private float m_dynamicFriction = 0.0f;
        [SerializeField]
        private float m_restitution = 0.0f;
    }
}