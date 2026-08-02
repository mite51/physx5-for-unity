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
