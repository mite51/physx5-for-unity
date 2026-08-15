# Troubleshooting

Two things go wrong with a rollback session: peers **disagree** (a desync), or a peer **stalls**.
This chapter maps symptoms to causes and lists the classic mistakes.

## When peers disagree

Desync detection is mandatory and fatal, so a divergence surfaces as a `SimDesyncReport` (and,
by default, a fatal one) naming the tick and the channel — physics, entity or game. Use the
channel to narrow it down.

| symptom | likely cause |
| --- | --- |
| `EntryMismatch` on restore | Peers built different worlds. Compare registry contents and `SimConfig.ComputeHash()`. |
| Immediate, large divergence | Different CPU architecture (see [Limits and platforms](LimitsAndPlatforms.md)), or a config mismatch that slipped past the handshake. |
| Slow drift over many ticks | Something affects the simulation from outside the step handler. |
| One entity diverges, the rest agree | Mass properties, or that entity's update reading non-deterministic state. Turn on `PerEntityHashDiagnostics`. |
| Desyncs only when someone spawns | A stable ID allocated outside the tick loop — check for the allocator warning. |
| Desyncs only under load | Frame-rate-dependent logic leaking in, or `Advance` called a variable number of times per second. |
| Desync only once a scene settles | Peers disagree on sleep parameters or `SleepTicks`. Confirm the config hash matches. |
| Works in editor, fails in build | A `[Conditional]` verbose-logging path with side effects, or logging changing timing. |

### Naming the body in a physics desync

Set `SimConfig.PerEntityHashDiagnostics = true` on **every** peer. Each peer then logs its own
per-entity hash table for the disagreeing tick and sends it to the others; the entry whose hash
differs between two peers' logs is the diverged body, and the entries that agree rule themselves
out. It costs a native walk over every entry each confirmed tick, so leave it off until you need
it. (A registration-order mismatch is already reported as a named line without this flag.)

### The entity- or game-channel version

A desync in the entity or game channel is almost always a broken `Capture`/`Restore` pair: a
field captured but not restored, read in a different order, or a future-scheduled action type
registered in a different order on different peers (which surfaces as an index-out-of-range on
restore). The writer and reader have no tags, so the two must be exact inverses — see
[The gameplay layer](Gameplay.md#the-one-contract).

## When a peer stalls

`engine.IsStalled` is true and `Advance()` returns `false`.

| symptom | likely cause |
| --- | --- |
| Constant stalling | `SnapshotHistory` too small for the lead the latency demands, or a peer that cannot hold the tick rate. |
| Permanent stall, input does nothing | A **gap in the input run** — a tick was never submitted for some player, so the confirmed frontier can never pass it. |
| A single peer stalls everyone | That peer stopped submitting (hitched longer than the buffer), and needs a rebuild. |

The permanent-stall case is the important one and is almost always the single-tick-per-frame
mistake below.

## The classic mistakes

- **Submitting one tick per frame instead of a run.** `LocalInputTick` starts `LocalInputDelay`
  ticks ahead of the clock, so stamping a single tick leaves a gap the confirmed frontier never
  crosses, and the peer stalls for good with input appearing dead. Use
  `SimSession.SubmitLocalInput`, or loop from the tick after the last one you submitted through
  `LocalInputTick`. See [Rollback and input](RollbackAndInput.md#submitting-input-without-stalling).
- **Applying forces outside the step handler.** A force applied from `Update`, a coroutine or a
  collision callback happens on the original pass and not the replay, and the peer desyncs
  against itself. All simulation effects go through the step handler / sim callbacks.
- **Letting a Unity actor add itself to the scene via `OnEnable`.** The world adds actors in
  stable-ID order at commit; a double-add or a different order between peers is a desync that
  looks like a physics bug. Create actors with `externalSceneMembership = true` against
  `SimActorBridge.CreateWorldScene(world)`. See [World and actors](WorldAndActors.md).
- **Allocating a runtime stable ID outside the tick loop.** From a UI callback, coroutine or
  network handler it happens at a different point on each peer and desyncs the allocator itself.
  Allocate inside `OnSimUpdate`/`OnBeforeStep`; the allocator warns when the tick goes backwards.
- **Reading a transform (or `Time.deltaTime`, or `UnityEngine.Random`) into the simulation.**
  Presentation is strictly one-way. Any render-rate quantity that reaches the sim leaks frame
  rate into physics and diverges peers with different hardware.
- **Branching hashed state on a contact impulse or a wake tick.** Neither is bit-exact across a
  rollback. Branch on *which* bodies touched, and on the fact that a body woke — never the exact
  values. See [Limits and platforms](LimitsAndPlatforms.md).
- **Not replicating mass.** Compute once and replicate; rely on `SimMass.Hash` to catch a
  mismatch at join rather than diagnosing it as a slow drift later.

## Logs

[`SimLog`](../Diagnostics/SimLog.cs) tags every message with the tick it describes
(`SimLog.CurrentTick`, set by the engine as it steps and replays) and `SimLog.PeerName`, so two
peers' logs can be interleaved and lined up — usually the only way a desync is understood. Call
`SimLog.AttachNativeSink()` at startup to route PhysX's own diagnostics through the same path.
The verbose channel (`SimLog.Verbose`) compiles out unless `UNDPWR_VERBOSE_LOGGING` is defined,
so leave verbose call sites in the replay loop; they cost nothing in a normal build.
