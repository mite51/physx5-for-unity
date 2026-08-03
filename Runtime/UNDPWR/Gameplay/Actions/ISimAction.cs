using UNDPWR.Core;

namespace UNDPWR.Gameplay
{
    /// <summary>
    /// A deterministic gameplay command: spawn something, despawn something, award a point.
    /// </summary>
    /// <remarks>
    /// Actions are how gameplay makes a discrete change to the world at a tick boundary,
    /// rather than reaching into the registry or the pool directly from the middle of an
    /// update. Submitting an action instead of acting immediately keeps every such change on
    /// the same schedule on every peer.
    /// <para>
    /// Note what is <i>not</i> here: there is no <c>Undo</c>. The system this replaces paired
    /// every action with a reversal, and getting those reversals right — a despawn had to
    /// snapshot the object it removed so it could put it back — was its most error-prone
    /// corner. Under this framework a rollback restores all three state channels wholesale,
    /// so an action's effects are undone by the restore and redone by replaying the action.
    /// An action only has to know how to happen, never how to un-happen.
    /// </para>
    /// <para>
    /// <see cref="Execute"/> must be a pure function of the world state and the action's own
    /// fields: it runs once on the original pass and again on every replay, and must do the
    /// same thing each time. <see cref="Serialize"/> and <see cref="Deserialize"/> exist only
    /// for actions scheduled for a future tick, which ride in the game channel until they are
    /// due; a same-tick action is executed and discarded within the tick and is never
    /// serialized.
    /// </para>
    /// </remarks>
    public interface ISimAction
    {
        /// <summary>Applies the command to the world.</summary>
        void Execute(SimContext context);

        /// <summary>Writes the action's fields, for a future-scheduled action in the game channel.</summary>
        void Serialize(ref SimStateWriter writer);

        /// <summary>Reads the action's fields back, in the order they were written.</summary>
        void Deserialize(ref SimStateReader reader);
    }
}
