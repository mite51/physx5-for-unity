using System;
using System.Collections.Generic;
using UNDPWR.Core;
using UNDPWR.Diagnostics;
using UNDPWR.Interop;
using UNDPWR.Rollback;

namespace UNDPWR.Gameplay
{
    /// <summary>
    /// The one object that drives a game over the rollback engine: the single step handler
    /// and the single state provider, owning the per-tick order every peer must share.
    /// </summary>
    /// <remarks>
    /// There is exactly one <see cref="ISimStepHandler"/> for the whole game, and this is it,
    /// on purpose. If entities, the game mode and the action queue each registered their own
    /// handler, the order they ran in would depend on registration order, and a difference in
    /// that order between peers is a desync. Funnelling everything through one handler makes
    /// the order a single, readable sequence:
    /// <list type="number">
    /// <item><description>the game mode's tick-begin hook;</description></item>
    /// <item><description>this tick's inputs distributed to their entities;</description></item>
    /// <item><description>actions already due this tick;</description></item>
    /// <item><description>entities updated in stable-ID order;</description></item>
    /// <item><description>the game mode's volume resolution;</description></item>
    /// <item><description>late actions the updates just submitted;</description></item>
    /// <item><description>the physics step (run by the engine);</description></item>
    /// <item><description>the contact and trigger drain, then the tick-end hook.</description></item>
    /// </list>
    /// It is also the <see cref="ISimStateProvider"/>: the entity channel is the registry in
    /// stable-ID order, and the game channel is the action queue followed by the game mode's
    /// own state. So the same object that decides the order also decides what is captured, and
    /// the two can never drift apart.
    /// </remarks>
    public sealed class SimGameHost : ISimStepHandler, ISimStateProvider, ISimAuthoritativeEventHandler
    {
        private readonly RollbackEngine _engine;
        private readonly SimContext _context;
        private readonly SimObjectRegistry _registry;
        private readonly SimEntityPool _pool;
        private readonly SimActionQueue _actions;
        private readonly SimPlayerRegistry _players;
        private ISimGameMode _gameMode;
        private bool _begun;

        private SimContactEvent[] _contactScratch = new SimContactEvent[256];
        private SimTriggerEvent[] _triggerScratch = new SimTriggerEvent[128];

        // A drain that fills its buffer exactly may have had more to give. The native drain
        // is idempotent within a step, so the buffer can be grown and the same step re-drained
        // until it fits, up to this cap; past it, the overflow is warned rather than silently
        // dropped. Truncation is deterministic (the front of the sorted list), so an overflow
        // is a completeness problem, never a desync.
        private const int MaxEventScratch = 8192;
        private bool _contactOverflowWarned;
        private bool _triggerOverflowWarned;

        /// <summary>Creates a host over a world, engine and ID allocator.</summary>
        public SimGameHost(DeterministicWorld world, RollbackEngine engine, StableIdAllocator ids)
        {
            if (world == null)
            {
                throw new ArgumentNullException("world");
            }
            if (engine == null)
            {
                throw new ArgumentNullException("engine");
            }
            if (ids == null)
            {
                throw new ArgumentNullException("ids");
            }

            _engine = engine;
            _registry = new SimObjectRegistry();
            _context = new SimContext(world, _registry, ids);
            _pool = new SimEntityPool(_context);
            _actions = new SimActionQueue();
            _players = new SimPlayerRegistry();

            _context.Pool = _pool;
            _context.Actions = _actions;
            _actions.Attach(_context);
        }

        /// <summary>The shared services, for gameplay setup code.</summary>
        public SimContext Context { get { return _context; } }

        /// <summary>The entity registry.</summary>
        public SimObjectRegistry Registry { get { return _registry; } }

        /// <summary>The entity pool, to configure with <see cref="SimEntityPool.Add"/> before <see cref="Begin"/>.</summary>
        public SimEntityPool Pool { get { return _pool; } }

        /// <summary>The action queue, to register action types with before <see cref="Begin"/>.</summary>
        public SimActionQueue Actions { get { return _actions; } }

        /// <summary>The player registry, to add players to before <see cref="Begin"/>.</summary>
        public SimPlayerRegistry Players { get { return _players; } }

        /// <summary>Sets the game mode. Required before <see cref="Begin"/>.</summary>
        public void SetGameMode(ISimGameMode mode)
        {
            _gameMode = mode;
            _context.GameMode = mode;
        }

        /// <summary>
        /// Finishes setup and initialises tick zero: preregisters the pool, commits it,
        /// disables the dormant instances, starts the game mode, then wires this host into
        /// the engine as its handler and state provider and captures the first snapshot.
        /// </summary>
        public void Begin()
        {
            if (_begun)
            {
                throw new InvalidOperationException("Begin was already called");
            }
            if (_gameMode == null)
            {
                throw new InvalidOperationException("A game mode must be set before Begin");
            }
            _begun = true;

            _pool.Preregister();
            _context.World.CommitPending();
            _pool.DisableAllInitially();

            _gameMode.OnSessionStart(_context);

            _engine.SetStateProvider(this);
            _engine.AddHandler(this);
            _engine.AddEventHandler(this);
            _engine.Initialise();

            SimLog.Info(string.Format("Game host begun: {0} entities, {1} players",
                _registry.Count, _players.Count));
        }

