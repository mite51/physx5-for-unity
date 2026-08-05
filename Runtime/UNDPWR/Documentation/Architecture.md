# UNDPWR architecture

How the framework is put together, how a tick actually executes, where data lives and who
owns it, and where your game plugs in.

This is the document to read before changing anything. Most of the design is shaped by one
constraint that is not obvious from the code, and changes that look harmless tend to
violate it.

---

## 1. What the framework is for

Every peer simulates the whole physics world locally, at a fixed tick rate, from the same
inputs. Only inputs cross the network. Because every peer computes the same result, no one
needs to send positions, so bandwidth is a function of player count and stays flat as the
physics scene grows.

That only works if the simulations genuinely agree. Almost everything in this document is
in service of making them agree, and of noticing quickly when they do not.

**In scope:** rigid bodies, articulations, and vehicles; rollback and prediction; mid-match
join; desync detection; deterministic identity and mass.

**Out of scope:** transport and matchmaking (you supply those behind an interface),
rendering, gameplay logic, and cross-architecture play — see
[CrossPlatformDeterminism.md](CrossPlatformDeterminism.md), which explains why all peers
must currently share a CPU architecture.

---

## 2. The constraint everything else follows from

> **Every peer must perform an identical sequence of operations every tick.**

Not a similar sequence. An identical one — same operations, same order, same count, every
frame, regardless of what the network delivered.

This is stricter than most rollback netcode requires, and it is forced on us by PhysX.
PhysX carries state between steps that no public API can read or write: warm-start contact
impulses in persistent manifolds, friction anchors, broadphase pair bookkeeping, island
assignments, and TGS's per-substep working state. None of it can be captured in a snapshot,
so a restored world carries hidden state from whatever it simulated *before* the restore.

### 2.1 Two different comparisons

Almost all confusion here comes from conflating two questions that have different answers:

- **Self-comparison (transparency).** Does a replayed tick match what that same peer
  computed the first time through?
- **Peer-comparison.** Do two peers computing the same tick agree with each other?

Peer-comparison is the one netcode actually asks, and the one the framework relies on. A
peer never compares itself against a hypothetical un-rewound version of itself. Transparency
matters only indirectly: when it holds, peers that did *different amounts of work* still
agree, and rollback depth stops having to be synchronised.

### 2.2 What was measured

Detail, method and the raw numbers are in
[DeterminismInvestigation.md](DeterminismInvestigation.md); this is the shape of it.

| experiment | result |
| --- | --- |
| Two peers running the same ticks from the same start | bit-identical indefinitely |
| A peer replaying its own earlier trace, stepping normally | diverges, ~2e-06 m over 30 ticks |
| The same, restoring before **every** step, PGS | **bitwise identical** |
| The same, restoring before **every** step, TGS | diverges on the first replayed step, ~3e-09 m/s |
| Two worlds driven along different histories, then given one snapshot | bit-identical for 600 steps |
| A world with **no** history replaying a used world's trace | 0 of 16 ticks exact |
| Two peers rolling back by **different, varying** depths, cold steps, PGS | bit-identical for 600 frames |
| The same, with a contact chain 9 or more bodies deep | diverges |
| The same, under TGS | diverges |

Two findings carry the design.

**Restore erases history.** `restore(S); step()` is a pure function of `S`. Two worlds
driven along deliberately different histories, handed the same snapshot, stay bit-identical
indefinitely. Peers do not need a *shared* history to agree — they need an identical
*operation sequence*.

**Restoring is not lossy; it is merely different from not restoring.** A step after a
restore runs from a cold contact cache, a step after another step runs warm, and the two do
not agree. Which is why the framework restores before *every* step (§6), including steps
nobody rolled back: it makes every step cold, so the warm/cold distinction disappears. Under
PGS that is enough for full transparency. Under TGS — which is what `SimConfig` selects — it
is not, because TGS's substep state survives the restore and nothing exposed clears it.

### 2.3 What follows

- **The identical-sequence rule is load-bearing, not belt-and-braces.** Under TGS, or with a
  contact chain more than 8 bodies deep, peers that do different amounts of work *do*
  diverge. The rule is what makes them agree.
- **The horizon must be fixed rather than merely bounded.** Not because differing depth is
  instantly fatal — a single divergent rollback produces no observable difference — but
  because the per-event error is below the resolution of captured state and accumulates
  until it flips a bit, hundreds of frames after the cause. That is the worst possible
  failure shape: invisible in short tests, uncorrelated with any single event.
