using System;
using UnityEngine;

namespace PhysX5ForUnity
{
    [CreateAssetMenu(fileName = "PhysXPBDMaterial", menuName = "PhysX 5/PBD Material", order = 4)]
    public class PhysxPBDMaterial : PhysxMaterial
    {
        public float Friction
        {
            get { return m_friction; }
            set
            {
                m_friction = value;
                if (m_nativeObjectPtr != IntPtr.Zero)
                {
                    Physx.PxPBDMaterialSetFriction(m_nativeObjectPtr, m_friction);
                }
            }
        }

        public float Damping
        {
            get { return m_damping; }
            set
            {
                m_damping = value;
                if (m_nativeObjectPtr != IntPtr.Zero)
                {
                    Physx.PxPBDMaterialSetDamping(m_nativeObjectPtr, m_damping);
                }
            }
        }

        public float Adhesion
        {
            get { return m_adhesion; }
            set
            {
                m_adhesion = value;
                if (m_nativeObjectPtr != IntPtr.Zero)
                {
                    Physx.PxPBDMaterialSetAdhesion(m_nativeObjectPtr, m_adhesion);
                }
            }
        }

        public float Viscosity
        {
            get { return m_viscosity; }
            set
            {
                m_viscosity = value;
                if (m_nativeObjectPtr != IntPtr.Zero)
                {
                    Physx.PxPBDMaterialSetViscosity(m_nativeObjectPtr, m_viscosity);
                }
            }
        }

        public float VorticityConfinement
        {
            get { return m_vorticityConfinement; }
            set
            {
                m_vorticityConfinement = value;
                if (m_nativeObjectPtr != IntPtr.Zero)
                {
                    Physx.PxPBDMaterialSetVorticityConfinement(m_nativeObjectPtr, m_vorticityConfinement);
                }
            }
        }

        public float SurfaceTension
        {
            get { return m_surfaceTension; }
            set
            {
                m_surfaceTension = value;
                if (m_nativeObjectPtr != IntPtr.Zero)
                {
                    Physx.PxPBDMaterialSetSurfaceTension(m_nativeObjectPtr, m_surfaceTension);
                }
            }
        }

        public float Cohesion
        {
            get { return m_cohesion; }
            set
            {
                m_cohesion = value;
                if (m_nativeObjectPtr != IntPtr.Zero)
                {
                    Physx.PxPBDMaterialSetCohesion(m_nativeObjectPtr, m_cohesion);
                }
            }
        }

        public float Lift
        {
            get { return m_lift; }
            set
            {
                m_lift = value;
                if (m_nativeObjectPtr != IntPtr.Zero)
                {
                    Physx.PxPBDMaterialSetLift(m_nativeObjectPtr, m_lift);
                }
            }
        }

        public float Drag
        {
            get { return m_drag; }
            set
            {
                m_drag = value;
                if (m_nativeObjectPtr != IntPtr.Zero)
                {
                    Physx.PxPBDMaterialSetDrag(m_nativeObjectPtr, m_drag);
                }
            }
        }

        public float CflCoefficient
        {
            get { return m_cflCoefficient; }
            set
            {
                m_cflCoefficient = value;
                if (m_nativeObjectPtr != IntPtr.Zero)
                {
                    Physx.PxPBDMaterialSetCflCoefficient(m_nativeObjectPtr, m_cflCoefficient);
                }
            }
        }

        public float GravityScale
        {
            get { return m_gravityScale; }
            set
            {
                m_gravityScale = value;
                if (m_nativeObjectPtr != IntPtr.Zero)
                {
                    Physx.PxPBDMaterialSetGravityScale(m_nativeObjectPtr, m_gravityScale);
                }
            }
        }


        protected override void CreateMaterial()
        {
            if (m_nativeObjectPtr == IntPtr.Zero)
            {
                m_nativeObjectPtr = Physx.CreatePxPBDMaterial(m_friction, m_damping, m_adhesion, m_viscosity, m_vorticityConfinement,
                    m_surfaceTension, m_cohesion, m_lift, m_drag, m_cflCoefficient, m_gravityScale);
            }
        }

        [SerializeField] private float m_friction;
        [SerializeField] private float m_damping;
        [SerializeField] private float m_adhesion;
        [SerializeField] private float m_viscosity;
        [SerializeField] private float m_vorticityConfinement;
        [SerializeField] private float m_surfaceTension;
        [SerializeField] private float m_cohesion;
        [SerializeField] private float m_lift = 0.0f;
        [SerializeField] private float m_drag = 0.0f;
        [SerializeField] private float m_cflCoefficient = 1.0f;
        [SerializeField] private float m_gravityScale = 1.0f;
    }
}
