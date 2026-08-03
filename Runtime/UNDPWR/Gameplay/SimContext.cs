using UNDPWR.Core;

namespace UNDPWR.Gameplay
{
    /// <summary>
    /// The services a piece of gameplay reaches for during a tick, gathered in one place.
    /// </summary>
    /// <remarks>
    /// The original system routed everything through a single <c>Arbiter.Instance</c>
    /// god-object that also owned the netcode, the tick loop and the transport. This is the
    /// same convenience without the coupling: a plain container of the things entities,
    /// actions and game modes legitimately need — the world, the registry, scene queries,
    /// contacts, the ID allocator — handed to them rather than reached through a static.
    /// It holds references and the current tick, and no logic; nothing here decides anything.
    /// <para>
    /// <see cref="CurrentTick"/> and <see cref="IsReplay"/> are updated by
    /// <see cref="SimGameHost"/> as it steps and replays, so gameplay reads the tick it is
    /// simulating rather than a wall-clock frame counter. Reading either from outside the
    /// tick loop is meaningless.
    /// </para>
    /// </remarks>
    public sealed class SimContext
    {
        /// <summary>The deterministic world being simulated.</summary>
        public DeterministicWorld World { get; private set; }

        /// <summary>The registry of gameplay entities, iterable in stable-ID order.</summary>
        public SimObjectRegistry Registry { get; private set; }

        /// <summary>Deterministic scene queries against the world.</summary>
        public SimQuery Query { get; private set; }

        /// <summary>The contact and trigger events of the most recent step.</summary>
        public SimContacts Contacts { get; private set; }

        /// <summary>The stable-ID allocator for anything spawned during the session.</summary>
        public StableIdAllocator Ids { get; private set; }

        /// <summary>The entity pool spawns and despawns draw from. Assigned by the host.</summary>
        public SimEntityPool Pool { get; internal set; }

        /// <summary>The tick currently being simulated. Set by the host.</summary>
        public int CurrentTick { get; internal set; }

        /// <summary>
        /// True while a tick is being resimulated after a rollback. Use it to suppress
        /// one-shot presentation effects, never to change what the simulation does.
        /// </summary>
        public bool IsReplay { get; internal set; }

        /// <summary>
        /// The action queue, for submitting spawns, despawns and game effects. Assigned by
        /// the host once the queue exists.
        /// </summary>
        public SimActionQueue Actions { get; internal set; }

        /// <summary>The active game mode. Assigned by the host at session start.</summary>
        public ISimGameMode GameMode { get; internal set; }

        /// <summary>Creates a context over the core services.</summary>
        public SimContext(DeterministicWorld world, SimObjectRegistry registry, StableIdAllocator ids)
        {
            World = world;
            Registry = registry;
            Ids = ids;
            Query = new SimQuery(world);
            Contacts = new SimContacts(world);
            CurrentTick = -1;
            IsReplay = false;
        }
    }
}
