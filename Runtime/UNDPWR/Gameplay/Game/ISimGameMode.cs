using UNDPWR.Interop;

namespace UNDPWR.Gameplay
{
    /// <summary>
    /// The rules of a match: what happens at the start and end of every tick, how players
    /// and teams enter and leave, what a contact means, and — through
    /// <see cref="ISimGameState"/> — the state all of that reads and writes.
    /// </summary>
    /// <remarks>
    /// This is the single seam a game plugs into. The framework owns the tick loop, the
    /// rollback, the entities and the pool; the game mode owns the meaning laid over them.
    /// It is called by <see cref="SimGameHost"/> at fixed points in a fixed order every tick,
    /// so two peers running the same game mode over the same inputs make the same decisions.
    /// <para>
    /// Because it also <i>is</i> the game channel (it extends <see cref="ISimGameState"/>),
    /// everything a game mode decides is captured and restored with the rest of the
    /// simulation. A score changed in <see cref="OnContact"/> rolls back for free; there is
    /// no separate bookkeeping to keep in step, which is exactly the split the previous
    /// system never resolved.
    /// </para>
    /// <para>
    /// Most games override only a few of these, so <see cref="SimGameModeBase"/> provides
    /// empty implementations to inherit from.
    /// </para>
    /// </remarks>
    public interface ISimGameMode : ISimGameState
    {
        /// <summary>Called once before tick zero, to set up starting state.</summary>
        void OnSessionStart(SimContext context);

        /// <summary>
        /// Called at the very start of a tick, before actions and entities run. The place
        /// for phase timers and anything that must precede gameplay.
        /// </summary>
        void OnTickBegin(int tick);

        /// <summary>
        /// Called after entities have updated but before the physics step, for logic driven
        /// by overlap and trigger volumes — capture zones, pickups, kill planes.
        /// </summary>
        void OnResolveVolumes(int tick);

        /// <summary>Called at the very end of a tick, after the step and contact drain.</summary>
        void OnTickEnd(int tick);

        /// <summary>
        /// Called once per contact reported by the step, in the deterministic order the
        /// native layer sorted them into.
        /// </summary>
        void OnContact(SimContext context, SimContactEvent contact);

        /// <summary>Called once per trigger-overlap change reported by the step.</summary>
        void OnTrigger(SimContext context, SimTriggerEvent trigger);

        /// <summary>Called when a player joins the session.</summary>
        void OnPlayerJoined(SimPlayer player);

        /// <summary>Called when a player leaves the session.</summary>
        void OnPlayerLeft(SimPlayer player);
    }

    /// <summary>
    /// An <see cref="ISimGameMode"/> with empty hooks and no game state, to inherit from and
    /// override only what a game needs.
    /// </summary>
    /// <remarks>
    /// The two state methods do nothing, which is correct for a mode with no state of its
    /// own; a mode that tracks scores or a phase overrides them to write and read those.
    /// </remarks>
    public abstract class SimGameModeBase : ISimGameMode
    {
        /// <inheritdoc/>
        public virtual void OnSessionStart(SimContext context) { }

        /// <inheritdoc/>
        public virtual void OnTickBegin(int tick) { }

        /// <inheritdoc/>
        public virtual void OnResolveVolumes(int tick) { }

        /// <inheritdoc/>
        public virtual void OnTickEnd(int tick) { }

        /// <inheritdoc/>
        public virtual void OnContact(SimContext context, SimContactEvent contact) { }

        /// <inheritdoc/>
        public virtual void OnTrigger(SimContext context, SimTriggerEvent trigger) { }

        /// <inheritdoc/>
        public virtual void OnPlayerJoined(SimPlayer player) { }

        /// <inheritdoc/>
        public virtual void OnPlayerLeft(SimPlayer player) { }

        /// <inheritdoc/>
        public virtual void CaptureGameState(ref Core.SimStateWriter writer) { }

        /// <inheritdoc/>
        public virtual void RestoreGameState(ref Core.SimStateReader reader) { }
    }
}
