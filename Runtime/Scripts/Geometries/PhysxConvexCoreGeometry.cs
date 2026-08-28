using System;
using System.Text;
using UnityEngine;

namespace PhysX5ForUnity
{
    /// <summary>
    /// Base for geometries backed by PhysX's convex core support functions. A convex core
    /// shape is a small analytic core swept by <see cref="Margin"/>, resolved by GJK rather
    /// than by a cooked hull, so it stays exact as it rotates instead of presenting flat
    /// facets that contact points snap between.
    /// </summary>
    /// <remarks>
    /// <para>Cylinder, cone and segment cores run along local +X, the same convention
    /// <see cref="PhysxCapsuleGeometry"/> follows. A geometry carries no pose of its own, so
    /// reorient one by rotating the GameObject its <see cref="PhysxShape"/> sits on.</para>
    /// <para>PhysX has no scale term for convex core geometry, unlike the mesh geometries, so
    /// the owning transform's scale is ignored. Author the dimensions directly.</para>
    /// </remarks>
    public abstract class PhysxConvexCoreGeometry : PhysxGeometry
    {
        /// <summary>
        /// Distance the analytic core is expanded by. Rounds the shape's edges, which can steady
        /// contact.
        /// </summary>
        /// <remarks>
        /// This is <b>additive</b>: the shape is the core <i>grown</i> by the margin, so the
        /// authored dimensions describe the core and the true size is larger. For a cylinder the
        /// true radius is <c>radius + margin</c> and the true length is <c>height + 2 * margin</c>.
        /// <para>
        /// This is the opposite of a vehicle wheel's
        /// <c>PhysxVehicleWheelAttachment.cylinderMargin</c>, which is size-preserving: a wheel has
        /// an authored radius and width to keep, so that path shrinks the core by the margin first
        /// and the outer size does not change. See <see cref="PxConvexCoreType"/> for the full
        /// note on the two meanings.
        /// </para>
        /// </remarks>
        public float Margin
        {
            get { return m_margin; }
            set { m_margin = value; } // This setter exists for dynamic runtime construction. Setting it after the object is registered with an active simulation will have no effect.
        }

        /// <summary>Which core this component authors.</summary>
        public abstract PxConvexCoreType CoreType { get; }

        /// <summary>
        /// The core's parameters in the order the native factory expects. The length must match
        /// the core: segment 1, box 3, ellipsoid 3, cylinder 2, cone 2.
        /// </summary>
        protected abstract float[] GetCoreParams();

        protected override void CreateGeometry()
        {
            float[] coreParams = GetCoreParams();
            m_nativeObjectPtr = PhysxUtils.CreateConvexCoreGeometry(CoreType, coreParams.Length, ref coreParams[0], m_margin);
            if (m_nativeObjectPtr == IntPtr.Zero)
            {
                throw new Exception($"Failed to create {CoreType} convex core geometry.");
            }
        }

        protected override string GenerateUniqueKey()
        {
            // Full-precision ("R") formatting throughout: the shared-geometry cache is keyed on
            // this string, so rounding would let two shapes with slightly different dimensions
            // silently share one native geometry.
            float[] coreParams = GetCoreParams();
            StringBuilder key = new StringBuilder("g_convexcore_").Append((int)CoreType);
            for (int i = 0; i < coreParams.Length; ++i)
            {
                key.Append('_').Append(coreParams[i].ToString("R"));
            }
            return key.Append('_').Append(m_margin.ToString("R")).ToString();
        }

        /// <summary>
        /// Draws a wire circle of <paramref name="radius"/> centred at <paramref name="center"/>
        /// in the plane spanned by <paramref name="axisA"/> and <paramref name="axisB"/>, both of
        /// which are expected to be unit length.
        /// </summary>
        protected static void DrawWireCircle(Vector3 center, Vector3 axisA, Vector3 axisB, float radius, int segments = 32)
        {
            Vector3 previous = center + axisA * radius;
            for (int i = 1; i <= segments; ++i)
            {
                float angle = i * 2.0f * Mathf.PI / segments;
                Vector3 next = center + (axisA * Mathf.Cos(angle) + axisB * Mathf.Sin(angle)) * radius;
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }

        /// <summary>The local +X core axis in world space.</summary>
        protected Vector3 WorldAxis { get { return transform.rotation * Vector3.right; } }

        /// <summary>An orthonormal basis for the plane perpendicular to <see cref="WorldAxis"/>.</summary>
        protected void GetWorldRadialAxes(out Vector3 axisA, out Vector3 axisB)
        {
            axisA = transform.rotation * Vector3.up;
            axisB = transform.rotation * Vector3.forward;
        }

        [SerializeField]
        [Tooltip("Distance the core is expanded by. A non-zero margin rounds the shape's edges, " +
                 "which can steady contact, but it also GROWS the shape: a cylinder's true " +
                 "radius is radius + margin and its true length is height + 2 * margin. Note this " +
                 "is the opposite of a vehicle wheel's Cylinder Margin, which shrinks the core to " +
                 "keep the wheel's authored size.")]
        private float m_margin = 0.0f;
    }
}
