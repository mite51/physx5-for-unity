using System;
using UnityEngine;

namespace PhysX5ForUnity
{
    [AddComponentMenu("PhysX 5/Geometries/PhysX Box Geometry")]
    public class PhysxBoxGeometry : PhysxGeometry
    {
        public Vector3 Size
        {
            get { return m_size; }
            set { m_size = value; } // This setter exists for dynamic runtime construction. Setting it after the object is registered with an active simulation will have no effect.
        }

        protected override void CreateGeometry()
        {
            float[] shapeParams = new float[3];
            shapeParams[0] = m_size.x / 2;
            shapeParams[1] = m_size.y / 2;
            shapeParams[2] = m_size.z / 2;
            m_nativeObjectPtr = PhysxUtils.CreatePxGeometry(PxGeometryType.Box, 3, ref shapeParams[0], IntPtr.Zero);
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
                    m_size
                );

                // Set the Gizmos matrix to our transformation matrix
                Gizmos.matrix = transformationMatrix;

                // Draw the cube with the size of (1,1,1) which will be transformed by the matrix
                Gizmos.DrawWireCube(Vector3.zero, Vector3.one);

                // Restore the original Gizmo matrix
                Gizmos.matrix = oldGizmosMatrix;
            }
        }

        protected override string GenerateUniqueKey()
        {
            return $"g_box_{m_size}";
        }

        [SerializeField]
        private Vector3 m_size = Vector3.one;
    }
}
