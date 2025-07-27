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
            set { m_radius = value; } // This setter exists for dynamic runtime construction. Setting it after the object is registered with an active simulation will have no effect.
        }

        public float HalfHeight
        {
            get { return m_halfHeight; }
            set { m_halfHeight = value; } // This setter exists for dynamic runtime construction. Setting it after the object is registered with an active simulation will have no effect.
        }

        public int direction
        {
            get { return m_direction; }
            set { m_direction = value; } // This setter exists for dynamic runtime construction. Setting it after the object is registered with an active simulation will have no effect.
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

            float halfHeight = m_halfHeight;

            Quaternion directionRotation = Quaternion.identity;
            switch (m_direction)
            {
                case 0: // X-axis
                    directionRotation = Quaternion.Euler(90, 0, 0);
                    break;
                case 1: // Y-axis
                    directionRotation = Quaternion.Euler(0, 0, 90);
                    break;
                case 2: // Z-axis
                    directionRotation = Quaternion.Euler(0, 90, 0);
                    break;
            }

            Vector3 direction = (transform.rotation * directionRotation) * Vector3.forward;
            Vector3 cylinderPos = transform.position - (direction * (halfHeight + (m_radius * 0.5f)));

            Vector3 rightSphere = cylinderPos + (direction * halfHeight);
            Vector3 leftSphere = cylinderPos - (direction * halfHeight);

            Gizmos.DrawWireSphere(rightSphere, m_radius);
            Gizmos.DrawWireSphere(leftSphere, m_radius);

            DrawWireCylinder(cylinderPos, direction, m_radius, halfHeight * 2);
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
        [SerializeField]
        private int m_direction = 0;
    }
}
