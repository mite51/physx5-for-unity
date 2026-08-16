using System;

namespace UNDPWR.Rollback
{
    /// <summary>
    /// The agreed state every peer restores during a synchronised rebuild: one confirmed
    /// tick, the roster that resumes from it, and all three snapshot channels captured at
    /// that tick.
    /// </summary>
    /// <remarks>
    /// A mid-match join, a leave, or a desync recovery all resolve to the same thing — put
    /// every peer back on one identical history — and this is the payload that carries it.
    /// It is a plain data holder so it can be produced by <see cref="RollbackEngine.CaptureRebuildState"/>
    /// on the peer that owns the timeline (the host), moved over whatever transport the game
    /// uses, and handed to <see cref="RollbackEngine.PrepareForRebuild(ref SimRebuildState, Action)"/>
    /// on every peer including the joiner.
    /// <para>
    /// The roster is part of the package on purpose. The engine fixes its player set at
    /// construction, and a join or leave changes it, so the set that resumes has to travel
    /// with the snapshot rather than being inferred separately — otherwise two peers would
    /// rebuild the same physics with different input-slot layouts and diverge on the first
    /// tick that reads an input.
    /// </para>
    /// <para>
    /// The byte arrays are the exact channel buffers, meaningful only up to their paired
    /// size. Copy them if they must outlive the call that produced them; the capture path
    /// hands back fresh arrays, so a captured state is safe to hold and serialise.
    /// </para>
    /// </remarks>
    public struct SimRebuildState
    {
        /// <summary>The confirmed tick every peer restores to and resumes from.</summary>
        public int ResumeTick;

        /// <summary>The sorted player-ID set the resumed session runs with.</summary>
        public uint[] PlayerIds;

        /// <summary>The last canonical command held for each player at the resume tick.</summary>
        public SimInput[] LastInputs;

        /// <summary>The proposal sequence associated with each held canonical command.</summary>
        public uint[] LastInputSequences;

        /// <summary>Server-assigned events scheduled strictly after the resume tick.</summary>
        public SimAuthoritativeEvent[] PendingEvents;

        /// <summary>The opaque native physics blob.</summary>
        public byte[] PhysicsData;

        /// <summary>How many bytes of <see cref="PhysicsData"/> are meaningful.</summary>
        public int PhysicsSize;

        /// <summary>The physics channel's native hash, for validating the transfer.</summary>
        public ulong PhysicsHash;

        /// <summary>The entity channel's managed bytes, or empty for a physics-only world.</summary>
        public byte[] EntityData;

        /// <summary>How many bytes of <see cref="EntityData"/> are meaningful.</summary>
        public int EntitySize;

        /// <summary>The game channel's managed bytes, or empty when no game mode is set.</summary>
        public byte[] GameData;

        /// <summary>How many bytes of <see cref="GameData"/> are meaningful.</summary>
        public int GameSize;

        /// <summary>
        /// Copies the meaningful prefix of every channel into freshly sized arrays, so the
        /// result owns its buffers and can outlive the source snapshot.
        /// </summary>
        public SimRebuildState Compact()
        {
            SimRebuildState copy = new SimRebuildState();
            copy.ResumeTick = ResumeTick;
            copy.PhysicsSize = PhysicsSize;
            copy.PhysicsHash = PhysicsHash;
            copy.EntitySize = EntitySize;
            copy.GameSize = GameSize;

            copy.PlayerIds = new uint[PlayerIds == null ? 0 : PlayerIds.Length];
            if (PlayerIds != null)
            {
                Array.Copy(PlayerIds, copy.PlayerIds, PlayerIds.Length);
            }
            copy.LastInputs = new SimInput[LastInputs == null ? 0 : LastInputs.Length];
            if (LastInputs != null)
            {
                Array.Copy(LastInputs, copy.LastInputs, LastInputs.Length);
            }
            copy.LastInputSequences = new uint[
                LastInputSequences == null ? 0 : LastInputSequences.Length];
            if (LastInputSequences != null)
            {
                Array.Copy(LastInputSequences, copy.LastInputSequences, LastInputSequences.Length);
            }
            copy.PendingEvents = new SimAuthoritativeEvent[
                PendingEvents == null ? 0 : PendingEvents.Length];
            if (PendingEvents != null)
            {
                for (int i = 0; i < PendingEvents.Length; ++i)
                {
                    copy.PendingEvents[i] = PendingEvents[i];
                    byte[] payload = PendingEvents[i].Payload ?? new byte[0];
                    copy.PendingEvents[i].Payload = new byte[payload.Length];
                    Array.Copy(payload, copy.PendingEvents[i].Payload, payload.Length);
                }
            }

            copy.PhysicsData = Slice(PhysicsData, PhysicsSize);
            copy.EntityData = Slice(EntityData, EntitySize);
            copy.GameData = Slice(GameData, GameSize);
            return copy;
        }

        private static byte[] Slice(byte[] source, int size)
        {
            byte[] result = new byte[size < 0 ? 0 : size];
            if (source != null && size > 0)
            {
                Buffer.BlockCopy(source, 0, result, 0, size);
            }
            return result;
        }
    }
}
