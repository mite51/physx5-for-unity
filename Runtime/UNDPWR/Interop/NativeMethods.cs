using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace UNDPWR.Interop
{
    /// <summary>
    /// Raw P/Invoke bindings for the native UNDPWR layer in <c>PxwUndpwr.h</c>.
    /// </summary>
    /// <remarks>
    /// This type is a transcription of the native header and nothing else: no argument
    /// validation, no allocation, no policy. Everything above it in the framework talks
    /// to <see cref="UNDPWR.Core.DeterministicWorld"/> instead, which owns lifetimes and
    /// turns result codes into diagnostics.
    /// <para>
    /// Kept internal deliberately. Calling these directly bypasses the stable-ID
    /// ordering that the whole determinism guarantee rests on.
    /// </para>
    /// </remarks>
    internal static class NativeMethods
    {
#if UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
        internal const string PhysXDll = "libPhysXUnity";
#else
        internal const string PhysXDll = "PhysXUnity.dll";
#endif

        // ------------------------------------------------------------- logging ----

        /// <summary>Signature of the native diagnostic callback.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void LogCallback(int severity, IntPtr message);

        /// <summary>
        /// Installs a diagnostic callback. Pass null to revert to buffered polling.
        /// </summary>
        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PxwSetLogCallback(LogCallback callback);

        // --------------------------------------------------- rigid body extras ----

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PxwSetRigidDynamicSolverIterations(IntPtr actor, uint positionIters, uint velocityIters);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PxwSetRigidBodyMassSpaceInertiaTensor(IntPtr actor, ref Vector3 inertia);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PxwGetRigidBodyMassSpaceInertiaTensor(IntPtr actor, out Vector3 inertia);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PxwSetRigidBodyCMassLocalPose(IntPtr actor, ref SimTransform pose);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PxwSetActorSimulationEnabled(IntPtr actor, [MarshalAs(UnmanagedType.I1)] bool enabled);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PxwApplyDeterministicRigidDefaults(IntPtr actor, uint positionIters, uint velocityIters);

        // ---------------------------------------------------- body gameplay I/O ----
        //
        // These are the reads and writes gameplay needs on a single body inside a step
        // handler. They are declared here so the managed gameplay layer compiles and can
        // be developed in parallel with the native side; see Documentation/NativeGameplayApi.md
        // for the contract each must satisfy.

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PxwBodyAddForce(IntPtr actor, ref Vector3 force, uint mode);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PxwBodyAddTorque(IntPtr actor, ref Vector3 torque, uint mode);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PxwBodyGetPose(IntPtr actor, out SimTransform pose);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PxwBodyTeleport(IntPtr actor, ref SimTransform pose, ref Vector3 velocity, ref Vector3 angularVelocity);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PxwBodyGetLinearVelocity(IntPtr actor, out Vector3 velocity);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PxwBodySetLinearVelocity(IntPtr actor, ref Vector3 velocity);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PxwBodyGetAngularVelocity(IntPtr actor, out Vector3 velocity);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PxwBodySetAngularVelocity(IntPtr actor, ref Vector3 velocity);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern float PxwBodyGetMass(IntPtr actor);

        // -------------------------------------------------------- scene queries ----
        //
        // World-level queries that resolve every hit to a stable ID and return the hits in
        // a deterministic order (raycast/sweep by distance then ID, overlap by ID).

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern unsafe uint PxwWorldRaycast(
            IntPtr world, ref Vector3 origin, ref Vector3 direction, float maxDistance,
            uint filterMask, SimRaycastHit* hits, uint capacity);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern unsafe uint PxwWorldOverlap(
            IntPtr world, uint shape, ref Vector3 center, ref Vector3 halfExtents, float radius,
            ref Quaternion rotation, uint filterMask, SimOverlapHit* hits, uint capacity);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern unsafe uint PxwWorldSweep(
            IntPtr world, uint shape, ref Vector3 origin, ref Vector3 halfExtents, float radius,
            ref Quaternion rotation, ref Vector3 direction, float maxDistance,
            uint filterMask, SimRaycastHit* hits, uint capacity);

        // ------------------------------------------------- contacts and triggers ----
        //
        // Drained once after a step. Both buffers arrive with their pairs normalised to
        // ascending stable-ID order and sorted, so gameplay sees the same events in the
        // same order on every peer.

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern unsafe uint PxwWorldDrainContacts(IntPtr world, SimContactEvent* dst, uint capacity);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern unsafe uint PxwWorldDrainTriggers(IntPtr world, SimTriggerEvent* dst, uint capacity);

        // ---------------------------------------------------------------- mass ----

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int PxwComputeMassProperties(IntPtr actor, float density, float isotropyTolerance,
            [MarshalAs(UnmanagedType.I1)] bool includeNonSimShapes, out SimMassProperties result);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int PxwApplyMassProperties(IntPtr actor, ref SimMassProperties props);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int PxwSetupDeterministicMass(IntPtr actor, float density, float isotropyTolerance,
            [MarshalAs(UnmanagedType.I1)] bool includeNonSimShapes, out SimMassProperties result);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong PxwHashMassProperties(ref SimMassProperties props);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int PxwGetMassProperties(IntPtr actor, out SimMassProperties result);

        // --------------------------------------------------------------- world ----

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr PxwWorldCreate(ref SimSceneDesc desc);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PxwWorldDestroy(IntPtr world);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr PxwWorldGetScene(IntPtr world);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int PxwWorldRegister(IntPtr world, uint stableId, IntPtr handle, uint kind);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int PxwWorldUnregister(IntPtr world, uint stableId);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int PxwWorldCommitPending(IntPtr world);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint PxwWorldGetEntryCount(IntPtr world);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr PxwWorldFindHandle(IntPtr world, uint stableId);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int PxwWorldSetEntryEnabled(IntPtr world, uint stableId, [MarshalAs(UnmanagedType.I1)] bool enabled);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int PxwWorldSetSleepParams(IntPtr world, float linearThreshold, float angularThreshold, uint sleepTicks);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PxwWorldSimulate(IntPtr world, float dt);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PxwWorldFetchResults(IntPtr world);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PxwWorldStep(IntPtr world, float dt);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PxwWorldResetContactStateEx(IntPtr world, uint mode);

        // --------------------------------------------------------------- state ----

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint PxwWorldStateSize(IntPtr world);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern unsafe uint PxwWorldCaptureState(IntPtr world, void* dst, uint capacity, ulong* outHash);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern unsafe int PxwWorldRestoreState(IntPtr world, void* src, uint size);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong PxwWorldHashState(IntPtr world);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern unsafe uint PxwWorldHashPerEntry(IntPtr world, SimEntryHash* dst, uint capacity);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern unsafe uint PxwWorldReadPoses(IntPtr world, SimPoseEntry* dst, uint capacity);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern unsafe uint PxwWorldReadArticulationLinkPoses(IntPtr world, uint stableId, SimTransform* dst, uint capacity);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern unsafe uint PxwWorldReadInternalIds(IntPtr world, SimInternalIdEntry* dst, uint capacity);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong PxwWorldHashInternalIds(IntPtr world);

        [DllImport(PhysXDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern unsafe ulong PxwHashBuffer(void* src, uint size);
    }
}
