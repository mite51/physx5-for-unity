using UNDPWR.Core;

namespace UNDPWR.Rollback
{
    /// <summary>
    /// Supplies the managed state channels the rollback engine captures and restores
    /// alongside the physics blob.
    /// </summary>
    /// <remarks>
    /// The physics snapshot only holds what PhysX can be asked for: pose, velocity, sleep
    /// and articulation state. Everything a game keeps outside PhysX — health, cooldown
    /// timers, scores, the pending action log — would be lost across a rollback unless it
    /// too is captured and restored on the same schedule. A game that wants gameplay state
    /// to survive a rewind implements this and hands it to
    /// <see cref="RollbackEngine.SetStateProvider"/>; a physics-only world leaves it unset.
    /// <para>
    /// The methods run inside the tick loop, several times per frame during a replay, so
    /// they must not allocate. That is why they write and read through
    /// <see cref="SimStateWriter"/> and <see cref="SimStateReader"/> cursors over buffers
    /// the engine owns, rather than returning objects. Both cursors are structs and are
    /// passed by <c>ref</c> so the position and any grown buffer propagate back.
    /// </para>
    /// <para>
    /// The single hard rule: a capture followed by a restore must reproduce exactly the
    /// state that was captured, writing and reading the same fields in the same order.
    /// A field that is captured but not restored, or restored in the wrong order, is a
    /// desync that behaves exactly like a physics determinism bug and is diagnosed the
    /// same painful way.
    /// </para>
    /// </remarks>
    public interface ISimStateProvider
    {
        /// <summary>Writes the entity channel — per-entity managed state, in stable-ID order.</summary>
        void CaptureEntityState(ref SimStateWriter writer);

        /// <summary>Reads the entity channel back, in the same order it was written.</summary>
        void RestoreEntityState(ref SimStateReader reader);

        /// <summary>Writes the game channel — the game mode's own state and action log.</summary>
        void CaptureGameState(ref SimStateWriter writer);

        /// <summary>Reads the game channel back, in the same order it was written.</summary>
        void RestoreGameState(ref SimStateReader reader);
    }
}
