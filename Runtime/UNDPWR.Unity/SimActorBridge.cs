using System;
using UnityEngine;
using PhysX5ForUnity;
using UNDPWR.Core;
using UNDPWR.Interop;

namespace UNDPWR.Unity
{
    /// <summary>
    /// Bridges Unity's PhysX 5 actor components into a <see cref="DeterministicWorld"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="DeterministicWorld.Register"/> takes a raw native handle and a
    /// <see cref="SimHandleKind"/> on purpose, so the core never depends on the Unity
    /// component types — that decoupling is what lets the whole core compile and its
    /// determinism run without an editor. This bridge is the one place that does depend on
    /// them: it maps a <see cref="PhysxActor"/> to the handle and kind the registry expects,
    /// which is why it lives in the integration assembly rather than the core.
    /// <para>
    /// The handle is not always the component's <c>NativeObjectPtr</c>. An articulation
    /// registers its <c>PxArticulationReducedCoordinate</c> (the root's
    /// <see cref="PhysxArticulationBody.GetArticulation"/>), not a link's <c>PxActor</c>, and
    /// a vehicle registers its <c>PxwVehicle</c> (<see cref="PhysxVehicle.VehiclePtr"/>), not
    /// the chassis actor. Resolving that is exactly why the mapping lives here rather than
    /// being inlined at each call site.
    /// </para>
    /// <para>
    /// Two lifecycle rules the caller owns, because this bridge cannot enforce them: the
    /// actor must have been created in the world's scene
    /// (<see cref="DeterministicWorld.ScenePtr"/>), and it must not have been added to that
    /// scene through the Unity component's own <c>OnEnable</c> path — the world adds it, in
    /// stable-ID order, when it commits its pending registrations. Adding it twice, or in a
    /// different order on another peer, is the classic desync. See Gameplay.md for the worked
    /// setup.
    /// </para>
    /// </remarks>
    public static class SimActorBridge
    {
        /// <summary>
        /// Builds a <see cref="PhysxScene"/> that points at a
        /// <see cref="DeterministicWorld"/>'s native scene, for handing to
        /// <see cref="PhysxActor.Scene"/> when the actor is created under external membership.
        /// </summary>
        /// <remarks>
        /// A <see cref="PhysxVehicle"/> and a <see cref="PhysxDynamicRigidActor"/> create their
        /// native object against <c>Scene.NativeObjectPtr</c>, so a component built into a
        /// deterministic world needs a <see cref="PhysxScene"/> whose handle is the world's, not
        /// a second scene of its own. This returns exactly that: a lightweight
        /// <see cref="PhysxScene"/> instance whose <see cref="PhysxScene.NativeObjectPtr"/> is
        /// the world's scene.
        /// <para>
        /// The returned scene is a view, not an owner. Assign it together with
        /// <see cref="PhysxActor.externalSceneMembership"/> set to <c>true</c>, so the component
        /// never routes through the scene's reference count and so can never release the world's
        /// scene. The world owns the scene's lifetime; disposing the world frees it.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">The world was null.</exception>
        /// <exception cref="InvalidOperationException">The world has no native scene.</exception>
        public static PhysxScene CreateWorldScene(DeterministicWorld world)
        {
            if (world == null)
            {
                throw new ArgumentNullException("world");
            }
            IntPtr scenePtr = world.ScenePtr;
            if (scenePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "The world has no native scene; create the DeterministicWorld before binding a PhysxScene to it.");
            }
            PhysxScene scene = ScriptableObject.CreateInstance<PhysxScene>();
            scene.NativeObjectPtr = scenePtr;
            return scene;
        }

        /// <summary>
        /// Maps a Unity PhysX actor component to the native handle and kind the registry
        /// wants, without registering it.
        /// </summary>
        /// <returns>
        /// False when the component has no usable handle yet (not created, or an articulation
        /// link that is not the root), in which case the out parameters are left at their
        /// defaults.
        /// </returns>
        public static bool TryResolveHandle(PhysxActor actor, out IntPtr handle, out SimHandleKind kind)
        {
            handle = IntPtr.Zero;
            kind = SimHandleKind.RigidDynamic;

            if (actor == null)
            {
                return false;
            }

            // Vehicle and articulation carry a handle that is not the PhysxActor's own
            // NativeObjectPtr, so they are resolved before the rigid-actor cases. A
            // PhysxArticulationBody also *is* a PhysxDynamicRigidActor, so its case has to
            // come first or it would be misclassified as a plain dynamic body.
            PhysxVehicle vehicle = actor as PhysxVehicle;
            if (vehicle != null)
            {
                if (!vehicle.IsFinalized || vehicle.VehiclePtr == IntPtr.Zero)
                {
                    return false;
                }
                handle = vehicle.VehiclePtr;
                kind = SimHandleKind.Vehicle;
                return true;
            }

            PhysxArticulationBody articulation = actor as PhysxArticulationBody;
            if (articulation != null)
            {
                // Only the root owns the PxArticulationReducedCoordinate; a link resolves
                // through it and is not registered on its own.
                if (!articulation.IsRoot)
                {
                    return false;
                }
                handle = articulation.GetArticulation();
                if (handle == IntPtr.Zero)
                {
                    return false;
                }
                kind = SimHandleKind.Articulation;
                return true;
            }

            if (actor is PhysxStaticRigidActor)
            {
                kind = SimHandleKind.RigidStatic;
            }
            else if (actor is PhysxKinematicRigidActor)
            {
                kind = SimHandleKind.RigidKinematic;
            }
            else if (actor is PhysxRigidActor)
            {
                kind = SimHandleKind.RigidDynamic;
            }
            else
            {
                return false;
            }

            handle = actor.NativeObjectPtr;
            return handle != IntPtr.Zero;
        }

        /// <summary>
        /// Registers a Unity PhysX actor into the world under a stable ID, resolving its
        /// handle and kind automatically.
        /// </summary>
        /// <param name="world">The world to register into.</param>
        /// <param name="stableId">
        /// The identity every peer knows this object by. Must come from content authoring or a
        /// stable-ID allocator, never from spawn order.
        /// </param>
        /// <param name="actor">The vehicle, articulation root, or rigid actor to register.</param>
        /// <returns>The registered entity. Its <see cref="SimEntity.UserData"/> is set to the actor.</returns>
        /// <exception cref="ArgumentNullException">The world or actor was null.</exception>
        /// <exception cref="InvalidOperationException">
        /// The actor has no usable native handle yet — it has not been created, or it is an
        /// articulation link rather than the root.
        /// </exception>
        public static SimEntity Register(DeterministicWorld world, uint stableId, PhysxActor actor)
        {
            if (world == null)
            {
                throw new ArgumentNullException("world");
            }
            if (actor == null)
            {
                throw new ArgumentNullException("actor");
            }

            IntPtr handle;
            SimHandleKind kind;
            if (!TryResolveHandle(actor, out handle, out kind))
            {
                throw new InvalidOperationException(string.Format(
                    "Cannot register '{0}' (id {1}): it has no native handle yet. Create the actor in the " +
                    "world's scene first, and register the articulation root rather than a link.",
                    actor.name, stableId));
            }

            SimEntity entity = world.Register(stableId, handle, kind);
            entity.UserData = actor;
            return entity;
        }
    }
}