- **Having no history at all is a category difference, not a degree.** A world that has
  never simulated cannot match one that has, under any contact reset mode. Two *used* worlds
  restored from the same snapshot do agree — so the distinction is warmed versus never
  simulated, not the specific contents of the warm-up.

So the framework never tries to make differing histories converge. It arranges for them
never to differ in the first place, and when that becomes impossible — a peer joining
mid-match has no history at all — it puts every peer onto a common history at once.

Four concrete rules fall out, and each has a section below: every step is preceded by a
restore (§6.1), the prediction horizon is fixed (§6), the confirmed timeline advances at
most one tick per frame (§6.3), and joining is a synchronised rebuild rather than a
catch-up (§9).

---

## 3. Layers

```
   your game
       │  ISimGameMode, SimGameEntity   (rules, entities — Gameplay.md)
       ▼
┌──────────────────────────────────────────────────────────┐
│  Gameplay/       SimGameHost, entities + pool, actions,   │  a game over
│                  game modes, players, camera-relative     │  the engine
│                  input, presentation binding              │
│                  (single ISimStepHandler + ISimStateProvider)
├──────────────────────────────────────────────────────────┤
│  Rollback/       RollbackEngine, InputBuffer, SimInput,   │  when to step,
│                  ISimStepHandler, ISimStateProvider       │  what to replay
├──────────────────────────────────────────────────────────┤
│  Core/           DeterministicWorld, SimEntity,           │  what exists,
│                  StableIdAllocator, SnapshotRing,         │  what state it has
│                  SimConfig, SimMass, SimStateWriter/Reader│  (3 channels)
├──────────────────────────────────────────────────────────┤
│  Interop/        NativeMethods, NativeTypes  (internal)   │  P/Invoke only
└──────────────────────────────────────────────────────────┘
       │  PxwWorld* API
       ▼
   PhysXUnity.dll  →  PxwUndpwr.cpp   registry, capture/restore/hash,
                                       deterministic mass, per-scene stepping
       │
       ▼
   PhysX 5 (patched — see §12)
```

The dependency direction is strictly downward. `Core` knows nothing about ticks or
rollback; `Rollback` knows nothing about P/Invoke or gameplay; `Gameplay` is optional and
sits entirely above the engine, reaching state through `ISimStateProvider` and the step
through `ISimStepHandler`. `Interop` is `internal` on purpose: calling it directly bypasses
the stable-ID ordering in §4, which is the single easiest way to produce a desync that looks
like a physics bug.

`Diagnostics/SimLog` is orthogonal and used by every layer, including the native one, which
routes its messages up through a callback.

---

## 4. Identity: stable IDs

PhysX only guarantees reproducible results when actors are inserted into the scene in the
same order. Gameplay code spawns things in whatever order it likes, and two peers will not
agree on that order — one player's crate is destroyed slightly earlier, a coroutine fires a
frame later, an object pool hands back a different instance.

This was the hardest lesson from the original UNDPWR and it remains the most important
thing the framework does.

### 4.1 The ID ranges

`StableIdAllocator` partitions the 32-bit space so that different sources cannot collide:

| range | purpose | assigned |
| --- | --- | --- |
| `1 .. 0x0FFFFFFF` | **Authored.** Scene content — a crate placed in a level. | At author time, baked into content. Stable across sessions and builds. |
| `0x10000000 .. 0x7FFFFFFF` | **Deterministic runtime.** Spawned during a session. | `Allocate()`, a pure function of allocation order. |
| `0x80000000 ..` | **Local.** Debug visualisers, presentation-only objects. | `AllocateLocal()`. Never registered with a networked world. |

The runtime range works only because allocation happens *inside the simulation*, on a tick
every peer runs. Allocating from a UI callback, a coroutine or a network message handler
happens at a different moment on each peer and desyncs the allocator itself — so
`Allocate()` takes the current tick and warns when it moves backwards.

### 4.2 Deferred, ordered insertion

`DeterministicWorld.Register` does **not** add the actor to the scene. It records the
intent. Actors reach the scene at `CommitPending()`, sorted by stable ID, in the native
layer.

```
gameplay spawn order:   crate(4102)  bullet(0x10000007)  barrel(4099)
scene insertion order:  barrel(4099) crate(4102)         bullet(0x10000007)
```

