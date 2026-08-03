using System;
using System.Collections.Generic;
using UNDPWR.Diagnostics;
using UNDPWR.Rollback;

namespace UNDPWR.Gameplay
{
    /// <summary>
    /// The players in a session and the entities they drive, and the fan-out of a tick's
    /// input frame to those entities.
    /// </summary>
    /// <remarks>
    /// Its one job in the tick loop is <see cref="DistributeInputs"/>: take the frame the
    /// engine hands the host and set each player's input on the entity that player drives, so
    /// an entity's <c>OnSimUpdate</c> reads its controller's input without knowing anything
    /// about players or the network. An entity with no player behind it is simply never
    /// written to and keeps the neutral input the host cleared it to.
    /// </remarks>
    public sealed class SimPlayerRegistry
    {
        private readonly Dictionary<uint, SimPlayer> _byId = new Dictionary<uint, SimPlayer>();
        private readonly List<SimPlayer> _all = new List<SimPlayer>();

        /// <summary>How many players are registered.</summary>
        public int Count { get { return _all.Count; } }

        /// <summary>Every player, in the order they were added.</summary>
        public IReadOnlyList<SimPlayer> All { get { return _all; } }

        /// <summary>Adds a player at a slot.</summary>
        public SimPlayer Add(uint playerId, int slot)
        {
            if (_byId.ContainsKey(playerId))
            {
                throw new InvalidOperationException(string.Format("Player {0} is already registered", playerId));
            }
            SimPlayer player = new SimPlayer(playerId, slot);
            _byId.Add(playerId, player);
            _all.Add(player);
            return player;
        }

        /// <summary>Removes a player.</summary>
        public bool Remove(uint playerId)
        {
            SimPlayer player;
            if (!_byId.TryGetValue(playerId, out player))
            {
                return false;
            }
            _byId.Remove(playerId);
            _all.Remove(player);
            return true;
        }

        /// <summary>Looks up a player by ID.</summary>
        public bool TryGet(uint playerId, out SimPlayer player)
        {
            return _byId.TryGetValue(playerId, out player);
        }

        /// <summary>
        /// Sets each player's input for this tick on the entity that player drives.
        /// </summary>
        /// <remarks>
        /// Iterates the frame by slot so the order is the frame's fixed slot order, though
        /// order does not actually matter here because each input lands on a different
        /// entity. A player with no bound entity, or one bound to an ID that is not
        /// registered, is skipped.
        /// </remarks>
        public void DistributeInputs(SimInputFrame frame, SimObjectRegistry entities)
        {
            for (int slot = 0; slot < frame.PlayerCount; ++slot)
            {
                SimInput input = frame[slot];
                SimPlayer player;
                if (!_byId.TryGetValue(input.PlayerId, out player) || !player.HasEntity)
                {
                    continue;
                }
                SimGameEntity entity;
                if (entities.TryGet(player.EntityId, out entity))
                {
                    entity.SetInput(input);
                }
            }
        }
    }
}
