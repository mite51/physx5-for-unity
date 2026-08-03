namespace UNDPWR.Gameplay
{
    /// <summary>
    /// A participant in the session, distinct from the entity they drive.
    /// </summary>
    /// <remarks>
    /// The distinction is the one the previous system drew between a player and their avatar,
    /// and it matters for the same reasons: a player exists before they have spawned an
    /// avatar and after it has been destroyed, a player may drive different entities over a
    /// match, and a player's <see cref="Slot"/> — not their ID — is what indexes the input
    /// frame, so it must be stable and shared across peers.
    /// <para>
    /// The binding to an entity is a plain field, not captured state, because in the common
    /// case a player is bound to one avatar for the whole session. A game that lets a player
    /// possess different entities over time must make that deterministic itself — typically by
    /// storing the current avatar's ID in its game state and re-applying it — so a rollback
    /// past a possession change restores the right binding.
    /// </para>
    /// </remarks>
    public sealed class SimPlayer
    {
        /// <summary>The player's network identity.</summary>
        public uint PlayerId { get; private set; }

        /// <summary>
        /// The player's fixed slot, assigned in ascending player-ID order at session start.
        /// This is the index into a <see cref="Rollback.SimInputFrame"/>.
        /// </summary>
        public int Slot { get; private set; }

        /// <summary>The stable ID of the entity this player drives, or <see cref="SimGameEntity.NoOwner"/>.</summary>
        public uint EntityId { get; private set; }

        /// <summary>Whether the player currently drives an entity.</summary>
        public bool HasEntity { get { return EntityId != SimGameEntity.NoOwner; } }

        internal SimPlayer(uint playerId, int slot)
        {
            PlayerId = playerId;
            Slot = slot;
            EntityId = SimGameEntity.NoOwner;
        }

        /// <summary>Binds the player to an entity they will drive.</summary>
        public void BindEntity(uint entityId)
        {
            EntityId = entityId;
        }

        /// <summary>Clears the player's entity binding.</summary>
        public void UnbindEntity()
        {
            EntityId = SimGameEntity.NoOwner;
        }
    }
}
