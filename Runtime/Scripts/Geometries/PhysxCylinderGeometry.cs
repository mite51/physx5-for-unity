using UnityEngine;

namespace PhysX5ForUnity
{
    /// <summary>
    /// A true cylinder, axis along local +X.
    /// </summary>
    /// <remarks>
    /// Unlike a cooked convex hull approximating a cylinder, this shape has no facets, so a
    /// rolling or spinning cylinder produces stable contact instead of chattering as contact
    /// points jump between flat faces. That makes it the right choice for wheels.
    /// </remarks>
    [AddComponentMenu("PhysX 5/Geometries/PhysX Cylinder Geometry")]
    public class PhysxCylinderGeometry : PhysxConvexCoreGeometry
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

        public override PxConvexCoreType CoreType { get { return PxConvexCoreType.Cylinder; } }

        protected override float[] GetCoreParams()
        {
            return new float[] { m_height, m_radius };
        }

        private void OnDrawGizmosSelected()
        {
            if (!enabled || m_radius <= 0.0f || m_height <= 0.0f) return;

            Gizmos.color = Color.yellow;

            // The drawn shape is the core plus the margin, which is what actually collides.
            float radius = m_radius + Margin;
            float halfHeight = m_height * 0.5f + Margin;

            Vector3 axis = WorldAxis;
            GetWorldRadialAxes(out Vector3 axisA, out Vector3 axisB);
            Vector3 capA = transform.position + axis * halfHeight;
            Vector3 capB = transform.position - axis * halfHeight;

            DrawWireCircle(capA, axisA, axisB, radius);
            DrawWireCircle(capB, axisA, axisB, radius);

            for (int i = 0; i < 4; ++i)
            {
                float angle = i * 0.5f * Mathf.PI;
                Vector3 offset = (axisA * Mathf.Cos(angle) + axisB * Mathf.Sin(angle)) * radius;
                Gizmos.DrawLine(capA + offset, capB + offset);
            }
        }

        [SerializeField]
        [Tooltip("Length along the local X axis, before the margin is added.")]
        private float m_height = 1.0f;
        [SerializeField]
        [Tooltip("Radius, before the margin is added.")]
        private float m_radius = 0.5f;
    }
}
