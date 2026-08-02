using System;
using UNDPWR.Diagnostics;
using UNDPWR.Interop;

namespace UNDPWR.Core
{
    /// <summary>
    /// Deterministic mass properties for rigid bodies.
    /// </summary>
    /// <remarks>
    /// Mass properties look like local setup, but they are simulation input, and getting
    /// them to agree across peers is subtler than it appears.
    ///
    /// <para>PhysX supports only a diagonal inertia tensor, so it diagonalises the real
    /// tensor and stores the eigenvector rotation as the body's centre-of-mass
    /// orientation. For a body whose principal moments are close together, which is any
    /// body roughly as wide as it is tall and deep, those eigenvectors barely exist: the
    /// native suite measures a spiked ball whose principal moments differ by 0.25%
    /// turning a 1e-6 m change in shape layout into an 8e-5 rad change in the mass frame,
    /// an amplification of about eighty. Two peers building that body from inputs that
    /// differ in the last bit end up simulating measurably different objects.</para>
    ///
    /// <para>Three things make it safe, all applied by <see cref="Compute"/>:</para>
    /// <list type="bullet">
    /// <item><description>Shape contributions are sorted into a canonical order before
    /// summing, so the result does not depend on attachment order. Reversing attachment
    /// order used to move the centre of mass by 1.3e-9 m.</description></item>
    /// <item><description>A near-isotropic tensor has its mass frame collapsed to the
    /// identity rather than storing an arbitrary rotation, which removes the
    /// amplification entirely and, as a bonus, keeps the actor pose round trip
    /// lossless.</description></item>
    /// <item><description>Otherwise the mass frame quaternion is put in a canonical sign,
    /// since a diagonalisation may return either <c>q</c> or <c>-q</c>.</description></item>
    /// </list>
    ///
    /// <para>Even with all three, the safest pattern is to compute once and replicate.
    /// <see cref="Hash"/> exists so a peer that computed something different is caught at
    /// session join rather than diagnosed from a desync twenty seconds later.</para>
    /// </remarks>
    public static class SimMass
    {
        /// <summary>
        /// Computes mass properties for an actor without applying them.
        /// </summary>
        /// <param name="actorHandle">A <c>PxRigidBody</c> pointer.</param>
        /// <param name="density">Uniform density for every shape, in kg/m³.</param>
        /// <param name="isotropyTolerance">
        /// Relative spread of principal moments below which the mass frame collapses to
        /// the identity. Pass a negative value for the native default of 1%, or zero to
        /// keep exact principal axes and accept the sensitivity.
        /// </param>
        /// <param name="includeNonSimShapes">
        /// Whether shapes without the simulation flag contribute mass. Normally false, so
        /// that trigger volumes do not add weight.
        /// </param>
        /// <exception cref="SimNativeException">
        /// The actor was null, had no shapes that contribute mass, or produced a
        /// degenerate tensor.
        /// </exception>
        public static SimMassProperties Compute(IntPtr actorHandle, float density,
            float isotropyTolerance = -1.0f, bool includeNonSimShapes = false)
        {
            SimMassProperties properties;
            NativeResult result = (NativeResult)NativeMethods.PxwComputeMassProperties(
                actorHandle, density, isotropyTolerance, includeNonSimShapes, out properties);
            result.ThrowIfFailed("PxwComputeMassProperties");

            WarnIfIllConditioned(properties);
            return properties;
        }

        /// <summary>
        /// Applies mass properties verbatim, with no recomputation.
        /// </summary>
        /// <remarks>
        /// This is what every peer should call, using one computed or authored value, so
        /// that no peer's local floating point ever enters the picture.
        /// </remarks>
        public static void Apply(IntPtr actorHandle, SimMassProperties properties)
        {
            NativeResult result = (NativeResult)NativeMethods.PxwApplyMassProperties(actorHandle, ref properties);
            result.ThrowIfFailed("PxwApplyMassProperties");
        }

        /// <summary>
        /// Computes and applies in one call.
        /// </summary>
        /// <remarks>
        /// Safe when every peer runs it against an identical body, which is the case for
        /// authored content loaded from the same asset. For anything assembled at runtime
        /// from data that might differ, compute on one peer and replicate the result.
        /// </remarks>
        public static SimMassProperties Setup(IntPtr actorHandle, float density,
            float isotropyTolerance = -1.0f, bool includeNonSimShapes = false)
        {
            SimMassProperties properties;
            NativeResult result = (NativeResult)NativeMethods.PxwSetupDeterministicMass(
                actorHandle, density, isotropyTolerance, includeNonSimShapes, out properties);
            result.ThrowIfFailed("PxwSetupDeterministicMass");

            WarnIfIllConditioned(properties);
            return properties;
        }

        /// <summary>
        /// Reads back what an actor currently has, to verify it matches what was applied.
        /// </summary>
        public static SimMassProperties Read(IntPtr actorHandle)
        {
            SimMassProperties properties;
            NativeResult result = (NativeResult)NativeMethods.PxwGetMassProperties(actorHandle, out properties);
            result.ThrowIfFailed("PxwGetMassProperties");
            return properties;
        }

        /// <summary>
        /// Hashes the physically meaningful fields, for comparing setup across peers.
        /// </summary>
        /// <remarks>
        /// Covers mass, inertia, mass frame and shape count. Excludes the diagnostic
        /// fields, so a peer that used a different isotropy tolerance but arrived at the
        /// same numbers is not rejected.
        /// </remarks>
        public static ulong Hash(SimMassProperties properties)
        {
            return NativeMethods.PxwHashMassProperties(ref properties);
        }

        /// <summary>
        /// Checks that an actor's live mass properties match what it was supposed to get.
        /// </summary>
        /// <returns>True when they match exactly.</returns>
        public static bool Verify(IntPtr actorHandle, SimMassProperties expected)
        {
            SimMassProperties actual = Read(actorHandle);
            if (Hash(actual) == Hash(expected))
            {
                return true;
            }

            SimLog.Error(string.Format(
                "Mass properties do not match what was applied. Expected mass {0:R}, inertia ({1:R}, {2:R}, {3:R}); " +
                "found mass {4:R}, inertia ({5:R}, {6:R}, {7:R}). A peer simulating this body will diverge.",
                expected.Mass, expected.Inertia.x, expected.Inertia.y, expected.Inertia.z,
                actual.Mass, actual.Inertia.x, actual.Inertia.y, actual.Inertia.z));
            return false;
        }

        private static void WarnIfIllConditioned(SimMassProperties properties)
        {
            if (properties.MassFrameCollapsed != 0)
            {
                SimLog.Verbose(string.Format(
                    "Mass frame collapsed to identity; principal moments differ by only {0:F3}%.",
                    properties.Anisotropy * 100.0f));
                return;
            }

            // Above the collapse threshold but still close enough that the principal axes
            // move noticeably under small input changes. The native layer warns too; this
            // repeats it in managed terms so the message names the actor being set up.
            if (properties.Anisotropy < 0.05f)
            {
                SimLog.Warning(string.Format(
                    "A body's principal moments differ by only {0:F3}%, so its mass frame is ill conditioned and " +
                    "sensitive to last-bit differences in shape layout. Replicate these mass properties rather " +
                    "than recomputing them per peer, or raise SimConfig.MassIsotropyTolerance above {1:F4} to " +
                    "collapse the frame.",
                    properties.Anisotropy * 100.0f, properties.Anisotropy));
            }
        }
    }
}
