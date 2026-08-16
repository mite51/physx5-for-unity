# Concepts

This chapter explains what the framework is doing and why, so the rules in the rest of the
manual read as consequences rather than arbitrary constraints. You can build a game without
it, but every "do not do X" elsewhere traces back to something here.

## Canonical commands on the wire

The authoritative server and every client simulate the whole physics world locally at a fixed
tick rate. Clients propose inputs and deterministic events; the server assigns their ticks and
broadcasts canonical frames. Positions are sent only for exceptional rebuilds, so ordinary
bandwidth remains a function of player count rather than scene size.

That only works if the simulations genuinely agree, to the bit, forever. Most of this
framework exists to make them agree and to notice immediately when they do not.

## The governing rule

> **The server owns the confirmed timeline, and under PGS each confirmed tick is a pure
> function of its predecessor snapshot and canonical command frame.**

Two different questions hide inside "is it deterministic", and conflating them causes most of
the confusion:

- **Server agreement** — does a client computing a confirmed tick match the authoritative
  server? This is the agreement the framework checks.
- **Self-transparency** — does a replayed tick match what the same peer computed the first time
  through? This matters only because, when it holds, peers that did *different amounts of work*
  (rolled back different depths, predicted different distances) still land on identical
  confirmed state.

The framework relies on server agreement. It never compares a client against a hypothetical
un-rewound version of itself.

## Why PhysX makes this hard

PhysX carries state between steps that no public API can read or write: warm-start contact
impulses in persistent manifolds, friction anchors, broadphase pair bookkeeping, island
assignments, and (under TGS) per-substep working state. None of it fits in a snapshot. So a
restored world always carries hidden state from whatever it simulated *before* the restore.

The instinct is that this makes a rewind lossy, and that two peers rewinding by different
amounts must therefore drift apart. That instinct is wrong, and measuring it is what shaped
the design:

- Restoring a snapshot and stepping is a **pure function of the snapshot**. Two worlds driven
  along completely different histories, then handed the same snapshot, stay bit-identical for
  as long as they are stepped together.
- The real asymmetry is that a step taken straight after a restore runs *cold* (caches
  cleared) and a step after another step runs *warm*, and the two do not agree.

## Cold steps

The fix is to make every step cold: **restore the snapshot before every step, including the
steps nobody rolled back.** Restoring state a world already has looks like a no-op and is not —
it cools the caches, so a replayed tick and a never-rolled-back tick take the identical path.

Under PGS this is enough to make replay bitwise transparent. Under TGS it is not: the solver's
per-substep state survives the restore, leaving a residual that is invisible for a few hundred
frames and then flips a bit long after the frame that caused it. That is why a networked
session **requires PGS**. UNDPWR fixes the solver to PGS rather than exposing an unsafe TGS
selection. See [Configuration](Configuration.md).

## Free-running clock and conditional rollback

Prediction is not a fixed shared horizon. `TargetTick` advances once per `FixedUpdate`, while
`CurrentTick` may trail during budgeted replay. Each `Advance()`:

1. drains whatever the confirmed frontier reached into the confirmed timeline, one cold
   restore-and-step per tick, capturing each;
2. advances the wall-clock target one tick when history permits;
3. resimulates from the earliest disturbed tick toward the target, stopping after the
   configured complete-tick work budget.

A client that falls beyond the local recovery budget requests a server rebuild rather than
entering an unbounded replay spiral. [Rollback and input](RollbackAndInput.md) covers the tick
lifecycle in detail.

## Latency: adaptive lead and bounded recovery

`SimAdaptiveInputLead` targets measured server round-trip latency plus jitter and a safety
margin. It rises quickly after retiming and falls slowly after a stable period. Immediate feel
comes from presentation anticipation, not from a zero-delay simulation mode.

`SnapshotHistory`, `MaxSimulationStepsPerFrame`, and `HardResyncTicks` bound local correction
cost. Defaults are 64 retained ticks, 8 simulation steps per Unity frame and rebuild after a
30-tick backlog. See [Configuration](Configuration.md).

## Three state channels

A snapshot carries three channels, captured and restored together and hashed apart:

| channel | holds | written by |
| --- | --- | --- |
| physics | pose, velocity, sleep, articulation and vehicle integrator state | the native layer |
| entity | per-entity managed state, in stable-ID order | `SimGameEntity.CaptureState` |
| game | the game mode's own state and the pending action log | `ISimGameState` + `SimActionQueue` |

Hashing them separately lets a desync report say *physics*, *entity 4102* or *game state*
instead of only "the worlds differ". The physics-only path (bare `ISimStepHandler`) uses only
the first channel; the gameplay layer adds the other two. See [The gameplay layer](Gameplay.md).

## Identity: stable IDs

Agreement on *which actor is which* cannot come from spawn order, instance IDs, or scene
traversal — those differ per peer. It comes from a **stable ID** every peer computes the same
way. [`StableIdAllocator`](../Core/StableIdAllocator.cs) partitions the ID space into authored,
deterministic-runtime and local-only ranges, and actors enter the PhysX scene in stable-ID
order regardless of the order gameplay created them. This is the single most important thing
the layer does; [World and actors](WorldAndActors.md) covers it.

## What follows from all this

Everything else in the manual is a rule that keeps the confirmed timeline reproducible:

- All simulation effects go through the step handler, so they happen on the replay too.
- Actors are registered in stable-ID order, deferred to a commit at the tick boundary.
- Presentation is strictly one-way: poses flow out, nothing flows back in.
- Mass is computed once and replicated, and hashed so a mismatch is caught at join.
- The solver must be PGS, and all peers must share a CPU architecture.

Read on for how each of those looks in code.
