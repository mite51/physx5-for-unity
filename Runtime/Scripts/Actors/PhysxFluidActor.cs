using System;
using UnityEngine;

namespace PhysX5ForUnity
{
    public abstract class PhysxFluidActor : PhysxParticleActor
    {
        public float Density
        {
            get { return m_density; }
        }

        public float Buoyancy
        {
            get { return m_buoyancy; }
        }

        public Color FluidColor
        {
            get { return m_fluidColor; }
            set
            {
                m_fluidColor = value;
                SetColors(m_particleData.IndexOffset, NumParticles, value);
            }
        }

        public delegate void SetColorEventHandeler(int indexOffset, int numParticles, Color color);
        public event SetColorEventHandeler SetColors;

        public virtual void ResetObject()
        {
            if (ParticleData.NativeParticleObjectPtr != IntPtr.Zero)
            {
                Physx.ResetParticleSystemObject(ParticleData.NativeParticleObjectPtr);
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            SetColors = null;
        }

        [SerializeField]
        protected Vector4[] m_initialParticlePositions;
        [SerializeField]
        protected float m_density = 1000.0f;
        [SerializeField]
        protected float m_buoyancy = 0.9f;
        [SerializeField]
        protected Color m_fluidColor = new Color(0, 0, 0.5f, 1);
    }
}
