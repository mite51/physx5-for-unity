# The gameplay layer

The determinism layer gets physics to roll back correctly. It stops at `ISimStepHandler`:
one callback around the step, and nothing above it. A game needs entities that carry state,
things that spawn and die, scores and phases and timers, players who drive avatars, and a
camera that makes "forward" mean forward. The `Gameplay/` layer is all of that, built as a
generic framework rather than a port of any one game.

It is optional. A world driven straight through `ISimStepHandler` still works exactly as
before; nothing here changes the engine. But if you want a game rather than a physics demo,
this is the intended way up.

## The idea in one paragraph

Gameplay state has to roll back too. The engine already restores physics before every step
and replays the whole prediction horizon every frame, so health, scores and the pending
action log have to be restored on that same schedule or they are simply wrong on the replay.
The layer adds two managed state channels to the snapshot next to the physics blob, funnels
every game into one step handler with a fixed per-tick order, and leans on the fact that a
rollback restores everything wholesale to delete the two most error-prone mechanisms the
original system had: action undo, and the pooling creation log.

## Three state channels

A snapshot carries three channels, captured and restored together and hashed apart:

| channel | holds | written by |
| --- | --- | --- |
| physics | pose, velocity, sleep, articulation | the native layer |
| entity | per-entity managed state, in stable-ID order | `SimGameEntity.CaptureState` |
| game | the game mode's own state, and the action log | `ISimGameState` + `SimActionQueue` |

The managed channels go through `SimStateWriter` / `SimStateReader` — a cursor over a
preallocated buffer that folds each write into an FNV-1a hash as it goes, so the bytes and the
hash always agree and neither allocates in the tick loop. `Snapshot.CombinedHash` folds all
three channel hashes together; a desync report can then say *physics*, *entity 4102* or *game
state* instead of just "something diverged".

`SimGameHost` is the `ISimStateProvider` that supplies the two managed channels: the entity
channel is the registry walked in stable-ID order, and the game channel is the action queue
followed by the game mode's own state. Because one object decides both the capture order and
the per-tick execution order, the two cannot drift apart.

### The one contract

Every `Capture`/`Restore` pair must be exact inverses: write and read the same fields in the
same order. A field captured but not restored, or read out of order, is a desync that behaves
exactly like a physics determinism bug and is diagnosed the same painful way. The writer and
reader have no self-describing tags — that is the price of not allocating — so the coupling is
the same one a `BinaryWriter`/`BinaryReader` pair has.

## Two things the rollback model deletes

**Actions need no undo.** The original system paired every action with a reversal, and the
despawn action had to snapshot the whole object it removed so it could rebuild it — the single
most intricate corner of that code. Here a rollback restores all three channels wholesale, so
an action's effects are undone by the restore and redone by replaying the action. `ISimAction`
has `Execute`, and no `Undo`.

**Pooling needs no creation log.** Everything is created once at session start, in stable-ID
order, so the snapshot layout never changes. Spawning is `SetEntityEnabled(true)` plus a
teleport; despawning is the reverse. There is no creation log to replay for a joiner and no
free list to keep in sync — "which instances are free" is derived from the entities' own active
flags, which are in the entity channel and therefore already roll back. `SimEntityPool.Spawn`
hands out the lowest-ID inactive instance, which is a pure function of restored state, so two
peers pick the same instance without exchanging a message.

## Entities

A `SimGameEntity` is a `MonoBehaviour` on a pooled prefab: a stable ID, a physics body, the
managed state that rolls back with it, and the per-tick callbacks that drive it. A concrete
entity supplies four things and inherits the rest:

```csharp
public sealed class Ball : SimGameEntity
{
    private float _charge;

    public override IntPtr ResolveNativeHandle(out SimHandleKind kind)
    {
        kind = SimHandleKind.RigidDynamic;
        return GetComponent<PhysxRigidActor>().NativeObjectPtr; // your actor component
    }

    public override void OnSimSpawn(int tick) { _charge = 0f; }

    public override void OnSimUpdate(int tick, bool isReplay)
    {
        Vector3 move = SimInputEncoder.MovementDirection(Input);      // world-space, from the frame
        SimBody.AddForce(this, move * 20f, SimForceMode.Acceleration); // forces only in a step handler
        _charge = Mathf.Min(_charge + 1f, 100f);
    }

    protected override void CaptureState(ref SimStateWriter writer) { writer.WriteFloat(_charge); }
    protected override void RestoreState(ref SimStateReader reader)  { _charge = reader.ReadFloat(); }
}
```

