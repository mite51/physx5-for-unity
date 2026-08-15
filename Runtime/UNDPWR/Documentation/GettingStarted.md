# Getting started

This chapter takes you from nothing to a running deterministic simulation, then to a
networked one. It assumes you know Unity and the PhysX 5 for Unity package; it does not assume
you know rollback netcode. If you want the reasoning behind the moving parts first, read
[Concepts](Concepts.md); otherwise start here and follow the links as they come up.

## Prerequisites

- Unity 2021.3 or newer, with the PhysX 5 for Unity package (`PhysX5ForUnity`) installed and
  its native plugin present.
- A single CPU architecture across all peers. UNDPWR peers must share an architecture; x86 and
  ARM do not agree. See [Limits and platforms](LimitsAndPlatforms.md).

## The two assemblies

UNDPWR ships as two assemblies plus its tests:

| Assembly | Namespace roots | You reference it for |
| --- | --- | --- |
| `UNDPWR` | `UNDPWR.Core`, `UNDPWR.Rollback`, `UNDPWR.Gameplay`, `UNDPWR.Net`, `UNDPWR.Diagnostics` | Everything: the world, the engine, the gameplay layer, networking. Depends only on `PhysX5ForUnity` and Unity math types. |
| `UNDPWR.Unity` | `UNDPWR.Unity` | Bridging Unity PhysX components (`PhysxActor`, `PhysxVehicle`, `PhysxArticulationBody`) into the world, and decoding vehicle commands. |

The core assembly deliberately does not depend on the Unity PhysX component types, which is
what lets it compile and run its determinism tests without opening the editor. The one place
that does depend on them is `UNDPWR.Unity`, so registering a Unity actor goes through
[`SimActorBridge`](../../UNDPWR.Unity/SimActorBridge.cs) — covered in
[World and actors](WorldAndActors.md).

## The fixed-update contract

The whole framework runs on one idea: **the simulation advances exactly one tick per
`Advance()`, and you call `Advance()` once per `FixedUpdate`.** Set Unity's fixed timestep to
match your tick rate (`Time.fixedDeltaTime = config.FixedDeltaTime`) and never touch physics
from `Update`. Everything that affects the simulation happens inside the tick, through the
step handler — never from `Update`, a coroutine, or a collision callback.

## Step 1 — a local, physics-only loop

The smallest useful setup: a world, some actors, an engine, and one step handler. No
networking yet.

```csharp
using UNDPWR.Core;
using UNDPWR.Rollback;
using UNDPWR.Diagnostics;

var config = SimConfig.Deterministic; // PGS solver, 60 Hz, LocalInputDelay 2, history 32
SimLog.AttachNativeSink();            // route PhysX diagnostics through SimLog

var world = new DeterministicWorld(config);
var ids = new StableIdAllocator(sessionSeed);

// Register actors created in world.ScenePtr. Order does not matter; the world
// commits them sorted by stable ID. See WorldAndActors.md.
world.Register(crateId, cratePtr, SimHandleKind.RigidDynamic);
SimMass.Setup(cratePtr, config.DefaultDensity, config.MassIsotropyTolerance);

var engine = new RollbackEngine(world, playerIds);
engine.AddHandler(new MyGameplay());  // an ISimStepHandler; all sim effects go through it
engine.Initialise();                  // commits actors and captures tick 0

int nextLocalTick = engine.CurrentTick;
```

Then, once per `FixedUpdate`:

```csharp
// Submit a RUN of ticks, not a single tick. LocalInputTick starts LocalInputDelay ticks
// ahead of the clock, so covering only one tick opens a permanent gap. See RollbackAndInput.md.
for (; nextLocalTick <= engine.LocalInputTick; ++nextLocalTick)
{
    engine.SubmitInput(SampleLocalInput(nextLocalTick));
}
engine.Advance();
```

`MyGameplay` is your [`ISimStepHandler`](../Rollback/RollbackEngine.cs): it applies forces and
reads state in `OnBeforeStep`, once per tick and again for every replayed tick.
[Rollback and input](RollbackAndInput.md) covers the handler and the input loop in full.

> The single-tick-per-frame mistake is the most common way to break a session. Always submit
> the whole run up to `LocalInputTick`, or let `SimSession.SubmitLocalInput` do it for you.

## Step 2 — add networking

Implement [`ISimTransport`](../Net/ISimTransport.cs) over whatever moves bytes between peers
(UDP, a relay, a mesh). It only has to deliver whole messages best-effort; loss, duplication
and reordering are all tolerated. Then wrap the engine in a `SimSession`:

```csharp
using UNDPWR.Net;

var session = new SimSession(engine, transport, config, localPlayerId, playerIds);
session.Start(); // handshake: broadcasts the config hash and player set
```

The per-`FixedUpdate` loop becomes:

```csharp
session.Pump();                          // remote inputs and hashes into the engine
session.SubmitLocalInput(SampleInput()); // stamp for engine.LocalInputTick; fills the run
engine.Advance();
session.PublishConfirmed();              // publish and check the confirmed-tick hash
```

`SubmitLocalInput` fills the tick run for you, so the bare loop from Step 1 is no longer
needed. [Networking](Networking.md) covers the handshake, desync detection, and mid-match
join.

## Step 3 — the gameplay layer (recommended)

`ISimStepHandler` is the floor. For an actual game — entities that carry state, spawning,
scores, players, camera-relative input, smooth rendering — use the gameplay layer, which wires
all of it into a single `SimGameHost`:

```csharp
using UNDPWR.Gameplay;

var host = new SimGameHost(world, engine, ids);
host.Pool.Add("ball", ballPrefab, 32);   // preregister a pool of an entity prefab
host.AddPlayer(localPlayerId, slot: 0);
host.SetGameMode(new MyGameMode());       // your ISimGameMode
host.Begin();                             // preregisters, commits, captures tick 0
```

`SimGameHost.Begin` registers everything, becomes the engine's single step handler and state
provider, and captures tick zero — so you do not call `engine.AddHandler` or
`engine.Initialise` yourself. The `FixedUpdate` loop is the same networked loop as Step 2.
[The gameplay layer](Gameplay.md) is the full walkthrough.

## Where to go next

- [Concepts](Concepts.md) — what rollback is doing under the hood, and why PGS and cold steps.
- [World and actors](WorldAndActors.md) — registering PhysX actors correctly (the #1 desync source).
- [The gameplay layer](Gameplay.md) — entities, pooling, actions, game modes, presentation.
- [Networking](Networking.md) — transport, handshake, desync, join.
- [Troubleshooting](Troubleshooting.md) — when peers disagree or a peer stalls.
