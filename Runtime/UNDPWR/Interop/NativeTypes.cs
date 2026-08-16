using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace UNDPWR.Interop
{
    /// <summary>
    /// Result codes returned by the native UNDPWR registry and state functions.
    /// Mirrors <c>pxw::PxwResult</c>.
    /// </summary>
    /// <remarks>
    /// Native code never throws. Every entry point returns one of these instead, so a
    /// failure in a rollback hot path costs a comparison rather than an exception.
    /// Use <see cref="NativeResultExtensions.ThrowIfFailed"/> at setup boundaries and
    /// plain checks inside the tick loop.
    /// </remarks>
    public enum NativeResult
    {
        /// <summary>The call succeeded.</summary>
        Ok = 0,

        /// <summary>A required pointer argument was null.</summary>
        NullArgument = -1,

        /// <summary>A stable ID was registered that is already in use.</summary>
        DuplicateId = -2,

        /// <summary>A stable ID was referenced that is not in the registry.</summary>
        UnknownId = -3,

        /// <summary>The destination buffer was too small for the data being written.</summary>
        BufferTooSmall = -4,

        /// <summary>A state blob failed its magic-number or structural validation.</summary>
        BadFormat = -5,

        /// <summary>A state blob was written by an incompatible version of the layer.</summary>
        VersionMismatch = -6,

        /// <summary>
        /// A state blob describes a different set of entries than the world holds. The
        /// usual cause is two peers whose worlds were built differently.
        /// </summary>
        EntryMismatch = -7,

        /// <summary>The operation is not legal while the scene is mid-simulation.</summary>
        Busy = -8
    }

    /// <summary>Helpers for turning a <see cref="NativeResult"/> into an exception.</summary>
    public static class NativeResultExtensions
    {
        /// <summary>Returns true when the result indicates success.</summary>
        public static bool Succeeded(this NativeResult result)
        {
            return result == NativeResult.Ok;
        }

        /// <summary>
        /// Throws a descriptive <see cref="SimNativeException"/> unless the result is
        /// <see cref="NativeResult.Ok"/>. Intended for setup and teardown, not for the
        /// per-tick path.
        /// </summary>
        /// <param name="result">The result to inspect.</param>
        /// <param name="operation">
        /// What was being attempted, used verbatim in the exception message.
        /// </param>
        public static void ThrowIfFailed(this NativeResult result, string operation)
        {
            if (result == NativeResult.Ok)
            {
                return;
            }
            throw new SimNativeException(result, operation);
        }
    }

    /// <summary>Raised when a native UNDPWR call fails at a setup boundary.</summary>
    public class SimNativeException : Exception
    {
        /// <summary>The code the native layer returned.</summary>
        public NativeResult Result { get; private set; }

        /// <summary>Creates an exception describing a failed native call.</summary>
        public SimNativeException(NativeResult result, string operation)
            : base(string.Format("UNDPWR native call failed: {0} returned {1}", operation, result))
        {
            Result = result;
        }
    }

    /// <summary>
    /// Discriminates what kind of object a registry entry refers to, which decides how
    /// its state is captured. Mirrors <c>pxw::PxwHandleKind</c>.
    /// </summary>
    public enum SimHandleKind : uint
    {
        /// <summary>A non-kinematic <c>PxRigidDynamic</c>.</summary>
        RigidDynamic = 0,

        /// <summary>
        /// A <c>PxRigidStatic</c>. Never captured, since it cannot move. Registered only
        /// so that queries and contact reports can resolve it to a stable ID.
        /// </summary>
        RigidStatic = 1,

        /// <summary>A <c>PxRigidDynamic</c> with the kinematic flag set.</summary>
        RigidKinematic = 2,

        /// <summary>A <c>PxArticulationReducedCoordinate</c>.</summary>
        Articulation = 3,

        /// <summary>A PhysX Vehicle2 vehicle.</summary>
        Vehicle = 4
    }

    /// <summary>
    /// Scene creation flags. Mirrors <c>pxw::PxwSceneFlag</c>.
    /// </summary>
    /// <remarks>
    /// These values are a wire format: the native side switches on the raw bits, so a member
    /// declared in the wrong position does not fail to compile, does not fail a config hash
    /// check — both peers compute the same wrong number — and does not throw. It silently
    /// builds a different scene than the one asked for. Keep the declaration order identical
    /// to <c>PxwSceneFlag</c> in <c>DataInterop.h</c>, and see
    /// <c>SimTimingTests.SceneFlagsMatchTheNativeHeader</c>, which pins every value.
    /// </remarks>
    [Flags]
    public enum SimSceneFlags : uint
    {
        /// <summary>No flags.</summary>
        None = 0,

        /// <summary>Persistent contact manifolds. On by default.</summary>
        EnablePcm = 1 << 0,

        /// <summary>Continuous collision detection.</summary>
        EnableCcd = 1 << 1,

        /// <summary>Stabilization pass for resting stacks.</summary>
        EnableStabilization = 1 << 2,

        /// <summary>
        /// <c>PxSceneFlag::eENABLE_ENHANCED_DETERMINISM</c>. Makes results independent of
        /// the number of worker threads and of actors that are not interacting. Required
        /// for cross-peer determinism and enabled by every UNDPWR preset.
        /// </summary>
        EnhancedDeterminism = 1 << 4,

        /// <summary>Direct GPU API. Ignored unless the scene is a GPU scene.</summary>
        EnableDirectGpuApi = 1 << 5,

        /// <summary>
        /// Suppresses the PhysX Visual Debugger connection. Always set for headless test
        /// runs, since a PVD connection perturbs timing.
        /// </summary>
        DisablePvd = 1 << 6,

        /// <summary>
        /// Installs the notification-adding filter shader. The UNDPWR world layer forces this
        /// on for every world it creates, so managed callers never need to pass it.
        /// </summary>
        EnableContactEvents = 1 << 7
    }

    /// <summary>Severity of a native diagnostic. Mirrors <c>pxw::PxwLogSeverity</c>.</summary>
    public enum SimLogSeverity
    {
        /// <summary>Verbose detail, off unless explicitly enabled.</summary>
        Debug = 0,

        /// <summary>Normal progress information.</summary>
        Info = 1,

        /// <summary>Something suspicious that did not stop the operation.</summary>
        Warning = 2,

        /// <summary>An operation failed.</summary>
        Error = 3
    }

    /// <summary>
    /// A position and orientation laid out to match <c>pxw::PxwTransformData</c>, which
    /// is a <c>PxTransform</c>: quaternion first, then position.
    /// </summary>
    /// <remarks>
    /// Field order matters. This struct is memcpy'd across the interop boundary in bulk
    /// arrays, so it must match the native layout exactly rather than being marshalled
    /// field by field.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct SimTransform
    {
        /// <summary>Orientation, x y z w, matching <c>PxQuat</c>.</summary>
        public float QuatX;

        /// <summary>Orientation, x y z w, matching <c>PxQuat</c>.</summary>
        public float QuatY;

        /// <summary>Orientation, x y z w, matching <c>PxQuat</c>.</summary>
        public float QuatZ;

        /// <summary>Orientation, x y z w, matching <c>PxQuat</c>.</summary>
        public float QuatW;

        /// <summary>Position.</summary>
        public float PosX;

        /// <summary>Position.</summary>
        public float PosY;

        /// <summary>Position.</summary>
        public float PosZ;

        /// <summary>Builds a native transform from Unity types.</summary>
        public SimTransform(Vector3 position, Quaternion rotation)
        {
            QuatX = rotation.x;
            QuatY = rotation.y;
            QuatZ = rotation.z;
            QuatW = rotation.w;
            PosX = position.x;
            PosY = position.y;
            PosZ = position.z;
        }

        /// <summary>The position as a Unity vector.</summary>
        public Vector3 Position
        {
            get { return new Vector3(PosX, PosY, PosZ); }
            set { PosX = value.x; PosY = value.y; PosZ = value.z; }
        }

        /// <summary>The orientation as a Unity quaternion.</summary>
        public Quaternion Rotation
        {
            get { return new Quaternion(QuatX, QuatY, QuatZ, QuatW); }
            set { QuatX = value.x; QuatY = value.y; QuatZ = value.z; QuatW = value.w; }
        }

        /// <summary>An identity transform.</summary>
        public static SimTransform Identity
        {
            get { return new SimTransform(Vector3.zero, Quaternion.identity); }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format("p({0:F6}, {1:F6}, {2:F6}) q({3:F6}, {4:F6}, {5:F6}, {6:F6})",
                PosX, PosY, PosZ, QuatX, QuatY, QuatZ, QuatW);
        }
    }

    /// <summary>
    /// Explicit scene configuration. Mirrors <c>pxw::PxwSceneDesc</c>.
    /// </summary>
    /// <remarks>
    /// Every field is set explicitly rather than inherited from a PhysX default, because
    /// a default that changes between SDK versions is a silent determinism break. Build
    /// one with <see cref="UNDPWR.Core.SimConfig"/> rather than filling it in by hand.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct SimSceneDesc
    {
        /// <summary>Gravity, in metres per second squared.</summary>
        public Vector3 Gravity;

        /// <summary>Combination of <see cref="SimSceneFlags"/>.</summary>
        public uint Flags;

        /// <summary>Scene query pruning structure, a <c>PxPruningStructureType</c>.</summary>
        public int PruningStructureType;

        /// <summary>Solver type, a <c>PxSolverType</c>. PGS is the UNDPWR default.</summary>
        public int SolverType;

        /// <summary>Broadphase type, a <c>PxBroadPhaseType</c>, or -1 for the PhysX default.</summary>
        public int BroadPhaseType;

        /// <summary>
        /// Worker thread count. Zero means the scene runs entirely on the calling thread,
        /// which is the only configuration whose task ordering is guaranteed reproducible
        /// without relying on <see cref="SimSceneFlags.EnhancedDeterminism"/>.
        /// </summary>
        public uint CpuWorkerThreads;

        /// <summary>Non-zero to request GPU simulation. Experimental; see the backend docs.</summary>
        public uint UseGpu;

        /// <summary>Relative velocity below which contacts do not bounce.</summary>
        public float BounceThresholdVelocity;

        /// <summary>Distance within which friction anchors are merged.</summary>
        public float FrictionOffsetThreshold;

        /// <summary>Maximum continuous collision detection passes per step.</summary>
        public uint CcdMaxPasses;
    }

    /// <summary>
    /// Mass, inertia and mass frame for one body, in a form that can be compared, hashed
    /// and replicated. Mirrors <c>pxw::PxwMassProperties</c>.
    /// </summary>
    /// <remarks>
    /// These must be computed once and shared, not recomputed independently by each peer.
    /// PhysX supports only a diagonal inertia tensor, so it diagonalises the real tensor
    /// and stores the eigenvector rotation as the centre-of-mass orientation. For a body
    /// whose principal moments are close together, which is any body roughly as wide as
    /// it is tall and deep, those eigenvectors are ill conditioned: the native suite
    /// measures a spiked ball whose principal moments differ by 0.25% turning a 1e-6 m
    /// change in shape layout into an 8e-5 rad change in the mass frame, an amplification
    /// of about eighty.
    /// <para>
    /// <see cref="UNDPWR.Core.SimMass"/> wraps the computation and applies the
    /// canonicalisation that removes that amplification.
    /// </para>
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct SimMassProperties
    {
        /// <summary>Total mass, in kilograms.</summary>
        public float Mass;

        /// <summary>Diagonal inertia, expressed in the mass frame.</summary>
        public Vector3 Inertia;

        /// <summary>The mass frame relative to the actor origin.</summary>
        public SimTransform CenterOfMassLocalPose;

        /// <summary>
        /// <c>(largest - smallest) / largest</c> principal moment. Near zero means the
        /// body is inertially close to a sphere and its principal axes are ill
        /// conditioned.
        /// </summary>
        public float Anisotropy;

        /// <summary>How many shapes contributed.</summary>
        public uint ShapeCount;

        /// <summary>
        /// Non-zero when the tensor was within the isotropy tolerance and the mass frame
        /// was collapsed to the identity. A near-origin centre of mass is snapped to the
        /// actor origin in that case as well, so the whole mass frame is peer-identical.
        /// </summary>
        public uint MassFrameCollapsed;
    }

    /// <summary>
    /// A per-entry checksum, used to pinpoint which body diverged.
    /// Mirrors <c>pxw::PxwEntryHash</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SimEntryHash
    {
        /// <summary>The stable ID of the entry.</summary>
        public uint StableId;

        /// <summary>The entry's <see cref="SimHandleKind"/>.</summary>
        public uint Kind;

        /// <summary>FNV-1a hash of the entry's captured state.</summary>
        public ulong Hash;
    }

    /// <summary>
    /// A pose readback record, for driving presentation transforms.
    /// Mirrors <c>pxw::PxwPoseEntry</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SimPoseEntry
    {
        /// <summary>The stable ID of the entry.</summary>
        public uint StableId;

        /// <summary>The entry's <see cref="SimHandleKind"/>.</summary>
        public uint Kind;

        /// <summary>The entry's current world pose.</summary>
        public SimTransform Pose;
    }

}