`OnSimUpdate` runs once per tick in stable-ID order and again on every replayed tick, so it
must be a pure function of world state and `Input`: no `Time.deltaTime`, no
`UnityEngine.Random`, no input outside `Input`. The component stays enabled while the entity is
pooled out so it keeps its slot in the loop; only the optional `presentationRoot` is toggled.

The framework never assumes which PhysX actor component you use — `ResolveNativeHandle` is how
the entity hands over its body pointer.

## Actions

An action is a discrete change submitted rather than done inline, so every such change lands on
the same schedule on every peer. Same-tick actions — the common case — are submitted during an
update and run before the step, then discarded; they never touch a channel. An action scheduled
for a *future* tick has to survive until then and survive a rollback in between, so it rides in
the game channel, which is the only reason `ISimAction` is serializable.

`SpawnAction` and `DespawnAction` ship over the pool. Custom actions register once at setup, in
an order every peer matches:

```csharp
host.Actions.RegisterActionType<AwardPointAction>(() => new AwardPointAction());
// ...during an update:
host.Actions.Submit(new SpawnAction("bolt", muzzle, aim, ownerId)); // this tick
host.Actions.Submit(new DespawnAction(id), tick + 90);              // 1.5 s later at 60 Hz
```

## Game modes

`ISimGameMode` is the single seam a game plugs its rules into, and it *is* the game channel:
it extends `ISimGameState`, so everything it decides is captured and restored with the rest of
the simulation. A score changed in `OnContact` rolls back for free — there is no second
bookkeeping path to keep in step, which is the split the previous system never resolved.

Inherit `SimGameModeBase` and override what you need:

```csharp
public sealed class KingOfTheHill : SimGameModeBase
{
    private readonly SimPhaseMachine<Phase> _phase = new SimPhaseMachine<Phase>(Phase.Warmup);
    private int _scoreA, _scoreB;

    public override void OnTickEnd(int tick)
    {
        if (_phase.Phase == Phase.Warmup && _phase.TicksInPhase(tick) > 180)
            _phase.TransitionTo(Phase.Playing, tick);
    }

    public override void CaptureGameState(ref SimStateWriter writer)
    {
        _phase.Capture(ref writer);
        writer.WriteInt(_scoreA);
        writer.WriteInt(_scoreB);
    }

    public override void RestoreGameState(ref SimStateReader reader)
    {
        _phase.Restore(ref reader);
        _scoreA = reader.ReadInt();
        _scoreB = reader.ReadInt();
    }
}
```

`SimPhaseMachine<TPhase>` packages the phase-and-timer pattern as two integers, timed in ticks
because ticks are the only clock peers share.

## The game host

`SimGameHost` is the one `ISimStepHandler` for the whole game, on purpose. If entities, the
game mode and the action queue each registered their own handler, their run order would depend
on registration order — and a difference in that between peers is a desync. Funnelling
everything through one handler makes the order a single readable sequence every tick:

1. the game mode's tick-begin hook;
2. this tick's inputs distributed to their entities;
3. actions already due this tick;
4. entities updated in stable-ID order;
5. the game mode's volume resolution;
6. late actions the updates just submitted;
7. the physics step (run by the engine);
8. the contact and trigger drain, then tick-end.

## Players and camera-relative input

A `SimPlayer` is a participant, distinct from the entity they drive — a player exists before
they spawn an avatar and may drive different ones over a match. Its `Slot`, assigned in
ascending player-ID order, indexes the input frame. `SimPlayerRegistry.DistributeInputs` sets
each player's input on its bound entity, so an entity reads its controller without knowing
anything about players or the network.

Camera-relative movement is generalised to `ISimInputFrameProvider`, which returns the
horizontal forward/right a peer's raw input is resolved against. Orbit, first-person, fixed
isometric and world-space providers ship; any camera qualifies.

