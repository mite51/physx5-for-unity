using System;
using UnityEngine;

namespace PhysX5ForUnity
{
    [AddComponentMenu("PhysX 5/Geometries/PhysX Capsule Geometry")]
    public class PhysxCapsuleGeometry : PhysxGeometry
    {
        public float Radius
        {
            get { return m_radius; }
        }

        public float HalfHeight
        {
            get { return m_halfHeight; }
        }

        protected override void CreateGeometry()
        {
            float[] shapeParams = new float[3];
            shapeParams[0] = m_radius;
            shapeParams[1] = m_halfHeight;
            m_nativeObjectPtr = PhysxUtils.CreatePxGeometry(PxGeometryType.Capsule, 2, ref shapeParams[0], IntPtr.Zero);
        }
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;

            Vector3 basePos = transform.position;
            float halfHeight = m_halfHeight;
            Vector3 rightSphere = basePos + transform.right * halfHeight;
            Vector3 leftSphere = basePos - transform.right * halfHeight;

            Gizmos.DrawWireSphere(rightSphere, m_radius);
            Gizmos.DrawWireSphere(leftSphere, m_radius);

            DrawWireCylinder(basePos, transform.right, m_radius, halfHeight * 2);
        }

        void DrawWireCylinder(Vector3 position, Vector3 direction, float radius, float height)
        {
            Vector3 right = direction.normalized * (height / 2);
            Vector3 forward = Vector3.Slerp(right, -right, 0.5f);
            Vector3 up = Vector3.Cross(right, forward).normalized * radius;

            float step = 12; // Degree step for drawing circles
            for (int i = 0; i < 360; i += (int)step)
            {
                Quaternion rotation = Quaternion.AngleAxis(i, direction);
                Vector3 circlePoint = rotation * up;

                // Draw the front and back circles of the cylinder
                Vector3 nextPoint = Quaternion.AngleAxis(i + step, direction) * up;
                Gizmos.DrawLine(position + right + circlePoint, position + right + nextPoint);
                Gizmos.DrawLine(position - right + circlePoint, position - right + nextPoint);

                // Draw vertical lines to connect the front and back circles
                if (i % 36 == 0) // Reducing clutter
                {
                    Gizmos.DrawLine(position + right + circlePoint, position - right + circlePoint);
                }
            }
        }

        protected override string GenerateUniqueKey()
        {
            return $"g_capsule_{m_radius}_{m_halfHeight}";
        }

        [SerializeField]
        private float m_radius = 0.5f;
        [SerializeField]
        private float m_halfHeight = 0.5f;
    }
}
