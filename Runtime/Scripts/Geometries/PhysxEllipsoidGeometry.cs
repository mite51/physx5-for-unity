using UnityEngine;

namespace PhysX5ForUnity
{
    /// <summary>
    /// An ellipsoid centred on the local origin and aligned with the local axes.
    /// </summary>
    /// <remarks>
    /// With three equal radii and a zero margin this is a sphere, but
    /// <see cref="PhysxSphereGeometry"/> has a cheaper dedicated narrowphase path and should be
    /// preferred for that case.
    /// </remarks>
    [AddComponentMenu("PhysX 5/Geometries/PhysX Ellipsoid Geometry")]
    public class PhysxEllipsoidGeometry : PhysxConvexCoreGeometry
    {
        public Vector3 Radii
        {
            get { return m_radii; }
            set { m_radii = value; } // This setter exists for dynamic runtime construction. Setting it after the object is registered with an active simulation will have no effect.
        }

        public override PxConvexCoreType CoreType { get { return PxConvexCoreType.Ellipsoid; } }

        protected override float[] GetCoreParams()
        {
            return new float[] { m_radii.x, m_radii.y, m_radii.z };
        }

        private void OnDrawGizmosSelected()
        {
            if (!enabled || m_radii.x <= 0.0f || m_radii.y <= 0.0f || m_radii.z <= 0.0f) return;

            Gizmos.color = Color.yellow;

            Matrix4x4 oldMatrix = Gizmos.matrix;

            // The margin expands every axis uniformly, so it adds to each radius.
            Vector3 radii = m_radii + Vector3.one * Margin;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, radii * 2.0f);
            Gizmos.DrawWireSphere(Vector3.zero, 0.5f);

            Gizmos.matrix = oldMatrix;
        }

        [SerializeField]
        [Tooltip("Radii along the local X, Y and Z axes, before the margin is added.")]
        private Vector3 m_radii = new Vector3(0.5f, 0.25f, 0.25f);
    }
}
