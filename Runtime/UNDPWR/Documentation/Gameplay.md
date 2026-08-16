# The gameplay layer

The engine and step handler get physics to roll back. A game needs more: entities that carry
state, things that spawn and die, scores and phases and timers, players who drive avatars, and
a camera that makes "forward" mean forward. The `Gameplay/` layer is all of that, built as a
generic framework and wired into a single [`SimGameHost`](../Gameplay/Game/SimGameHost.cs).

Authoritative sessions use this layer directly: `SimClientSession` and `SimServerSession`
share its action queue for deterministic network events.

## The three channels, in gameplay terms

[Concepts](Concepts.md#three-state-channels) introduced the three snapshot channels. The
gameplay layer is what writes the two managed ones:

| channel | holds | written by |
| --- | --- | --- |
| physics | pose, velocity, sleep, articulation/vehicle integrator state | the native layer |
| entity | per-entity managed state, in stable-ID order | `SimGameEntity.CaptureState` |
| game | the game mode's own state and the pending action log | `ISimGameMode` + `SimActionQueue` |

`SimGameHost` is the [`ISimStateProvider`](../Rollback/ISimStateProvider.cs) that supplies both
managed channels, so the same object decides both the capture order and the per-tick execution
order and the two cannot drift apart.

### The one contract

Every `Capture`/`Restore` pair must be **exact inverses**: write and read the same fields in
the same order. The writer and reader ([`SimStateWriter`/`SimStateReader`](../Core/SimStateBuffer.cs))
have no self-describing tags — that is the price of not allocating in the tick loop — so a
field captured but not restored, or read out of order, is a desync that behaves exactly like a
physics determinism bug. Treat it like a `BinaryWriter`/`BinaryReader` pair.

## Entities

A [`SimGameEntity`](../Gameplay/SimGameEntity.cs) is a `MonoBehaviour` on a pooled prefab, but
its simulation is driven entirely by the framework — never by Unity's `Update`. A concrete
entity supplies three things and inherits the rest:

```csharp
public sealed class Ball : SimGameEntity
{
    private float _charge;

    // How to find the native body. Return your actor component's handle and its kind.
    public override IntPtr ResolveNativeHandle(out SimHandleKind kind)
    {
        kind = SimHandleKind.RigidDynamic;
        return GetComponent<PhysxRigidActor>().NativeObjectPtr;
    }

    public override void OnSimSpawn(int tick) { _charge = 0f; }

    // Once per tick in stable-ID order, and again on every replayed tick, so it must be a pure
    // function of world state and Input: no Time.deltaTime, no UnityEngine.Random.
    public override void OnSimUpdate(int tick, bool isReplay)
    {
        Vector3 move = SimInputEncoder.MovementDirection(Input);               // world-space, from the frame
        SimBody.AddForce(Registration, move * 20f, SimForceMode.Acceleration); // forces only in a sim callback
        _charge = Mathf.Min(_charge + 1f, 100f);
    }

    // The entity channel. Write and read the same fields in the same order.
    protected override void CaptureState(ref SimStateWriter writer) { writer.WriteFloat(_charge); }
    protected override void RestoreState(ref SimStateReader reader)  { _charge = reader.ReadFloat(); }
}
```

The framework handles the stable ID, registration, the active flag and the entity's place in
the channel. `Input` is this tick's input, set by the player binding for a controlled entity or
left neutral for an AI one (which drives itself from its own state). The active flag is captured
ahead of your `CaptureState`, so it rolls back for free.

Presentation is kept downstream: the component stays enabled even while pooled out, so it keeps
its slot in the tick loop; only the optional `presentationRoot` GameObject is toggled with the
active flag.

## Pooling

[`SimEntityPool`](../Gameplay/SimEntityPool.cs) is deliberately simple, and rollback is why.
Every instance is created and registered once at session start, in stable-ID order, so the
snapshot layout never changes. Spawning is `SetEntityEnabled(true)` plus a teleport; despawning
is the reverse. There is no creation log to replay for a joiner and no free list to keep in
sync — "which instances are free" is derived from the entities' own active flags, which are in
the entity channel and therefore already roll back.

```csharp
host.Pool.Add("ball", ballPrefab, count: 32); // preregister before Begin, same on every peer
```

`Pool.Spawn("ball", position, rotation)` hands out the **lowest-ID inactive instance**, which
is a pure function of restored state, so two peers spawning from the same state pick the same
instance without exchanging a message. When a group is exhausted the spawn is dropped — which
every peer does identically, so it does not desync; raise the count or despawn sooner.

Prefer scheduling spawns and despawns as **actions** (below) rather than calling the pool
directly, so they run at a well-defined point in the tick.

## Actions

An [`ISimAction`](../Gameplay/Actions/ISimAction.cs) is a discrete change scheduled through the
[`SimActionQueue`](../Gameplay/Actions/SimActionQueue.cs). Because a rollback restores all three
channels wholesale, an action's effects are undone by the restore and redone by replaying the
action — so **actions need no undo**. `ISimAction` has `Execute`, and no `Undo`.

```csharp
// Fire a projectile now:
context.Actions.Submit(new SpawnAction("bullet", muzzlePos, muzzleRot, owner: shooterId));

// Or schedule for a future tick:
context.Actions.Submit(new DespawnAction(entityId), scheduledTick: tick + 120);
```

[`SpawnAction`](../Gameplay/Actions/SpawnAction.cs) and `DespawnAction` are provided. Most
actions execute on the tick they are submitted and touch nothing else. An action scheduled for
a *later* tick must survive a rollback in between, so it rides in the game channel — which is
the only reason `ISimAction` is serializable. Register each future-scheduled action type once
at setup, in an order every peer matches:

```csharp
host.Actions.RegisterActionType(() => new SpawnAction());
host.Actions.RegisterActionType(() => new DespawnAction());
```

Execution order within a tick is submission order, which is deterministic because every peer
runs the same gameplay to produce the same submissions.

For a player-originated network event, submit the registered action through
`SimClientSession.SubmitEvent`. The server assigns its tick and inserts it into its action
queue, then reliably sends the same serialized action to every client. Use `EventAnticipated`
for immediate cosmetic feedback and `EventResolved` to accept, retime or cancel that feedback.
Do not execute the action locally from the anticipation callback.

## Game modes

[`ISimGameMode`](../Gameplay/Game/ISimGameMode.cs) is the single seam your rules plug into: what
happens at the start and end of each tick, how players enter and leave, what a contact or
trigger means. Because it also *is* the game channel (it extends `ISimGameState`), everything it
decides is captured and restored with the rest of the simulation — a score changed in
`OnContact` rolls back for free. Inherit from `SimGameModeBase` and override only what you need:

```csharp
public sealed class KingOfTheHill : SimGameModeBase
{
    private int _blueScore, _redScore;

    public override void OnResolveVolumes(int tick)
    {
        // Called after entities update, before the step — the place for capture zones,
        // pickups, kill planes, driven by SimQuery overlaps.
    }

    public override void OnContact(SimContext context, SimContactEvent contact)
    {
        // Branch on WHICH bodies touched (the pair is reproducible), never on the exact
        // impulse or contact point. See SimulationAPIs.md.
    }

    // The game channel. Same exact-inverse contract as an entity's.
    public override void CaptureGameState(ref SimStateWriter writer)
    {
        writer.WriteInt(_blueScore); writer.WriteInt(_redScore);
    }
    public override void RestoreGameState(ref SimStateReader reader)
    {
        _blueScore = reader.ReadInt(); _redScore = reader.ReadInt();
    }
}
```

For phase-and-timer logic, [`SimPhaseMachine<TPhase>`](../Gameplay/Game/SimPhaseMachine.cs) is a
tick-based helper whose state you capture and restore in the game channel like any other field.

## The game host and its fixed tick order

[`SimGameHost`](../Gameplay/Game/SimGameHost.cs) is the one `ISimStepHandler` and the one
`ISimStateProvider` for the whole game, so the per-tick order lives in one readable place and
cannot depend on registration order. Every tick runs, in this fixed order:

1. the game mode's `OnTickBegin`;
2. this tick's inputs distributed to their entities;
3. actions already due this tick;
4. entities' `OnSimUpdate`, in stable-ID order;
5. the game mode's `OnResolveVolumes`;
6. late actions the updates just submitted;
7. the physics step (run by the engine);
8. the contact and trigger drain (`OnContact` / `OnTrigger`), then `OnTickEnd`.

Setup and start:

```csharp
var host = new SimGameHost(world, engine, ids);
host.Pool.Add("ball", ballPrefab, 32);
host.Actions.RegisterActionType(() => new SpawnAction());
host.AddPlayer(localPlayerId, slot: 0);
host.SetGameMode(new KingOfTheHill());
host.Begin(); // preregister pool, commit, disable dormant instances, start mode,
              // wire host into engine as handler + state provider, capture tick 0
```

After `Begin`, do not call `engine.AddHandler`, `engine.SetStateProvider` or
`engine.Initialise` yourself — the host did. Bind players to their avatars (typically spawn an
avatar and `player.BindEntity(avatarId)`), then run the networked `FixedUpdate` loop from
[Getting started](GettingStarted.md#step-2--add-networking).

## Players and camera-relative input

A [`SimPlayer`](../Gameplay/Input/SimPlayer.cs) is a participant, distinct from the entity they
drive. Its `Slot` — assigned in ascending player-ID order — is what indexes the input frame, so
it is stable and shared across peers. `player.BindEntity(entityId)` binds it to an avatar;
[`SimPlayerRegistry.DistributeInputs`](../Gameplay/Input/SimPlayerRegistry.cs) (called by the
host) sets each player's input on the entity it drives.

Movement should be camera-relative without the camera ever touching the simulation. Build input
with [`SimInputEncoder`](../Gameplay/Input/SimInputEncoder.cs) and an
[`ISimInputFrameProvider`](../Gameplay/Input/ISimInputFrameProvider.cs):

```csharp
ISimInputFrameProvider frame = new SimOrbitInputFrame(cameraTransform);
SimInput input = SimInputEncoder.BuildInput(playerId, 0, buttons, rawMove, frame);
client.SubmitLocalInput(input, nowMicroseconds); // assigns the adaptive future server tick
```

The encoder resolves the stick against the camera locally, then **quantizes and dequantizes**
the result before it is used, so the sender simulates the exact value the receivers will
dequantize from the wire. The camera orientation never enters the networked payload — only the
resolved, quantized direction does — so two peers with different camera angles still simulate
identically. Providers are supplied for world-space (`SimWorldSpaceInputFrame`), fixed
(`SimFixedInputFrame`), orbit (`SimOrbitInputFrame`) and first-person
(`SimFirstPersonInputFrame`) cameras. Read the direction back in `OnSimUpdate` with
`SimInputEncoder.MovementDirection(Input)`.

## Presentation

The simulation runs at a fixed tick rate; rendering does not, and the prediction window replays
every frame. [`SimPresentationBinder`](../Gameplay/Presentation/SimPresentationBinder.cs)
interpolates each entity's visible transform between the two most recent simulated poses, so
motion is smooth without any render-rate quantity feeding back into the simulation.

```csharp
var binder = new SimPresentationBinder(world, host.Registry);
binder.Rebuild();        // once, after the pool is preregistered

// after each engine.Advance():
binder.Sample();

// each rendered frame, alpha in [0,1] for how far through the current tick:
binder.Render(alpha);
```

The direction is the whole point: poses flow out of the simulation into transforms and never
back. A binder that read `transform.position` into the sim would let a render-rate quantity into
a deterministic computation and desync.

The same one-way rule applies to anticipation. `InputAnticipated` may move a camera, select an
animation or play a cosmetic effect immediately; the authoritative physics body still moves
only from its scheduled `SimInput`.

## Putting it together

```csharp
// setup
var host = new SimGameHost(world, engine, ids);
host.Pool.Add("ball", ballPrefab, 32);
host.AddPlayer(localPlayerId, 0);
host.SetGameMode(new KingOfTheHill());
host.Begin();

var binder = new SimPresentationBinder(world, host.Registry);
binder.Rebuild();

var network = SimNetConfig.Authoritative;
var client = new SimClientSession(
    engine, transport, config, network, localPlayerId, playerIds, host.Actions);
client.Start(nowMicroseconds);

// FixedUpdate
client.Pump(nowMicroseconds);
client.SubmitLocalInput(SampleInput(), nowMicroseconds);
client.Advance();
if (!engine.IsCatchingUp)
    binder.Sample();

// Update
binder.Render(Mathf.Clamp01((Time.time - lastFixed) / Time.fixedDeltaTime));
```

## Next

- [Simulation APIs](SimulationAPIs.md) — forces, queries and contacts, and their determinism rules.
- [Vehicles and articulations](VehiclesAndArticulations.md) — handle kinds and vehicle commands.
- [Networking](Networking.md) — the session, desync detection, and mid-match join.
