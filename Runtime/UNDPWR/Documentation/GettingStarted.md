# Getting started

UNDPWR runs a deterministic PhysX world on one headless authoritative server and predictive
clients. This guide shows the shared simulation setup and the two network roles.

## Prerequisites

- Unity with PhysX 5 for Unity and the native plugin.
- The same CPU architecture and content build on server and clients.
- An authenticated transport with unreliable and reliable-ordered delivery.
- `Time.fixedDeltaTime` equal to `SimConfig.FixedDeltaTime`.

## Shared simulation setup

All roles build the same world, pools, player roster and action registration:

```csharp
var simulation = SimConfig.Deterministic;
var network = SimNetConfig.Authoritative;

var world = new DeterministicWorld(simulation);
var ids = new StableIdAllocator(sessionSeed);
var engine = new RollbackEngine(world, playerIds, network);

var host = new SimGameHost(world, engine, ids);
host.Pool.Add("ball", ballPrefab, 32);
host.Actions.RegisterActionType(() => new SpawnAction());
host.Actions.RegisterActionType(() => new DespawnAction());
for (int i = 0; i < playerIds.Count; ++i)
    host.AddPlayer(playerIds[i], i);
host.SetGameMode(new MyGameMode());
host.Begin();
```

`host.Begin()` commits actors in stable-ID order, installs the gameplay/state providers and
captures tick zero. Server and clients must produce the same construction hash.

## Transport

Implement:

```csharp
public interface ISimTransport
{
    uint LocalPeerId { get; }
    void Send(uint recipientId, byte[] data, int offset, int length, SimDelivery delivery);
    bool TryReceive(out SimTransportMessage message);
}
```

Peer ID `0` belongs to the server. Client transport IDs equal their player IDs. Sender identity
must come from the authenticated connection.

## Server

```csharp
var session = new SimServerSession(
    engine, transport, simulation, network, playerIds, host.Actions);

void FixedUpdate()
{
    long now = MonotonicMicroseconds();
    session.Pump(now);
    session.Advance();
}
```

`Advance` finalizes the next canonical input frame, runs the authoritative simulation and
publishes the frame plus confirmed hashes.

## Client

```csharp
var session = new SimClientSession(
    engine, transport, simulation, network,
    localPlayerId, playerIds, host.Actions);
session.Start(MonotonicMicroseconds());

void FixedUpdate()
{
    long now = MonotonicMicroseconds();
    session.Pump(now);

    SimInput input = SimInputEncoder.BuildInput(
        localPlayerId, tick: 0, buttons, rawMove, inputFrame);
    session.SubmitLocalInput(input, now); // session assigns the requested server tick
    session.Advance();                    // bounded prediction/reconciliation

    if (!engine.IsCatchingUp)
        presentation.Sample();
}
```

The `tick` passed to `BuildInput` is overwritten by `SubmitLocalInput`; it may be zero at the
sampling boundary.

## Immediate feedback

Scheduling lead prevents rollbacks but should not make controls feel delayed:

```csharp
session.InputAnticipated += proposal =>
{
    cameraFeedback.Apply(proposal.Input);
    animator.SetMovementIntent(proposal.Input.AxisX, proposal.Input.AxisY);
};

session.InputResolved += decision =>
{
    cosmeticFeedback.Resolve(
        decision.Sequence, decision.Disposition, decision.AssignedTick);
};
```

These callbacks are presentation-only. Never write preview transforms into the simulation.

## Deterministic events

```csharp
session.EventAnticipated += proposal => muzzleFlash.Play();
session.EventResolved += decision => muzzleFlash.Resolve(decision.Sequence, decision.Disposition);
session.SubmitEvent(new SpawnAction("bullet", muzzle, rotation, localPlayerId), now);
```

The server schedules and rebroadcasts the serialized action. All roles execute it from the game
channel at the assigned tick.

## Recovery UI

Observe `session.StateChanged`. `CatchingUp` means replay is continuing under the per-frame
budget; `Resyncing` means the client requested and is applying a server snapshot. Both recover
automatically.

Continue with [Rollback and input](RollbackAndInput.md), [Networking](Networking.md), and
[Configuration](Configuration.md).