Every peer therefore issues an identical sequence of `PxScene::addActor` calls no matter
what its gameplay code did. `CommitPending` is called by the rollback engine at a tick
boundary; gameplay code generally should not call it, and must never call it mid-replay —
an actor inserted part-way through a replay has a different history than the same actor on
a peer that inserted it at the boundary.

### 4.3 Prefer disabling to unregistering

For anything that comes and goes — projectiles, debris, pooled effects — use
`SetEntityEnabled(id, false)` rather than `Unregister`. Disabling keeps the ID and the
entity's slot in the snapshot layout. Unregistering changes the layout, and a layout change
mid-session must be agreed by every peer, which is a much bigger commitment than it looks.

---

## 5. State: what a snapshot is

A snapshot has three channels, captured and restored together every tick and hashed
separately so a mismatch can be attributed to one of them:

- **Physics** — the opaque native blob described in §5.1, hashed natively.
- **Entity** — per-entity managed state (health, timers, an AI target), written in stable-ID
  order through `SimStateWriter`. Empty for a world driven without the gameplay layer.
- **Game** — the game mode's own state and the pending action log, also through
  `SimStateWriter`.

The two managed channels exist so gameplay state survives a rewind the same way physics does;
they are supplied by an optional `ISimStateProvider` (the gameplay layer's `SimGameHost`) and
are the subject of [Gameplay.md](Gameplay.md). The rest of this section is the physics channel,
which is the one with the hard determinism constraints.

### 5.1 Contents

The physics channel is an opaque byte blob produced by the native layer. Per entity it holds:

- **Rigid dynamic / kinematic** — world pose, linear and angular velocity, rest counter,
  sleeping flag, disabled flag.
- **Articulation** — root pose, root velocities, joint positions / velocities / forces for
  every DOF, rest counter, sleep and disabled flags.
- **Rigid static** — nothing. Statics are registered so queries and contact reports can
  resolve them to a stable ID, but they cannot move, so there is nothing to capture.

The blob carries a magic number, a version, and an entry manifest. Restoring a blob into a
world with a different set of entities fails with `NativeResult.EntryMismatch` rather than
silently producing nonsense — that error almost always means two peers built their worlds
differently.

The sleeping flag and the rest counter drive framework sleeping (§5.6); the wake counter is
deliberately *not* captured, because it is pinned while a body is awake and would not replay
otherwise.

### 5.2 What is deliberately absent, and why it matters

Solver warm-start contact impulses, friction anchors, broadphase pair bookkeeping, island
assignments and TGS substep state are **not** in the snapshot, because PhysX exposes no way
to read them.

This is not an oversight to fix later; it is the property the whole design is built around
(§2). Two consequences:

- Two worlds restored from the *same* snapshot **are** identical with each other. The
  framework relies on this everywhere.
- Whether a restored world matches the world the snapshot came from depends on the solver:
  under PGS it does, provided every step is cold; under TGS it does not. The framework
  relies on this nowhere, which is why TGS remains a supportable choice.

### 5.3 Do not clear the contact caches

The intuition that a rewind should start by wiping PhysX's carried state is wrong, and the
measurements say so plainly. Over a 30-tick replay of a contact-heavy stack:

| `SimContactResetMode` | position error | velocity error |
| --- | --- | --- |
| `None` | 1.8e-06 m | 9.4e-06 m/s |
| `ResetFiltering` | 1.4e-02 m | 7.0e-02 m/s |
| `Reinsert` | 9.8e-02 m | 2.1e-01 m/s |

Clearing discards warm-start data the original tick actually had, so the replay ends up
*further* from the truth. `None` is the default by four orders of magnitude. The other
modes exist for hard resynchronisation, where discarding history is the entire point.

There is also nothing left for them to do. Restoring a pose invalidates that actor's cached
contact data as a side effect, so every restore already cools the cache — which is the
mechanism the cold-step discipline in §6.1 runs on.

### 5.4 Hashing

Two granularities, both FNV-1a and both computed natively so managed and native code agree:

- `HashState()` — the whole world. Cheap, and answers "do we agree?"
- `HashPerEntity()` — one hash per stable ID. Answers "**who** disagrees?", which is
  usually enough to recognise the cause.

Whole-world hashes are what peers exchange. Per-entity hashes are what you pull when one
mismatches.

When the gameplay layer is present, the entity and game channels each fold their bytes into a
hash of their own, and `Snapshot.CombinedHash` folds all three together — so a mismatch first
tells you *physics*, *entity* or *game* before you drill into which entity. The managed
channel hashes use the same FNV-1a as the native ones, so they are comparable across peers the
same way.

### 5.5 The snapshot ring

`SnapshotRing` holds `SimConfig.SnapshotHistory` ticks in a fixed, preallocated ring. It is
never grown during a session, because allocating during a rollback would drop a frame at
exactly the moment the simulation is busiest.

Capacity bounds how far back a late input can be honoured. An input older than the ring
cannot be applied, because the state it would apply to is gone — that peer has exceeded the
session's latency budget and needs a resynchronisation (§9). `SimConfig.Validate` enforces
`SnapshotHistory > PredictionHorizon + LocalInputDelay`: those two together span the live
window, from the confirmed tick out to the furthest tick any input has been stamped for
(§7.4). Below that, the tick a rollback still needs has already been overwritten.

Buffers are handed out via `BeginWrite` / `CompleteWrite` so a capture writes straight into
the ring rather than into a temporary that then gets copied.

### 5.6 The framework decides sleeping

PhysX's wake counter was the last piece of *captured* state that refused to replay, and it
refused for a structural reason: when the counter resets, the value it resets to encodes how
many contact *interactions* the body has, through a `dt * (clusterFactor - 1)` term. That is
pair bookkeeping, and pair bookkeeping is exactly what a snapshot cannot carry (§5.2). Two
peers with bitwise identical poses and velocities could reset the same body a timestep apart,
and since sleeping changes whether a body is simulated at all, that is a desync.

So the counter is taken out of play. While a body is awake the native layer pins its counter
at `PX_MAX_F32` — on creation, after every restore, on re-enable, and on commit — so it never
decays, the reset expression is never evaluated, and there is nothing left to read the
bookkeeping. The wake counter is no longer captured or restored at all.

Sleeping is then the framework's decision, made from state the snapshot *does* hold, so it
replays. Each body carries a `restTicks` counter that counts steps spent below the configured
`SimConfig.SleepLinearThreshold` / `SleepAngularThreshold`; once it reaches
`SimConfig.SleepTicks` the body is put to sleep, and `restTicks` rides in the snapshot next to
the pose. Waking is left to PhysX, which wakes a sleeper when a new contact lands on it; the
native `UpdateSleep` pass notices that, clears the counter and re-pins. `SleepTicks = 0`
disables the whole thing and keeps everything awake and pinned, which is the default. The gate
that this replays under rollback is `TestFrameworkSleepReplays` (§16).

---

## 6. The tick lifecycle

This is the core of the framework. `RollbackEngine.Advance()` is called once per fixed
update and performs the **same work every time**, whether or not anything was mispredicted.

### 6.1 The shape of a frame

```
Advance()
│
├─ 1. can we proceed?            confirmed frontier vs fixed horizon      → stall if not
│
├─ 2. AdvanceConfirmed()         at most ONE tick
│      restore(snapshot[c])
│      step(c+1)                 with fully known inputs
│      capture → snapshot[c+1], marked confirmed
│      c := c+1
│
└─ 3. RunPrediction()            ALWAYS exactly PredictionHorizon ticks
       for i in 1..horizon:
           restore(snapshot[c+i-1])   ← before EVERY step, not just the first
           step(c+i)                  with known-or-predicted inputs
           capture → snapshot[c+i], unconfirmed
```

After every call, `CurrentTick == ConfirmedTick + PredictionHorizon`.

The restore inside the prediction loop is the **cold-step discipline**, and it is the least
obvious line in the engine. Restoring state the world already has looks like a no-op, and it
is not: a step following a restore solves from a cold contact cache, a step following
another step solves from a warm one, and the two give different answers (§2.2). Restoring
once at the top of the loop would leave the first step cold and the rest warm, so a replayed
run would differ from an un-replayed one. Restoring before each step makes every step in the
session cold, including steps nobody rolled back, and the distinction stops existing.

Restore before every step. **Exactly one restore, never two** — quaternion normalisation is
not idempotent, so a second restore shifts the rotation by one ULP.

### 6.2 Why prediction replays unconditionally

A conventional rollback engine rewinds only when a prediction turns out wrong. That makes
the amount of work — and therefore the operation sequence — depend on network timing, which
differs per peer. Replaying the full window every frame costs a little throughput and buys
the identical-sequence property.

It also removes the frame-time spike that conditional rollback produces on the frames it
fires. Cost becomes steady and predictable instead of bursty.

### 6.3 Why the confirmed timeline advances at most one tick per frame

This one is subtle and easy to get wrong.

A confirmed tick is computed as `restore(previous); step`. The restore is exact for
everything the snapshot holds — but not for the state PhysX carries between steps, which
comes from whichever step ran immediately before (§5.2). The cold-step discipline makes that
predecessor's *contact cache* irrelevant; it does not make the predecessor irrelevant, since
TGS substep state survives regardless.

So consider a peer that receives three confirmations at once versus one that receives them
across three frames:

```
drains all three:   … step(c+1)  step(c+2)  step(c+3) …
                              ↑ predecessor of step(c+2) is a confirmed step

one per frame:      … step(c+1)  [horizon prediction steps]  step(c+2) …
                                                    ↑ predecessor is a prediction step
```

Same tick, same inputs, different predecessor, therefore different hidden state. The number
of confirmations arriving in a frame is a property of the network, not of the simulation, so
peers would disagree about *confirmed* state purely because their packets clumped
differently.

As with rollback depth (§2.2), a single occurrence produces no visible difference — which is
exactly why this is worth guarding against rather than measuring for. The damage is
cumulative and only becomes observable long after the frames that caused it.

Capping at one keeps every peer's per-frame sequence identical. It is also sustainable:
inputs are produced at the tick rate and `Advance` runs at the tick rate, so one per frame
is the steady state, and bursts are absorbed by the horizon.

### 6.4 Stalling

If confirmation falls further behind than the horizon allows, `Advance()` returns `false`
and simulates nothing.

Stalling is deliberate. Predicting further ahead would make this peer's sequence longer
than everyone else's and desync it *silently*; stalling makes the problem visible, bounded
and recoverable.

The budget being exhausted is `PredictionHorizon + LocalInputDelay - 1` ticks of **one-way**
delivery, not a round trip — 116 ms at the defaults. §7.4 works the timing through and
explains which of the two knobs to widen.

---

## 7. Input flow

### 7.1 Shape

`SimInput` is a fixed-size struct — button bits plus four analogue axes — not an interface.
A per-tick allocation in a loop that replays the whole horizon every frame is the easiest
way to make a rollback engine stutter, and a fixed payload is also trivially serialisable.

`SimInputFrame` holds every player's input for one tick, **ordered by player ID, never by
arrival**. Gameplay iterating a frame must see the same order on every peer, or two peers
apply the same forces in a different order and get different floating-point results.
`InputBuffer` sorts the player list once at construction so slot indices are stable
everywhere.

### 7.2 Prediction

A tick missing a player's input gets one anyway: that player's last known input, repeated.
Simple, and right most of the time, because players hold inputs for many ticks at a time.

When the real input arrives, `Submit` compares it against the guess using `SameCommandAs`,
which ignores the tick, the player and the predicted flag — only the simulation-affecting
fields count. A correct guess costs nothing.

### 7.3 The confirmed frontier

`ConfirmedThrough` is the newest tick for which *every* player's input has actually been
received. It only moves forward, and `RecomputeConfirmedFrontier` walks from its current
position rather than rescanning, so it is O(1) amortised.

This value is what gates §6.2 — it is the boundary between "final" and "guessed".

### 7.4 Local input delay

A peer stamps its own input for `RollbackEngine.LocalInputTick`, which is `CurrentTick`
plus `SimConfig.LocalInputDelay`, rather than for the tick it is simulating right now.

The point is to arrive ahead of the guess. Work the timing through with all peers' confirmed
clocks advancing together, which is what §6.3 enforces. Peer Q stamps an input for tick
`T = c + horizon + delay` while its confirmed tick is `c`. Peer P first *predicts* tick `T`
when its own window reaches it, at confirmed `T - horizon` — which is `delay` ticks of wall
time later. P must *confirm* `T` at confirmed `T - 1`, which is `horizon + delay - 1` ticks
later. So:

| one-way latency | outcome |
| --- | --- |
| ≤ `LocalInputDelay` | the input is in hand before anyone predicts it; no misprediction |
| ≤ `PredictionHorizon + LocalInputDelay - 1` | predicted, then corrected on arrival; no stall |
| beyond | the peers waiting on it stall (§6.4) |

Note that this is one-way delivery in both rows. Nothing in the loop waits for a reply, so
sizing either knob against a round trip overprovisions it by a factor of two.

The delay is **peer-local and not hashed**, which is unusual for a timing field and worth
being clear about. An input carries the tick it applies to and is applied at that tick
whenever it arrives, so a peer delaying by two and a peer delaying by five produce the same
input timeline and simulate identically. Only the horizon has to match, because the horizon
is the length of the per-frame operation sequence (§2.2) and the delay is not.

Which knob to spend is a real choice. The delay costs local responsiveness and nothing else,
and it removes mispredictions outright. The horizon costs a full extra replayed tick every
frame, forever, and only lets a misprediction be corrected more cheaply. Reach for the delay
first, and size the horizon for the jitter tail the delay does not cover.

---

## 8. Data ownership and allocation discipline

The per-tick path must not allocate. Everything it needs is preallocated and reused:

| buffer | owner | lifetime |
| --- | --- | --- |
| Snapshot payloads | `SnapshotRing` | Session. Grown only if the registry grows. |
| Input frames | `InputBuffer` | Session. |
| Pose readback | `DeterministicWorld._poseScratch` | Reused; valid until next `ReadPoses`. |
| Per-entity hashes | `DeterministicWorld._hashScratch` | Reused; valid until next `HashPerEntity`. |

Anything returned from a `ReadPoses` or `HashPerEntity` call is **borrowed**, not owned.
Copy it if it needs to outlive the next call. `Snapshot.ToArray()` exists for when you
genuinely need an independent copy — sending one to a joining peer, for instance.

Native handles are owned by `DeterministicWorld`. `Dispose()` destroys the world, its
registry and its scene together. `SimEntity.UserData` is the one field the framework never
reads, so it cannot influence the simulation — hang your presentation GameObject there.

---

## 9. Joining and resynchronising

A peer joining mid-match has no history, and §2 established that a world with no history
cannot reproduce a running world's trace under any contact reset mode. So the joiner does
not try.

Instead, **every peer rebuilds from one agreed snapshot at one agreed tick**:

```
  server picks resume tick R and the confirmed snapshot S(R)
  ────────────────────────────────────────────────────────────
  every peer, including the joiner:
      CommitPending()              registry agreed
      RestoreState(S(R))
      snapshots.Clear()            old timeline no longer exists
      inputs.Reset(R)
      capture → snapshot[R], confirmed
      confirmed := current := R
```

Afterwards every peer is on an identical history again, which the measurements support: two
worlds rebuilt this way stayed bit-identical for 32 ticks even with their actors registered
in opposite orders.

> **Known gap.** `RollbackEngine.PrepareForRebuild` currently restores into each peer's
> *existing* native world rather than recreating it. That is not what the passing test does:
> `TestRebuiltWorldsAgree` builds two brand-new worlds. The distinction matters because a
> joining peer's world is necessarily fresh while every existing peer's is warmed, and §2.3
> establishes that a world which has never simulated cannot match one that has. The rebuild
> must therefore destroy and recreate the native world on *every* peer — re-registering the
> same actor pointers in stable-ID order — not merely restore into it. Until that lands,
> mid-match join is not correct.

The cost is a brief hitch for everyone rather than only the joiner. Joins are rare, and the
alternative is a joiner that is permanently slightly wrong.

**The same procedure is the recovery path for a detected desync**, and for a peer that fell
so far behind that its rollback target has left the snapshot ring. There is one repair
mechanism, not three.

---

## 10. Where your game plugs in

Everything that touches the simulation goes through `ISimStepHandler`:

```csharp
void OnBeforeStep(DeterministicWorld world, int tick, SimInputFrame inputs, bool isReplay);
void OnAfterStep (DeterministicWorld world, int tick, bool isReplay);
```

A game can implement this directly for simple cases, but the `Gameplay/` layer is the
intended route: it provides one `ISimStepHandler` — `SimGameHost` — for the whole game, with
entities, an action queue, a game mode and players layered above it and a fixed per-tick
order baked in. Gameplay state that must survive a rewind rides in the entity and game
channels through the paired `ISimStateProvider` (§5). See [Gameplay.md](Gameplay.md).

**Why it must all go through here:** a rollback replays ticks. A force applied outside this
callback happens on the original pass and not on the replay, which desyncs a peer against
*itself* — usually the most confusing class of bug in this kind of system.

Handlers must be pure with respect to the tick: given the same world state and the same
inputs, they must do the same thing. In particular, never read wall-clock time,
`UnityEngine.Random`, `Time.deltaTime`, frame counters, or anything derived from local
input outside `SimInputFrame`.

`isReplay` exists to suppress **presentation** side effects — one-shot sounds, particle
bursts, haptics — that would otherwise fire several times per tick. It must never change
what the simulation does.

Handler registration order is part of the simulation, since two handlers applying forces in
a different order produce different floating-point results. Register them from one place at
session start, not from each object's own initialisation.

---

## 11. Presentation

The simulation runs at a fixed tick rate; rendering does not. Read poses with
`DeterministicWorld.ReadPoses()` and interpolate between the last two ticks for display, or
let `SimPresentationBinder` in the gameplay layer do exactly that for every entity.

Keep presentation strictly downstream. Nothing a renderer, animator or camera does may feed
back into the simulation — that is how frame rate leaks into physics and peers with
different hardware drift apart. The one place a camera legitimately touches gameplay is
input: `SimInputEncoder` resolves a peer's movement against its camera locally and networks
only the quantized result, so the camera orientation never enters the simulation. See
[Gameplay.md](Gameplay.md).

---

## 12. Mass properties

Mass looks like local setup but is simulation input, and it is subtler than it appears.

PhysX supports only a diagonal inertia tensor, so it diagonalises the real one and stores
the eigenvector rotation as the body's centre-of-mass orientation. For a body whose
principal moments are close together — anything roughly as wide as it is tall and deep —
those eigenvectors barely exist. A spiked ball whose principal moments differ by 0.25% turns
a 1e-6 m change in shape layout into an 8e-5 rad change in the mass frame: an amplification
of about eighty.

`SimMass` removes that three ways:

1. **Canonical summation order.** Shape contributions are sorted before summing, so the
   result no longer depends on attachment order. Reversing attachment order used to move the
   centre of mass by 1.3e-9 m; it now changes nothing.
2. **Isotropy collapse.** A tensor whose principal moments agree within
   `SimConfig.MassIsotropyTolerance` (default 1%) gets an identity mass frame instead of an
   arbitrary rotation. Sensitivity to a 1e-6 m layout change drops from 4.6e-5 rad to zero.
3. **Canonical quaternion sign**, since a diagonalisation may return either `q` or `-q`.

Collapse has a second benefit. An identity mass frame is exactly the condition under which
the actor pose round trip is lossless, so `setGlobalPose` / `getGlobalPose` becomes bitwise
exact — 240 capture/restore cycles on a spiked ball with no drift.

Even so, **compute once and replicate**. `SimMass.Hash` covers mass, inertia, mass frame and
shape count, so a peer that computed something different is caught at join rather than
diagnosed from a desync twenty seconds later.

> This framework depends on two patches to PhysX itself: a self-validating actor-pose cache
> in `NpRigidDynamic`, and a guard in `Sc::BodyCore::setCMassLocalPose` preventing an
> unchanged mass frame from rotating the actor by an ulp per call. Without them the pose
> round trip is lossy for any body with a rotated mass frame. Do not upgrade PhysX without
> re-applying them and re-running `PxwPoseRoundTripTests`.
>
> A third patch once existed — `PxRigidBody::resetSleepFilter()`, resetting the unsnapshotted
> motion accumulators on restore — but has been removed. It changed no measured number, and
> pinning the wake counter (§5.6) means the accumulators are never read, so it earned nothing
> for the cost of a pure virtual on a public class.

---

## 13. Diagnostics

Determinism bugs are diagnosed after the fact from whatever was recorded at the time, so
`SimLog` is part of the framework rather than debug scaffolding.

**Every message carries a tick.** A message without one cannot be lined up against another
peer's log, which is usually the only way a desync is ever understood. `SimLog.CurrentTick`
is set by the engine as it steps and replays, so a line records the tick it describes rather
than the tick that was current when it flushed. `SimLog.PeerName` lets several peers' logs be
interleaved and read.

**The verbose channel compiles out.** `SimLog.Verbose` is `[Conditional("UNDPWR_VERBOSE_LOGGING")]`,
so a call site in the replay loop costs nothing in a normal build.

`SimLog.AttachNativeSink()` routes PhysX's own diagnostics through the same path. The
delegate is deliberately held in a static field — native code keeps the function pointer
indefinitely, and a delegate that exists only as a call argument becomes collectable the
moment the call returns, producing a crash much later that looks nothing like its cause.

---

## 14. Configuration

`SimConfig` is the single place where simulation-affecting settings live, so they can be
hashed as a unit and checked at join. **A field that changes the simulation and is not in
`SimConfig` is a determinism bug waiting to happen.**

Everything is set explicitly rather than inherited from a PhysX default, because a default
that shifts between SDK versions is a silent break. Two fields are fixed in code rather than
exposed — the pruning structure and the solver type — because they change query results and
solver behaviour respectively, and a peer that answered a query differently would take a
different gameplay decision.

`ComputeHash()` covers tick rate, horizon, gravity, solver iterations, thresholds, density,
isotropy tolerance, sleep policy and backend. It deliberately excludes diagnostics-only
fields such as `DisablePvd`, so a peer running with the debugger attached is not rejected.

The sleep fields are in the hash for a reason worth spelling out: a peer that slept bodies on
a different schedule from everyone else would produce state that diverges only once something
settles, which is late, quiet, and hard to attribute. Better to refuse the
join.

### The GPU backend

`SimBackendMode.GpuExperimental` exists, but PhysX gives no cross-machine determinism
guarantee for GPU simulation — results depend on driver, card, and the scheduling of
thousands of concurrent blocks. A networked world refuses to start in that mode unless
`AllowExperimentalGpuNetworking` is set, and logs why. Use it for single-player or
presentation-only worlds.

---

## 15. Failure modes

| symptom | likely cause |
| --- | --- |
| `EntryMismatch` on restore | Peers built different worlds. Compare registry contents and `SimConfig.ComputeHash()`. |
| Immediate, large divergence | Different CPU architecture (see [CrossPlatformDeterminism.md](CrossPlatformDeterminism.md)), or a config mismatch. |
| Slow drift over many ticks | Something affecting the simulation from outside `ISimStepHandler`. |
| One entity diverges, rest agree | Mass properties, or that object's handler reading non-deterministic state. Use `HashPerEntity`. |
| Desyncs only when someone spawns | Stable ID allocated outside the tick loop — check for the allocator warning. |
| Desyncs only under load | Frame-rate-dependent logic leaking in, or `Advance` being called a variable number of times. |
| Desync appears only once a scene settles | Peers disagree on the sleep parameters, or on `SleepTicks` (§5.6). Confirm the config hash matches. |
| Constant stalling | `PredictionHorizon` too small for actual latency, or a peer that cannot hold tick rate. |
| Works in editor, fails in build | Verbose logging changing timing, or a `[Conditional]` path with side effects. |

---

## 16. Current status

**Implemented:** the native determinism layer (registry, capture/restore/hash, deterministic
mass, per-scene stepping, contact reset modes, sleep policy, log callback), the interop
bindings, `SimConfig`, `DeterministicWorld`, `StableIdAllocator`, `SnapshotRing`, `SimMass`,
`SimLog`, `SimInput` / `InputBuffer`, and `RollbackEngine`.

**State channels and gameplay layer (managed):** the three-channel snapshot
(`SimStateWriter`/`SimStateReader`, entity and game sections with per-channel hashes,
`ISimStateProvider`), and the whole `Gameplay/` layer — entities and pooling, actions, game
modes, the game host, players, camera-relative input and presentation binding — are
implemented in managed code with EditMode determinism tests for the parts that need no native
world.

**Known gap:** `PrepareForRebuild` restores into the existing native world instead of
recreating it, so mid-match join is not yet correct — see the note in §9. For the managed
channels it captures the provider's current state, so the game layer must apply the agreed
managed state before calling it.

**Framework sleeping** (§5.6) is the deterministic answer to resting bodies: it is driven
from snapshotted state so that it replays, and is off by default (`SleepTicks = 0`). The one
part still on trust is a sleeper woken by a *new* contact under rollback, which
`TestFrameworkSleepReplays` does not yet exercise.

**Specified, native side pending:** forces (`AddForce`/`AddTorque`), scene queries with
stable-ID-sorted hits, and the contact/trigger event buffer are specified in
[NativeGameplayApi.md](NativeGameplayApi.md) with matching `NativeMethods` declarations and
managed wrappers (`SimBody`, `SimQuery`, `SimContacts`), so gameplay compiles against them;
the native implementations are the remaining work before that gameplay runs.

**Not yet implemented:** the transport interface and wire messages that carry the
synchronised rebuild; articulation and vehicle rollback state beyond the generic path; the
multi-peer test harness; editor tooling; the sample scene.

Where this document describes something in that second list — principally the message flow
in §9 — it describes the intended design, and the mechanism it rests on
(`PrepareForRebuild`) does exist.
