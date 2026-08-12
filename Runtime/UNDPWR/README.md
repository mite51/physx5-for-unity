# UNDPWR — Unity Networked Deterministic Physics With Rollback

A rollback netcode framework for PhysX 5 in Unity. Peers exchange only player inputs;
every peer recomputes the physics identically, so bandwidth stays flat as the scene
grows.

## The one rule everything follows

**Only the confirmed timeline is compared between peers, and under PGS it is a pure
function of the snapshot before each tick.**

Everything below is a consequence of that. Most of the design's unusual choices exist to
keep the confirmed timeline reproducible while letting each peer predict as far ahead as its
own latency demands.

The subtlety is forced on us by how PhysX works. It carries state between steps that no
public API can read or write — warm-start contact impulses in persistent manifolds, friction
anchors, broadphase pair bookkeeping, TGS substep state — so none of it can go in a snapshot.
Clearing the caches on a rollback makes things worse rather than better, because it discards
warm-start data the original tick genuinely had.

The obvious fear — that a rewind is lossy, so two peers rewinding by different amounts drift
apart — turned out to be the wrong one. `restore(S); step()` was measured to be a pure
function of `S`: worlds driven along deliberately different histories converge exactly once
they are handed the same snapshot. Under PGS, with the cold-step discipline below, that holds
to the bit, so a peer that rewound four ticks and one that rewound sixteen agree. Under TGS it
does not — a residual far below the resolution of captured state accumulates for a few hundred
frames and flips a bit long after the frame that caused it — which is why a networked session
must run PGS.

So each peer rewinds only as far as a misprediction reaches and leads by whatever its network
allows, and the confirmed hashes still agree because they never depended on either. The
measurements behind this are in
[Documentation/DeterminismInvestigation.md](Documentation/DeterminismInvestigation.md).

## What follows from the rule

**Prediction is free-running, not a fixed shared horizon.** The clock advances one tick per
fixed update, and how far a peer runs ahead of the confirmed tick is emergent — it grows when
confirmations lag and shrinks as they arrive. Each `Advance` replays only the ticks a
misprediction or a new confirmation actually disturbed, not a whole window. A peer that would
run further ahead than `SimConfig.SnapshotHistory` can retain *stalls* — a visible pause
instead of silently losing the state a late input still needs.

**Mid-match join is a synchronised rebuild, not a catch-up.** A joiner has no history and
cannot manufacture one. But two worlds built from scratch and restored from the same
snapshot were measured bit-identical for thirty-two ticks — even with their actors
registered in opposite orders. So the joiner does not chase the others: every peer
restores one agreed snapshot at one agreed tick and continues from there. Brief hitch for
everyone, joins are rare, and afterwards all peers are back on an identical footing. The
same procedure is the recovery path when a desync is detected.

**Confirmed-tick hashes can be compared bit-for-bit.** Because of the points above, and a
mandatory confirmed-hash check is what verifies the PGS transparency the whole scheme rests
on.

## Latency

Two peer-local knobs bound how a late packet is handled, and neither has to be agreed on.

`SimConfig.LocalInputDelay` stamps a peer's own input that many ticks further ahead than the
tick it is currently simulating. An input is first *guessed* by the other peers
`LocalInputDelay` ticks after its sender produced it, so anything crossing the network faster
than the delay arrives before anyone predicts it — there is nothing to mispredict and nothing
to correct. Past the delay, prediction and rollback take over.

`SimConfig.SnapshotHistory` bounds how far ahead of the confirmed tick the clock may run
before the ring can no longer retain the whole live window. That cap — `SnapshotHistory -
LocalInputDelay - 1` ticks of lead — is the point a peer stalls at rather than a shared
constant, so a peer on a worse link simply leads less.

| condition | consequence |
| --- | --- |
| one-way latency ≤ `LocalInputDelay` | that input is never predicted, so it never mispredicts |
| within the lead the ring allows | predicted, corrected on arrival, no stall |
| beyond that | the peer waiting on it stalls until confirmation catches up |

At the defaults — delay 2, history 32, 60 Hz — inputs under 33 ms are simulated exactly, and
a peer can lead by up to 29 ticks (about 480 ms) before the ring bounds it.

