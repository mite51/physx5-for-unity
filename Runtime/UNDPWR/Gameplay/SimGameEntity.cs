using System;
using UnityEngine;
using UNDPWR.Core;
using UNDPWR.Interop;
using UNDPWR.Rollback;

namespace UNDPWR.Gameplay
{
    /// <summary>
    /// A gameplay object that lives in the deterministic world: a stable ID, a physics
    /// body, the managed state that rolls back with it, and the per-tick callbacks that
    /// drive it.
    /// </summary>
    /// <remarks>
    /// This is a <c>MonoBehaviour</c> so it can sit on a pooled prefab alongside its PhysX
    /// actor and its visuals, but its simulation is driven entirely by the framework, never
    /// by Unity's <c>Update</c>. Everything that affects the simulation happens in
    /// <see cref="OnSimUpdate"/>, which the host calls once per tick in stable-ID order and
    /// again for every replayed tick. Reading <c>Time.deltaTime</c>, <c>UnityEngine.Random</c>
    /// or input outside <see cref="Input"/> from inside a sim callback is a determinism bug.
    /// <para>
    /// A concrete entity supplies three things: how to find its native body
    /// (<see cref="ResolveNativeHandle"/>), what its rollback state is
    /// (<see cref="CaptureState"/> / <see cref="RestoreState"/>), and what it does each tick
    /// (<see cref="OnSimUpdate"/>). The framework handles identity, registration, the active
    /// flag and its place in the entity channel.
    /// </para>
    /// <para>
    /// Presentation is kept strictly downstream. The component itself stays enabled even
    /// while the entity is pooled out, so it keeps its slot in the tick loop; only the
    /// optional <see cref="presentationRoot"/> is toggled with the active flag. Nothing a
    /// renderer or animator does may feed back into a sim callback.
    /// </para>
    /// </remarks>
    public abstract class SimGameEntity : MonoBehaviour, ISimEntityState
    {
        [SerializeField]
        [Tooltip("Optional visual root toggled with the entity's active state. The entity " +
                 "component itself stays enabled so it keeps receiving sim callbacks.")]
        private GameObject presentationRoot;

        /// <summary>The stable-ID value meaning "no owner".</summary>
        public const uint NoOwner = uint.MaxValue;

        /// <summary>The identity every peer knows this entity by.</summary>
        public uint StableId { get; private set; }

        /// <summary>
        /// Who spawned this entity, as set by the spawning action just before
        /// <see cref="OnSimSpawn"/>, or <see cref="NoOwner"/>.
        /// </summary>
        /// <remarks>
        /// A convenience for the common projectile-and-owner case. The spawn action sets it
        /// on every pass, including replays, so a subclass may read it in
        /// <see cref="OnSimSpawn"/>; if the owner must persist past spawn, copy it into the
        /// subclass's own captured state.
        /// </remarks>
        public uint SpawnOwner { get; internal set; }

        /// <summary>The registry record for this entity's physics body.</summary>
        public SimEntity Registration { get; private set; }

        /// <summary>The services this entity reaches for during a tick.</summary>
        public SimContext Context { get; private set; }

        /// <summary>Whether the entity is currently spawned and simulating.</summary>
        public bool IsActive { get; private set; }

        /// <summary>True once the entity has been registered with the world.</summary>
        public bool IsBound { get { return Registration != null; } }

        /// <summary>The native <c>PxActor</c> handle, for <see cref="SimBody"/> calls.</summary>
        public IntPtr Body { get { return Registration.NativeHandle; } }

        /// <summary>
        /// This tick's input for the entity. Set by the player binding for a controlled
        /// entity, or left neutral for an AI-driven one, which computes its own behaviour.
        /// </summary>
        protected SimInput Input;

        /// <summary>
        /// Returns the native body handle and its kind, so the pool can register the entity.
        /// </summary>
        /// <remarks>
        /// The framework does not assume which PhysX actor component a game uses, so the
        /// concrete entity provides the handle. A typical implementation returns the pointer
        /// from its <c>PhysxRigidActor</c> and <see cref="SimHandleKind.RigidDynamic"/>.
        /// </remarks>
        public abstract IntPtr ResolveNativeHandle(out SimHandleKind kind);

        internal void Bind(uint id, SimEntity registration, SimContext context)
        {
            StableId = id;
            Registration = registration;
            Context = context;
            IsActive = false;
            SpawnOwner = NoOwner;
            ApplyPresentation(false);
        }

        /// <summary>Sets this tick's input, called by the player binding before the update pass.</summary>
        public void SetInput(SimInput input)
        {
            Input = input;
        }

        /// <summary>Resets the input to neutral, called by the host before the update pass.</summary>
        public void ClearInput()
        {
            Input = SimInput.Neutral(0, Context != null ? Context.CurrentTick : 0);
        }

        internal void Activate(int tick)
        {
            IsActive = true;
            ApplyPresentation(true);
            OnSimSpawn(tick);
        }

        internal void Deactivate(int tick)
        {
            OnSimDespawn(tick);
            IsActive = false;
            ApplyPresentation(false);
        }

        /// <summary>Called once when the entity is spawned from the pool.</summary>
        /// <remarks>
        /// Reset the entity's managed state to its starting values here. Fires again on a
        /// replayed spawn, so keep any presentation-only effect behind
        /// <see cref="SimContext.IsReplay"/>.
        /// </remarks>
        public virtual void OnSimSpawn(int tick) { }

        /// <summary>Called once per tick while the entity is active, and on every replay.</summary>
        /// <param name="tick">The tick being simulated.</param>
        /// <param name="isReplay">True when resimulating after a rollback.</param>
        public virtual void OnSimUpdate(int tick, bool isReplay) { }

        /// <summary>Called once when the entity is despawned back into the pool.</summary>
        public virtual void OnSimDespawn(int tick) { }

        void ISimEntityState.CaptureEntityState(ref SimStateWriter writer)
        {
            writer.WriteBool(IsActive);
            CaptureState(ref writer);
        }

        void ISimEntityState.RestoreEntityState(ref SimStateReader reader)
        {
            bool active = reader.ReadBool();
            if (active != IsActive)
            {
                // A restore is not a spawn: it silently re-establishes the active flag and
                // its presentation. The spawn or despawn action that originally changed the
                // flag re-runs during the forward replay and fires the lifecycle callbacks.
                IsActive = active;
                ApplyPresentation(active);
            }
            RestoreState(ref reader);
        }

        /// <summary>Writes the entity's rollback state after the active flag.</summary>
        protected abstract void CaptureState(ref SimStateWriter writer);

        /// <summary>Reads the entity's rollback state back, in the order it was written.</summary>
        protected abstract void RestoreState(ref SimStateReader reader);

        private void ApplyPresentation(bool active)
        {
            if (presentationRoot != null)
            {
                presentationRoot.SetActive(active);
            }
        }
    }
}
