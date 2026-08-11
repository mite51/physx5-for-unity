using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UNDPWR.Interop;

namespace UNDPWR.Diagnostics
{
    /// <summary>
    /// Diagnostic routing for the framework and for the native layer beneath it.
    /// </summary>
    /// <remarks>
    /// Determinism bugs are diagnosed after the fact from whatever was recorded at the
    /// time, so logging is treated as part of the framework rather than as debug
    /// scaffolding. Two rules follow from that.
    /// <para>
    /// First, every message carries the tick it happened on. A message without a tick
    /// cannot be lined up against another peer's log, which is the only way most desyncs
    /// are ever understood.
    /// </para>
    /// <para>
    /// Second, the per-tick path must not allocate. String formatting in a rollback loop
    /// that replays the prediction window every frame is not free, so the
    /// verbose channels compile out unless <c>UNDPWR_VERBOSE_LOGGING</c> is defined and
    /// check <see cref="Level"/> before formatting anything.
    /// </para>
    /// </remarks>
    public static class SimLog
    {
        /// <summary>How much detail is emitted.</summary>
        public enum Verbosity
        {
            /// <summary>Nothing at all.</summary>
            Silent = 0,

            /// <summary>Failures only.</summary>
            Errors = 1,

            /// <summary>Failures and suspicious situations. The default.</summary>
            Warnings = 2,

            /// <summary>Session lifecycle events as well.</summary>
            Info = 3,

            /// <summary>Per-tick detail. Expensive; for investigation only.</summary>
            Verbose = 4
        }

        /// <summary>The current verbosity. Defaults to <see cref="Verbosity.Warnings"/>.</summary>
        public static Verbosity Level = Verbosity.Warnings;

        /// <summary>
        /// The tick currently being simulated, stamped onto every message.
        /// </summary>
        /// <remarks>
        /// Set by the rollback engine as it advances or replays, so a log line records
        /// the tick it describes rather than the tick that happened to be current when it
        /// was flushed. Negative means no tick context.
        /// </remarks>
        public static int CurrentTick = -1;

        /// <summary>
        /// Identifies this peer in log output, so that logs from several peers can be
        /// interleaved and read.
        /// </summary>
        public static string PeerName = "peer";

        /// <summary>
        /// Raised for every message, for routing somewhere other than the Unity console.
        /// </summary>
        public static event Action<Verbosity, string> MessageLogged;

        private static NativeMethods.LogCallback _nativeCallback;

        /// <summary>
        /// Routes native diagnostics through this logger.
        /// </summary>
        /// <remarks>
        /// The delegate is held in a static field on purpose. Native code keeps the
        /// function pointer indefinitely, and a delegate that only exists as an argument
        /// is collectable the moment the call returns, which produces a crash later that
        /// looks nothing like its cause.
        /// </remarks>
        public static void AttachNativeSink()
        {
            if (_nativeCallback != null)
            {
                return;
            }
            _nativeCallback = OnNativeMessage;
            NativeMethods.PxwSetLogCallback(_nativeCallback);
        }

        /// <summary>Stops routing native diagnostics. Safe to call when not attached.</summary>
        public static void DetachNativeSink()
        {
            if (_nativeCallback == null)
            {
                return;
            }
            NativeMethods.PxwSetLogCallback(null);
            _nativeCallback = null;
        }

        [AOT.MonoPInvokeCallback(typeof(NativeMethods.LogCallback))]
        private static void OnNativeMessage(int severity, IntPtr message)
        {
            // Any exception escaping here unwinds into native code, which is undefined
            // behaviour, so nothing in this method is allowed to throw.
            try
            {
                string text = message == IntPtr.Zero ? "(null)" : Marshal.PtrToStringAnsi(message);
                switch ((SimLogSeverity)severity)
                {
                    case SimLogSeverity.Error: Error("[native] " + text); break;
                    case SimLogSeverity.Warning: Warning("[native] " + text); break;
                    case SimLogSeverity.Info: Info("[native] " + text); break;
                    default: Verbose("[native] " + text); break;
                }
            }
            catch (Exception)
            {
                // Deliberately swallowed. There is no safe way to report a failure here.
            }
        }

        /// <summary>Logs a failure.</summary>
        public static void Error(string message)
        {
            if (Level < Verbosity.Errors) return;
            Emit(Verbosity.Errors, message);
        }

        /// <summary>Logs something suspicious that did not stop the operation.</summary>
        public static void Warning(string message)
        {
            if (Level < Verbosity.Warnings) return;
            Emit(Verbosity.Warnings, message);
        }

        /// <summary>Logs a session lifecycle event.</summary>
        public static void Info(string message)
        {
            if (Level < Verbosity.Info) return;
            Emit(Verbosity.Info, message);
        }

        /// <summary>
        /// Logs per-tick detail. Compiled out entirely unless <c>UNDPWR_VERBOSE_LOGGING</c>
        /// is defined, so a call site costs nothing in a normal build.
        /// </summary>
        [System.Diagnostics.Conditional("UNDPWR_VERBOSE_LOGGING")]
        public static void Verbose(string message)
        {
            if (Level < Verbosity.Verbose) return;
            Emit(Verbosity.Verbose, message);
        }

        private static void Emit(Verbosity level, string message)
        {
            string line = CurrentTick >= 0
                ? string.Format("[UNDPWR:{0}][t{1}] {2}", PeerName, CurrentTick, message)
                : string.Format("[UNDPWR:{0}] {1}", PeerName, message);

            Action<Verbosity, string> handler = MessageLogged;
            if (handler != null)
            {
                handler(level, line);
            }

            switch (level)
            {
                case Verbosity.Errors: Debug.LogError(line); break;
                case Verbosity.Warnings: Debug.LogWarning(line); break;
                default: Debug.Log(line); break;
            }
        }
    }
}
