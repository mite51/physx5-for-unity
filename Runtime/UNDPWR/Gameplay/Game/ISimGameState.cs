using UNDPWR.Core;

namespace UNDPWR.Gameplay
{
    /// <summary>
    /// A game mode's own rollback state: the third channel, alongside physics and entities.
    /// </summary>
    /// <remarks>
    /// Scores, the round timer, the match phase, whose turn it is — everything a game mode
    /// tracks that is neither physics nor per-entity — lives here. It is a first-class
    /// channel, captured, restored and hashed on the same schedule as the other two, not a
    /// thing that is only serialized when a peer joins.
    /// <para>
    /// The original system had this backwards. Its game state was serialized for late join,
    /// but scores rolled back through a separate action-undo path and the two mechanisms
    /// never shared an owner, so a score could be correct for a joiner and wrong after a
    /// rewind, or the reverse. Here there is one channel and one owner: whatever
    /// <see cref="CaptureGameState"/> writes is what a rollback restores and what a joiner
    /// receives, because they are the same bytes.
    /// </para>
    /// <para>
    /// The framework never looks inside. It hands the game mode a
    /// <see cref="SimStateWriter"/> to fill and later a <see cref="SimStateReader"/> to drain,
    /// hashes the bytes for desync detection, and otherwise treats the channel as opaque. The
    /// only contract is the usual one: read back exactly what was written, in the same order.
    /// </para>
    /// </remarks>
    public interface ISimGameState
    {
        /// <summary>Writes the game mode's state into the game channel.</summary>
        void CaptureGameState(ref SimStateWriter writer);

        /// <summary>Reads the game mode's state back, in the order it was written.</summary>
        void RestoreGameState(ref SimStateReader reader);
    }
}
