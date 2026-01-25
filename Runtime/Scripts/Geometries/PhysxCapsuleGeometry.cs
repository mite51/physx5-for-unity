using System;
using UnityEngine;

namespace PhysX5ForUnity
{
    public enum CapsuleDirection
    {
        XAxis = 0,
        YAxis = 1,
        ZAxis = 2
    }

    [AddComponentMenu("PhysX 5/Geometries/PhysX Capsule Geometry")]
    public class PhysxCapsuleGeometry : PhysxGeometry
    {
        public float Radius
        {
            get { return m_radius; }
            set { m_radius = value; } // This setter exists for dynamic runtime construction. Setting it after the object is registered with an active simulation will have no effect.
        }

        public float Height
        {
            get { return m_height; }
            set { m_height = value; } // This setter exists for dynamic runtime construction. Setting it after the object is registered with an active simulation will have no effect.
        }

        public CapsuleDirection Direction
        {
            get { return m_direction; }
            set { m_direction = value; } // This setter exists for dynamic runtime construction. Setting it after the object is registered with an active simulation will have no effect.
        }

        protected override void CreateGeometry()
        {
            float[] shapeParams = new float[3];
            shapeParams[0] = m_radius;
            shapeParams[1] = m_height * 0.5f;
            m_nativeObjectPtr = PhysxUtils.CreatePxGeometry(PxGeometryType.Capsule, 2, ref shapeParams[0], IntPtr.Zero);
        }
        void OnDrawGizmosSelected()
        {
            if(m_radius>0 && m_height>0)
            {
                Gizmos.color = Color.yellow;

                float height = m_height; // Height of the cylindrical portion
                height = Mathf.Max(height, 0);

                Quaternion directionRotation = Quaternion.identity;
                switch (m_direction)
                {
                    case CapsuleDirection.XAxis:
                        directionRotation = Quaternion.Euler(0, 90, 0);
                        break;
                    case CapsuleDirection.YAxis:
                        directionRotation = Quaternion.Euler(90, 0, 0);
                        break;
                    case CapsuleDirection.ZAxis:
                        directionRotation = Quaternion.Euler(0, 0, 0);
                        break;
                }

                Vector3 direction = (transform.rotation * directionRotation) * Vector3.forward;
                //direction.Normalize();
                Vector3 cylinderPos = transform.position;

                Vector3 rightSphereCenter = cylinderPos + (direction * (height * 0.5f));
                Vector3 leftSphereCenter = cylinderPos - (direction * (height * 0.5f));

                DrawWireHalfSphere(rightSphereCenter, direction, m_radius, 8, 8);
                DrawWireHalfSphere(leftSphereCenter, -direction, m_radius, 8, 8);

                if (height > 0) 
                {
                    DrawWireCylinder(cylinderPos, direction, m_radius, height);
                }
            }
        }

        void DrawWireHalfSphere(Vector3 center, Vector3 direction, float radius, int segments, int slices)
        {
            Vector3 normalizedDir = direction.normalized;
            
            // Create a coordinate system for the hemisphere
            Vector3 up = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(normalizedDir, up)) > 0.9f)
            {
                up = Vector3.right; // Use right if direction is too close to up
            }
            
            Vector3 right = Vector3.Cross(normalizedDir, up).normalized;
            Vector3 forward = Vector3.Cross(right, normalizedDir).normalized;
            
            float angleStep = 90f / segments; // Only draw 90 degrees (from pole to equator)
            
            // Draw longitudinal arcs (from pole to equator)
            for (int i = 0; i <= slices; i++)
            {
                float angle = i * 360f / slices;
                Quaternion rot = Quaternion.AngleAxis(angle, normalizedDir);
                Vector3 arcDirection = rot * right;
                
                for (int j = 0; j < segments; j++)
                {
                    float lat1 = j * angleStep;
                    float lat2 = (j + 1) * angleStep;
                    
                    Vector3 p1 = center + normalizedDir * (radius * Mathf.Cos(lat1 * Mathf.Deg2Rad)) 
                                       + arcDirection * (radius * Mathf.Sin(lat1 * Mathf.Deg2Rad));
                    Vector3 p2 = center + normalizedDir * (radius * Mathf.Cos(lat2 * Mathf.Deg2Rad)) 
                                       + arcDirection * (radius * Mathf.Sin(lat2 * Mathf.Deg2Rad));
                    
                    Gizmos.DrawLine(p1, p2);
                }
            }
            
            // Draw latitudinal circles (parallel to equator)
            for (int j = 1; j < segments; j++)
            {
                float lat = j * angleStep;
                float currentRadius = radius * Mathf.Sin(lat * Mathf.Deg2Rad);
                float offset = radius * Mathf.Cos(lat * Mathf.Deg2Rad);
                
                for (int i = 0; i < slices * 2; i++)
                {
                    float angle1 = i * 360f / (slices * 2);
                    float angle2 = (i + 1) * 360f / (slices * 2);
                    
                    Quaternion rot1 = Quaternion.AngleAxis(angle1, normalizedDir);
                    Quaternion rot2 = Quaternion.AngleAxis(angle2, normalizedDir);
                    
                    Vector3 p1 = center + normalizedDir * offset + rot1 * right * currentRadius;
                    Vector3 p2 = center + normalizedDir * offset + rot2 * right * currentRadius;
                    
                    Gizmos.DrawLine(p1, p2);
                }
            }
            
            // Draw equator circle
            for (int i = 0; i < slices * 2; i++)
            {
                float angle1 = i * 360f / (slices * 2);
                float angle2 = (i + 1) * 360f / (slices * 2);
                
                Quaternion rot1 = Quaternion.AngleAxis(angle1, normalizedDir);
                Quaternion rot2 = Quaternion.AngleAxis(angle2, normalizedDir);
                
                Vector3 p1 = center + rot1 * right * radius;
                Vector3 p2 = center + rot2 * right * radius;
                
                Gizmos.DrawLine(p1, p2);
            }
        }

        void DrawWireCylinder(Vector3 position, Vector3 direction, float radius, float height)
        {
            Vector3 right = direction.normalized * (height * 0.5f);
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
            return $"g_capsule_{m_radius}_{m_height}";
        }

        [SerializeField]
        private float m_radius = 0.5f;
        [SerializeField]
        private float m_height = 0.5f;
        [SerializeField]
        private CapsuleDirection m_direction = CapsuleDirection.XAxis;
    }
}

