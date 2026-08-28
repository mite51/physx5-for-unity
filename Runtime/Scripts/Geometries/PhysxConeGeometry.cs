using UnityEngine;

namespace PhysX5ForUnity
{
    /// <summary>
    /// A cone, axis along local +X with the base at local x = -height/2 and the apex at
    /// local x = +height/2.
    /// </summary>
    [AddComponentMenu("PhysX 5/Geometries/PhysX Cone Geometry")]
    public class PhysxConeGeometry : PhysxConvexCoreGeometry
    {
        public float Height
        {
            get { return m_height; }
            set { m_height = value; } // This setter exists for dynamic runtime construction. Setting it after the object is registered with an active simulation will have no effect.
        }

        public float Radius
        {
            get { return m_radius; }
            set { m_radius = value; } // This setter exists for dynamic runtime construction. Setting it after the object is registered with an active simulation will have no effect.
        }

        public override PxConvexCoreType CoreType { get { return PxConvexCoreType.Cone; } }

        protected override float[] GetCoreParams()
        {
            return new float[] { m_height, m_radius };
        }

        private void OnDrawGizmosSelected()
        {
            if (!enabled || m_radius <= 0.0f || m_height <= 0.0f) return;

            Gizmos.color = Color.yellow;

            // The drawn shape approximates the core plus the margin. The margin rounds the base
            // rim and the apex, so this outline is a close guide rather than exact at the tip.
            float radius = m_radius + Margin;
            float halfHeight = m_height * 0.5f + Margin;

            Vector3 axis = WorldAxis;
            GetWorldRadialAxes(out Vector3 axisA, out Vector3 axisB);
            Vector3 baseCenter = transform.position - axis * halfHeight;
            Vector3 apex = transform.position + axis * halfHeight;

            DrawWireCircle(baseCenter, axisA, axisB, radius);

            for (int i = 0; i < 4; ++i)
            {
                float angle = i * 0.5f * Mathf.PI;
                Vector3 offset = (axisA * Mathf.Cos(angle) + axisB * Mathf.Sin(angle)) * radius;
                Gizmos.DrawLine(baseCenter + offset, apex);
            }
        }

        [SerializeField]
        [Tooltip("Length along the local X axis, before the margin is added.")]
        private float m_height = 1.0f;
        [SerializeField]
        [Tooltip("Base radius, before the margin is added.")]
        private float m_radius = 0.5f;
    }
}