        /// <summary>Injects a server-assigned action immediately before its simulation tick.</summary>
        public void OnAuthoritativeEvent(SimAuthoritativeEvent command, bool isReplay)
        {
            _actions.SubmitNetworkAction(command.TypeId, command.Payload, command.Tick);
        }

        /// <summary>Adds a player and notifies the game mode.</summary>
        public SimPlayer AddPlayer(uint playerId, int slot)
        {
            SimPlayer player = _players.Add(playerId, slot);
            if (_gameMode != null)
            {
                _gameMode.OnPlayerJoined(player);
            }
            return player;
        }

        /// <summary>Removes a player and notifies the game mode.</summary>
        public bool RemovePlayer(uint playerId)
        {
            SimPlayer player;
            if (!_players.TryGet(playerId, out player))
            {
                return false;
            }
            if (_gameMode != null)
            {
                _gameMode.OnPlayerLeft(player);
            }
            return _players.Remove(playerId);
        }

        void ISimStepHandler.OnBeforeStep(DeterministicWorld world, int tick, SimInputFrame inputs, bool isReplay)
        {
            _context.CurrentTick = tick;
            _context.IsReplay = isReplay;

            _gameMode.OnTickBegin(tick);

            // Inputs to entities: clear every entity to neutral, then let the player bindings
            // overwrite the ones they control. An AI entity keeps neutral input and drives
            // itself from its own state in OnSimUpdate.
            IReadOnlyList<SimGameEntity> ordered = _registry.Ordered;
            for (int i = 0; i < ordered.Count; ++i)
            {
                ordered[i].ClearInput();
            }
            _players.DistributeInputs(inputs, _registry);

            // Actions already scheduled for this tick, before the updates run.
            _actions.ExecuteDue(tick, _context);

            // Entities, in stable-ID order, so every peer runs them identically.
            for (int i = 0; i < ordered.Count; ++i)
            {
                SimGameEntity entity = ordered[i];
                if (entity.IsActive)
                {
                    entity.OnSimUpdate(tick, isReplay);
                }
            }

            _gameMode.OnResolveVolumes(tick);

            // Late actions: anything the updates or the volume pass just submitted for this
            // tick. ExecuteDue only runs entries whose tick matches, so future-scheduled
            // actions stay put.
            _actions.ExecuteDue(tick, _context);
        }

        void ISimStepHandler.OnAfterStep(DeterministicWorld world, int tick, bool isReplay)
        {
            int contactCount = _context.Contacts.Drain(_contactScratch);
            while (contactCount == _contactScratch.Length && _contactScratch.Length < MaxEventScratch)
            {
                _contactScratch = new SimContactEvent[_contactScratch.Length * 2];
                contactCount = _context.Contacts.Drain(_contactScratch);
            }
            if (contactCount == _contactScratch.Length && !_contactOverflowWarned)
            {
                _contactOverflowWarned = true;
                SimLog.Warning(string.Format(
                    "Contact events hit the {0}-event cap this step; extra contacts were dropped. " +
                    "This is deterministic across peers but incomplete.", MaxEventScratch));
            }
            for (int i = 0; i < contactCount; ++i)
            {
                _gameMode.OnContact(_context, _contactScratch[i]);
            }

            int triggerCount = _context.Contacts.DrainTriggers(_triggerScratch);
            while (triggerCount == _triggerScratch.Length && _triggerScratch.Length < MaxEventScratch)
            {
                _triggerScratch = new SimTriggerEvent[_triggerScratch.Length * 2];
                triggerCount = _context.Contacts.DrainTriggers(_triggerScratch);
            }
            if (triggerCount == _triggerScratch.Length && !_triggerOverflowWarned)
            {
                _triggerOverflowWarned = true;
                SimLog.Warning(string.Format(
                    "Trigger events hit the {0}-event cap this step; extra triggers were dropped. " +
                    "This is deterministic across peers but incomplete.", MaxEventScratch));
            }
            for (int i = 0; i < triggerCount; ++i)
            {
                _gameMode.OnTrigger(_context, _triggerScratch[i]);
            }

            _gameMode.OnTickEnd(tick);
        }

        void ISimStateProvider.CaptureEntityState(ref SimStateWriter writer)
        {
            _registry.CaptureAll(ref writer);
        }

        void ISimStateProvider.RestoreEntityState(ref SimStateReader reader)
        {
            _registry.RestoreAll(ref reader);
        }

        void ISimStateProvider.CaptureGameState(ref SimStateWriter writer)
        {
            // Order fixed: the action queue first, then the game mode's own state. Restore
            // reads them back in the same order.
            _actions.CaptureState(ref writer);
            _gameMode.CaptureGameState(ref writer);
        }

        void ISimStateProvider.RestoreGameState(ref SimStateReader reader)
        {
            _actions.RestoreState(ref reader);
            _gameMode.RestoreGameState(ref reader);
        }
    }
}
