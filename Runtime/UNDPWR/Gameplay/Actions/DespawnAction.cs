using UNDPWR.Core;

namespace UNDPWR.Gameplay
{
    /// <summary>
    /// Returns an active entity to its pool by stable ID.
    /// </summary>
    /// <remarks>
    /// The counterpart to <see cref="SpawnAction"/>, and just as free to reverse. The system
    /// this replaces had its despawn capture the whole state of the object it removed so an
    /// undo could rebuild it — the single most intricate piece of that code. Here a despawn
    /// only flips an active flag and disables a body; a rollback restores both from the
    /// channels, so the entity comes back exactly as it was with nothing captured here.
    /// </remarks>
    public sealed class DespawnAction : ISimAction
    {
        /// <summary>The stable ID of the entity to despawn.</summary>
        public uint StableId;

        /// <summary>Creates an empty action, for deserialization.</summary>
        public DespawnAction() { }

        /// <summary>Creates a despawn action for an entity.</summary>
        public DespawnAction(uint stableId)
        {
            StableId = stableId;
        }

        /// <inheritdoc/>
        public void Execute(SimContext context)
        {
            context.Pool.Despawn(StableId);
        }

        /// <inheritdoc/>
        public void Serialize(ref SimStateWriter writer)
        {
            writer.WriteUInt(StableId);
        }

        /// <inheritdoc/>
        public void Deserialize(ref SimStateReader reader)
        {
            StableId = reader.ReadUInt();
        }
    }
}
