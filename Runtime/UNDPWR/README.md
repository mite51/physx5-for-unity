# UNDPWR

**Unity Networked Deterministic Physics With Rollback** — rollback netcode for PhysX 5 in
Unity. Peers exchange only player inputs and each recomputes the physics identically, so
bandwidth stays flat as the physics scene grows.

## The one rule everything follows

**Only the confirmed timeline is compared between peers, and under PGS it is a pure function
of the snapshot before each tick.** Every peer runs the same simulation from the same inputs;
prediction and rollback hide latency locally, and a mandatory confirmed-hash check verifies
that peers still agree. Everything else in the framework is a consequence of keeping that
confirmed timeline reproducible. The [manual](Documentation/README.md) explains why and how.

## What it does

- Deterministic rigid bodies, articulations and vehicles over PhysX 5.
- Free-running prediction with rollback that fires only when a prediction was wrong, and only
  as deep as the correction needs.
- Mid-match join and desync recovery through a synchronised rebuild.
- Config-hash handshake and per-tick hash comparison so a mismatch is caught at join or named
  when it happens.

You supply the transport (a thin `ISimTransport`); the framework never puts simulation state
on the wire. Matchmaking, rendering and cross-CPU-architecture play are out of scope — see
[Limits and platforms](Documentation/LimitsAndPlatforms.md).

## Quick start

The recommended path is the gameplay layer: a `SimGameHost` drives the engine, and a
`SimSession` drives the network. A minimal networked frame:

```csharp
var config = SimConfig.Deterministic;   // PGS, 60 Hz, delay 2, history 32
SimLog.AttachNativeSink();

var world = new DeterministicWorld(config);
var ids = new StableIdAllocator(sessionSeed);
var engine = new RollbackEngine(world, playerIds);

var host = new SimGameHost(world, engine, ids);
host.Pool.Add("ball", ballPrefab, 32);
host.AddPlayer(localPlayerId, slot: 0);
host.SetGameMode(new MyGameMode());
host.Begin();                            // registers, commits, captures tick 0

var session = new SimSession(engine, transport, config, localPlayerId, playerIds);
session.Start();

// once per FixedUpdate:
session.Pump();                          // drain the network into the engine
session.SubmitLocalInput(SampleInput()); // stamped for engine.LocalInputTick, fills the run
engine.Advance();                        // one simulated tick
session.PublishConfirmed();              // broadcast and check the new confirmed hash
```

New to the framework? Read [Getting started](Documentation/GettingStarted.md), which builds
this up from a physics-only loop.

## Documentation

The [manual](Documentation/README.md) is the full guide: getting started, the concepts behind
rollback, the world and actor model, the gameplay layer, networking, the simulation APIs,
configuration, limits, and troubleshooting.

[CHANGELOG.md](CHANGELOG.md) records what has landed and, critically, every change to the two
numbers that decide whether two peers interoperate: the managed config hash
(`SimConfig.ComputeHash`) and the native snapshot format (`kStateVersion`).