Both knobs are **peer-local and not hashed**. An input carries the tick it applies to and is
applied at that tick whenever it arrives, so a peer delaying by two and a peer delaying by
five simulate the identical input timeline; likewise a peer that retains more history just
tolerates a later input and a larger lead. `LocalInputDelay` costs local responsiveness and
nothing else, which makes it the knob to reach for.

## Determinism hazards this framework handles for you

**Actor insertion order.** PhysX only guarantees reproducible results when actors enter
the scene in the same order, and gameplay code spawns things in whatever order it likes.
`DeterministicWorld.Register` therefore only records intent; actors reach the scene at
`CommitPending`, sorted by stable ID. This was the hardest lesson from the original
UNDPWR and it is the single most important thing the layer does — getting it wrong
produces a desync that looks like a physics bug.

**Stable IDs.** Identity must come from something every peer computes the same way, never
from spawn order or instance IDs. `StableIdAllocator` partitions the range into authored,
deterministic-runtime and local-only, and warns when a runtime ID is allocated from
outside the tick loop.

**Mass properties.** PhysX supports only a diagonal inertia tensor, so it diagonalises the
real one and stores the eigenvector rotation as the centre-of-mass orientation. For a body
whose principal moments are close together — anything roughly as wide as it is tall and
deep — those eigenvectors barely exist. A spiked ball whose principal moments differ by
0.25% turns a 1e-6 m change in shape layout into an 8e-5 rad change in the mass frame, an
amplification of about eighty.

`SimMass` removes that three ways: shape contributions are sorted into a canonical order
before summing (reversing attachment order used to move the centre of mass by 1.3e-9 m); a
near-isotropic tensor gets its mass frame collapsed to identity instead of an arbitrary
rotation; and otherwise the quaternion is given a canonical sign. Compute once and
replicate anyway — `SimMass.Hash` catches a mismatched peer at join rather than twenty
seconds into a match.

**Contact caches are left alone.** Clearing them on rollback feels right and is wrong.
Measured over a 30-tick replay: leaving them alone gives 1.8e-06 m error, `resetFiltering`
gives 1.4e-02 m, `reinsert` gives 9.8e-02 m. Clearing discards warm-start data the
original tick actually had. The rollback path never clears them; the other
`SimContactResetMode` values are a manual hard-resynchronisation tool only, where discarding
history is the point.

**Every step is preceded by a restore**, including steps nobody rolled back. Restoring
state a world already has looks like a no-op and is not: it cools the contact cache, and a
cold step and a warm step do not give the same answer. Making every step cold removes the
asymmetry. Under PGS that is enough to make a replayed tick match an un-replayed one, which is
why a networked session requires PGS; under TGS the hardcoded solver keeps substep state that
survives the restore, so a small residual divergence remains and a data-dependent rewind
diverges. See [the investigation](Documentation/DeterminismInvestigation.md).

**The framework decides sleeping, not PhysX.** PhysX's wake counter, when it resets, encodes
how many contact interactions the body has — bookkeeping a snapshot cannot carry — so two
peers with bitwise identical poses could reset it a tick apart. So the wake counter is pinned
high while a body is awake, PhysX's sleep path never runs, and the framework sleeps a body
once it has stayed below the configured speed thresholds for `SimConfig.SleepTicks` ticks. The
rest counter that drives that is in the snapshot, so it replays. Sleeping is off by default
(`SleepTicks = 0` keeps everything awake); set it to turn it on.

## Changelog

[CHANGELOG.md](CHANGELOG.md) tracks what has landed and, critically, every change to the two
numbers that decide whether two peers interoperate: the managed config hash and the native
`kStateVersion`.

## Layout

| folder | contents |
| --- | --- |
| `Interop/` | P/Invoke bindings and blittable structs mirroring `PxwUndpwr.h`. Internal — calling it directly bypasses stable-ID ordering. |
| `Core/` | `SimConfig`, `DeterministicWorld`, `SimEntity`, `StableIdAllocator`, `SnapshotRing`, `SimMass`, `SimStateWriter`/`SimStateReader`. |
| `Rollback/` | `SimInput`, `InputBuffer`, `RollbackEngine`, `ISimStepHandler`, `ISimStateProvider`. |
| `Gameplay/` | The gameplay layer over the engine: entities and pooling, actions, game modes and the game host, players and camera-relative input, presentation binding. See [Documentation/Gameplay.md](Documentation/Gameplay.md). |
| `Diagnostics/` | `SimLog`, including the native diagnostic sink. |
| `Tests/` | EditMode determinism tests for the managed layers. |
| `Documentation/` | Long-form guides — see [the index](Documentation/README.md). |

