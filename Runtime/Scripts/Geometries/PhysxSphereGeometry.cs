using System;
using UnityEngine;

namespace PhysX5ForUnity
{
    [AddComponentMenu("PhysX 5/Geometries/PhysX Sphere Geometry")]
    public class PhysxSphereGeometry : PhysxGeometry
    {
        public float Radius
        {
            get { return m_radius; }
            set { m_radius = value; } // This setter exists for dynamic runtime construction. Setting it after the object is registered with an active simulation will have no effect.
        }

        protected override void CreateGeometry()
        {
            float[] shapeParams = new float[1];
            shapeParams[0] = m_radius;
            m_nativeObjectPtr = PhysxUtils.CreatePxGeometry(PxGeometryType.Sphere, 1, ref shapeParams[0], IntPtr.Zero);
        }

        private void OnDrawGizmosSelected()
        {
            if (enabled)
            {
                Gizmos.color = Color.yellow;

                // Save the original Gizmo matrix
                Matrix4x4 oldGizmosMatrix = Gizmos.matrix;

                // Create a transformation matrix that includes position, rotation, and scale
                Matrix4x4 transformationMatrix = Matrix4x4.TRS(
                    transform.position,
                    transform.rotation,
                    new Vector3(1, 1, 1)
                );

                // Set the Gizmos matrix to our transformation matrix
                Gizmos.matrix = transformationMatrix;

                // Draw the cube with the size of (1,1,1) which will be transformed by the matrix
                Gizmos.DrawWireSphere(Vector3.zero, m_radius);

                // Restore the original Gizmo matrix
                Gizmos.matrix = oldGizmosMatrix;
            }
        }

        protected override string GenerateUniqueKey()
        {
            return $"g_sphere_{m_radius}";
        }

        [SerializeField]
        private float m_radius = 0.5f;
    }
}
