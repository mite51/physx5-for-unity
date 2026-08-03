using UNDPWR.Core;

namespace UNDPWR.Gameplay
{
    /// <summary>
    /// The managed state an entity keeps outside PhysX and needs to survive a rollback.
    /// </summary>
    /// <remarks>
    /// Pose and velocity are in the physics snapshot already. This is for everything else an
    /// entity carries — health, cooldown timers, an AI target, a grab handle, a jump counter
    /// — none of which PhysX knows about and all of which would otherwise be wrong the moment
    /// a tick is replayed.
    /// <para>
    /// The pair must be exact inverses: whatever <see cref="CaptureEntityState"/> writes,
    /// <see cref="RestoreEntityState"/> reads back in the same order into the same fields.
    /// Because the entity channel is laid out per entity in stable-ID order, a fixed set of
    /// entities writing a fixed set of fields keeps the channel's layout identical every
    /// tick, which is what lets one tick's capture be restored into another.
    /// </para>
    /// </remarks>
    public interface ISimEntityState
    {
        /// <summary>Writes this entity's managed state into the entity channel.</summary>
        void CaptureEntityState(ref SimStateWriter writer);

        /// <summary>Reads this entity's managed state back, in the order it was written.</summary>
        void RestoreEntityState(ref SimStateReader reader);
    }
}
