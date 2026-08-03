using System;
using UnityEngine;
using UNDPWR.Core;
using UNDPWR.Interop;

namespace UNDPWR.Gameplay
{
    /// <summary>
    /// The forces, impulses and reads gameplay applies to a single body during a step.
    /// </summary>
    /// <remarks>
    /// Every one of these must happen inside <see cref="Rollback.ISimStepHandler.OnBeforeStep"/>,
    /// never from an ordinary <c>Update</c> or a collision callback. A rollback replays
    /// ticks; a force applied outside the step handler happens on the original pass and not
    /// on the replay, which desyncs a peer against itself — the most confusing failure this
    /// framework has. The reads (<see cref="GetLinearVelocity"/> and friends) are safe
    /// anywhere the world is not mid-simulate, but gameplay that feeds a read back into a
    /// force must do both inside the same step handler.
    /// <para>
    /// This is a thin static wrapper over the native per-body entry points. It takes the
    /// <c>PxActor</c> handle from <see cref="SimEntity.NativeHandle"/>; the overloads that
    /// take a <see cref="SimEntity"/> are the same call with the handle pulled out.
    /// </para>
    /// </remarks>
    public static class SimBody
    {
        /// <summary>Applies a force to a body.</summary>
        public static void AddForce(IntPtr actor, Vector3 force, SimForceMode mode = SimForceMode.Force)
        {
            NativeMethods.PxwBodyAddForce(actor, ref force, (uint)mode);
        }

        /// <summary>Applies a torque to a body.</summary>
        public static void AddTorque(IntPtr actor, Vector3 torque, SimForceMode mode = SimForceMode.Force)
        {
            NativeMethods.PxwBodyAddTorque(actor, ref torque, (uint)mode);
        }

        /// <summary>Reads a body's world pose.</summary>
        public static SimTransform GetPose(IntPtr actor)
        {
            SimTransform pose;
            NativeMethods.PxwBodyGetPose(actor, out pose);
            return pose;
        }

        /// <summary>
        /// Moves a body to a pose and sets its velocities, for spawning a pooled object.
        /// </summary>
        /// <remarks>
        /// A teleport, not a physical move: it places the body rather than pushing it, and
        /// re-pins the wake counter the same way a restore does, so a body brought out of a
        /// pool is awake and simulated. Use it only when activating a pooled entity, never
        /// for ordinary movement — that is what forces are for.
        /// </remarks>
        public static void Teleport(IntPtr actor, Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity)
        {
            SimTransform pose = new SimTransform(position, rotation);
            NativeMethods.PxwBodyTeleport(actor, ref pose, ref velocity, ref angularVelocity);
        }

        /// <summary>Reads a body's world position.</summary>
        public static Vector3 GetPosition(IntPtr actor)
        {
            return GetPose(actor).Position;
        }

        /// <summary>Reads a body's world orientation.</summary>
        public static Quaternion GetRotation(IntPtr actor)
        {
            return GetPose(actor).Rotation;
        }

        /// <summary>Reads a body's linear velocity.</summary>
        public static Vector3 GetLinearVelocity(IntPtr actor)
        {
            Vector3 velocity;
            NativeMethods.PxwBodyGetLinearVelocity(actor, out velocity);
            return velocity;
        }

        /// <summary>Sets a body's linear velocity.</summary>
        public static void SetLinearVelocity(IntPtr actor, Vector3 velocity)
        {
            NativeMethods.PxwBodySetLinearVelocity(actor, ref velocity);
        }

        /// <summary>Reads a body's angular velocity.</summary>
        public static Vector3 GetAngularVelocity(IntPtr actor)
        {
            Vector3 velocity;
            NativeMethods.PxwBodyGetAngularVelocity(actor, out velocity);
            return velocity;
        }

        /// <summary>Sets a body's angular velocity.</summary>
        public static void SetAngularVelocity(IntPtr actor, Vector3 velocity)
        {
            NativeMethods.PxwBodySetAngularVelocity(actor, ref velocity);
        }

        /// <summary>Reads a body's mass, in kilograms.</summary>
        public static float GetMass(IntPtr actor)
        {
            return NativeMethods.PxwBodyGetMass(actor);
        }

        /// <summary>Applies a force to an entity's body.</summary>
        public static void AddForce(SimEntity entity, Vector3 force, SimForceMode mode = SimForceMode.Force)
        {
            AddForce(Handle(entity), force, mode);
        }

        /// <summary>Applies a torque to an entity's body.</summary>
        public static void AddTorque(SimEntity entity, Vector3 torque, SimForceMode mode = SimForceMode.Force)
        {
            AddTorque(Handle(entity), torque, mode);
        }

        /// <summary>Reads an entity's world pose.</summary>
        public static SimTransform GetPose(SimEntity entity) { return GetPose(Handle(entity)); }

        /// <summary>Reads an entity's world position.</summary>
        public static Vector3 GetPosition(SimEntity entity) { return GetPosition(Handle(entity)); }

        /// <summary>Reads an entity's world orientation.</summary>
        public static Quaternion GetRotation(SimEntity entity) { return GetRotation(Handle(entity)); }

        /// <summary>Reads an entity's linear velocity.</summary>
        public static Vector3 GetLinearVelocity(SimEntity entity) { return GetLinearVelocity(Handle(entity)); }

        /// <summary>Sets an entity's linear velocity.</summary>
        public static void SetLinearVelocity(SimEntity entity, Vector3 velocity) { SetLinearVelocity(Handle(entity), velocity); }

        /// <summary>Reads an entity's angular velocity.</summary>
        public static Vector3 GetAngularVelocity(SimEntity entity) { return GetAngularVelocity(Handle(entity)); }

        /// <summary>Sets an entity's angular velocity.</summary>
        public static void SetAngularVelocity(SimEntity entity, Vector3 velocity) { SetAngularVelocity(Handle(entity), velocity); }

        /// <summary>Reads an entity's mass, in kilograms.</summary>
        public static float GetMass(SimEntity entity) { return GetMass(Handle(entity)); }

        private static IntPtr Handle(SimEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException("entity");
            }
            return entity.NativeHandle;
        }
    }
}