The trap this avoids is subtle. If a peer simulates from a raw float direction but networks a
quantized one, remote peers simulate from the dequantized value and the sender has desynced
against everyone from the first tick. `SimInputEncoder` quantizes *and dequantizes* before the
value is used locally, so the sender runs on the same value the receivers will:

```csharp
uint buttons = ReadButtons();
Vector2 wasd = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
SimInput input = SimInputEncoder.BuildInput(
    myPlayerId, engine.LocalInputTick, buttons, wasd, myCameraFrame);
engine.SubmitInput(input);
```

`LocalInputTick` is `CurrentTick` plus `SimConfig.LocalInputDelay`, and stamping against it
is what keeps remote players from snapping. An input that arrives before the tick it is
stamped for is never predicted by anyone, so there is nothing to correct when it lands. The
delay is peer-local and unhashed — see the latency section in the [package
README](../README.md).

The camera orientation never enters the payload — only the resolved, quantized world direction
does — so two peers with wildly different camera angles still simulate identically. Movement
lands in `AxisX`/`AxisY`; per the aim model, entities aim along their own facing, and
`AxisZ`/`AxisW` stay free for a later change without disturbing the wire format.

## Presentation

`SimPresentationBinder` drives entities' visible transforms from `DeterministicWorld.ReadPoses`,
interpolating between the two most recent simulated poses so motion is smooth at any refresh
rate. The arrow points one way only: poses flow out of the simulation into transforms and never
back. Reading a transform back into the sim would let a render-rate quantity into a
deterministic computation and desync.

```csharp
// after the sim advances:
binder.Sample();
// each rendered frame:
binder.Render(alphaThroughCurrentTick);
```

## Putting it together

```csharp
var world  = new DeterministicWorld(config);
var ids    = new StableIdAllocator(sessionSeed);
var engine = new RollbackEngine(world, playerIds);

var host = new SimGameHost(world, engine, ids);
host.SetGameMode(new KingOfTheHill());
host.Pool.Add("ball", ballPrefab, 8);
host.Pool.Add("bolt", boltPrefab, 64);
host.Actions.RegisterActionType<AwardPointAction>(() => new AwardPointAction());

var alice = host.AddPlayer(aliceId, slot: 0);
var bob   = host.AddPlayer(bobId,   slot: 1);

host.Begin();          // preregisters the pool, starts the mode, initialises tick 0
alice.BindEntity(host.Pool.Spawn("ball", spawnA, Quaternion.identity).StableId);
bob.BindEntity(host.Pool.Spawn("ball", spawnB, Quaternion.identity).StableId);

var binder = new SimPresentationBinder(world, host.Registry);
binder.Rebuild();

// once per fixed update:
engine.SubmitInput(SimInputEncoder.BuildInput(
    myId, engine.LocalInputTick, buttons, wasd, cameraFrame));
engine.Advance();
binder.Sample();

// each render:
binder.Render(alpha);
```

## What the native side still owes

Forces, scene queries and contact/trigger events are all implemented: `SimBody`, `SimQuery`
and `SimContacts` reach PhysX through the native plugin and run today. Contact and trigger
events are collected by a `PxSimulationEventCallback` behind a notification-only filter shader
that leaves the simulation bit-identical, then resolved to stable IDs, normalised and sorted
before they cross the boundary, so peers and replays see the same events in the same order.
See [NativeGameplayApi.md](NativeGameplayApi.md).

Two disciplines apply to events. First, ordering is reproducible but a contact's point, normal
and impulse are not bit-exact across a cold restore, because they come from solver warm-start
state the snapshot does not carry: branch hashed state on *which* bodies touched, never on the
exact impulse. Second, for a one-off overlap check an explicit `SimQuery.Overlap` in a step
handler is often clearer than a trigger, because it is evaluated at a known point in the tick
rather than reported after it; triggers earn their place when polling every volume every tick
would be wasteful.

What is still outstanding on the native side is vehicle rollback state beyond the chassis, and
the transport layer — neither of which the gameplay event path depends on.

## Verification

The EditMode tests in `Tests/` exercise the parts that need no native world: the state cursors'
round trip and hash stability, the input encoder's quantize/dequantize idempotence, the action
queue's channel serialization, and the phase machine. The property they all check is the one
everything here rests on — a capture followed by a restore reproduces exactly what was captured,
so a hash taken before a rollback matches the hash taken after the resimulation.