[`Documentation/Architecture.md`](Documentation/Architecture.md) is the detailed treatment:
layers and dependency direction, the stable-ID scheme, what a snapshot does and does not
contain, a step-by-step walk through the tick lifecycle, input flow and prediction, data
ownership, the join and resync procedure, where gameplay plugs in, and a failure-mode table.

## Using it

```csharp
var config = SimConfig.Deterministic;
config.TickRate = 60;
config.LocalInputDelay = 2;     // peer-local; 33 ms of mispredict-free budget
config.SnapshotHistory = 32;    // peer-local; bounds how far ahead the clock may lead

SimLog.AttachNativeSink();

var world = new DeterministicWorld(config);
var ids = new StableIdAllocator(sessionSeed);

// Register actors. Order does not matter; insertion is sorted by stable ID.
world.Register(crateId, cratePtr, SimHandleKind.RigidDynamic);
SimMass.Setup(cratePtr, config.DefaultDensity, config.MassIsotropyTolerance);

var engine = new RollbackEngine(world, playerIds);
engine.AddHandler(new MyGameplay());   // all simulation effects go through here
engine.Initialise();

int nextLocalTick = engine.CurrentTick;

// once per fixed update
for (; nextLocalTick <= engine.LocalInputTick; ++nextLocalTick)   // not engine.CurrentTick
{
    engine.SubmitInput(SampleLocalInput(nextLocalTick));
}
engine.Advance();
```

Submit a *run* of ticks, not one. Confirmation needs an unbroken stream from each player, and
one missing tick stalls that player's peers for good rather than briefly — so every tick from
the last one submitted through `LocalInputTick` has to be covered. Stamping a single tick per
frame opens a gap at the start: `LocalInputTick` begins `LocalInputDelay` ticks ahead of the
clock, so nothing covers the ticks between the tick the session starts at and that first stamp.
`SimSession.SubmitLocalInput` fills the run for you; the loop is only for driving the engine bare.

Everything that touches the simulation must go through `ISimStepHandler`. A force applied
outside it happens on the original pass and not on the replay, which desyncs a peer
against itself.

## Gameplay layer

`ISimStepHandler` is the floor, not the ceiling. The `Gameplay/` folder builds the rest of a
game on it: entities whose managed state rolls back, a pool that spawns by enabling a dormant
instance, an action queue, a game mode that owns its own rollback state, players with
camera-relative input, and presentation interpolation. `SimGameHost` ties it together as the
single step handler and state provider, so the per-tick order is fixed in one place.

The rollback model reaches gameplay through three state channels — physics, entity and game —
each captured, restored and hashed together, so health, scores and the action log survive a
rewind the same way poses do. [Documentation/Gameplay.md](Documentation/Gameplay.md) is the
walkthrough; [Documentation/NativeGameplayApi.md](Documentation/NativeGameplayApi.md) specifies
the forces, scene queries and contact events the layer needs from the native plugin.

## GPU backend

`SimBackendMode.GpuExperimental` exists but PhysX gives no cross-machine determinism
guarantee for GPU simulation: results depend on driver, card, and the scheduling of
thousands of concurrent blocks. A networked world refuses to start in that mode unless
`AllowExperimentalGpuNetworking` is set, and logs why. Use it for single-player or
presentation-only worlds.

## Platform support

Peers must share a CPU architecture. PhysX compiles different arithmetic on x86 and ARM —
Android falls through to a scalar backend while x86 uses SSE, and their approximate
reciprocals differ by about 3.7e-4, four orders of magnitude above the framework's noise
floor. Mixing them does not drift apart slowly; it never agrees at all.

[`Documentation/CrossPlatformDeterminism.md`](Documentation/CrossPlatformDeterminism.md) has
the full analysis, what it would take to change, and a cheap test plan — including one test
worth running on desktop today, since Intel and AMD may not agree either.

## Where the numbers come from

Every measurement quoted here is produced by `tests/PxwUndpwrTests.cpp` and
`tests/PxwPoseRoundTripTests.cpp` in the native plugin repository. They are characterisation
tests as much as regression tests: they exist so the limits stay documented rather than
being rediscovered.
