using System;
using UnityEngine;

namespace PhysX5ForUnity
{
    [AddComponentMenu("PhysX 5/Geometries/PhysX Plane Geometry")]
    public class PhysxPlaneGeometry : PhysxGeometry
    {
        protected override void CreateGeometry()
        {
            // PxPlaneGeometry is parameterless. The collision surface is the local YZ plane
            // and the surface normal is the shape's local +X axis. A dummy array is required
            // because the P/Invoke signature takes a ref float.
            float[] shapeParams = new float[1];
            m_nativeObjectPtr = PhysxUtils.CreatePxGeometry(PxGeometryType.Plane, 0, ref shapeParams[0], IntPtr.Zero);
            if (m_nativeObjectPtr == IntPtr.Zero)
            {
                throw new Exception("Failed to create plane geometry.");
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (enabled)
            {
                Gizmos.color = Color.yellow;

                Matrix4x4 oldGizmosMatrix = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);

                // The collision surface lies in the local YZ plane (normal = local +X).
                float half = m_gizmoSize * 0.5f;
                Vector3 c0 = new Vector3(0f, -half, -half);
                Vector3 c1 = new Vector3(0f, -half, half);
                Vector3 c2 = new Vector3(0f, half, half);
                Vector3 c3 = new Vector3(0f, half, -half);

                Gizmos.DrawLine(c0, c1);
                Gizmos.DrawLine(c1, c2);
                Gizmos.DrawLine(c2, c3);
                Gizmos.DrawLine(c3, c0);

                // Normal arrow pointing along the local +X axis.
                float arrowLength = Mathf.Max(m_gizmoSize * 0.25f, 0.1f);
                Vector3 tip = Vector3.right * arrowLength;
                Gizmos.DrawLine(Vector3.zero, tip);

                float headSize = arrowLength * 0.2f;
                Gizmos.DrawLine(tip, tip + new Vector3(-headSize, headSize, 0f));
                Gizmos.DrawLine(tip, tip + new Vector3(-headSize, -headSize, 0f));
                Gizmos.DrawLine(tip, tip + new Vector3(-headSize, 0f, headSize));
                Gizmos.DrawLine(tip, tip + new Vector3(-headSize, 0f, -headSize));

                Gizmos.matrix = oldGizmosMatrix;
            }
        }

        protected override string GenerateUniqueKey()
        {
            return "g_plane";
        }

        [SerializeField]
        private float m_gizmoSize = 10f;
    }
}
