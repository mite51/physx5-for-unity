# Concepts

This chapter explains what the framework is doing and why, so the rules in the rest of the
manual read as consequences rather than arbitrary constraints. You can build a game without
it, but every "do not do X" elsewhere traces back to something here.

## Inputs on the wire, nothing else

Every peer simulates the whole physics world locally, at a fixed tick rate, from the same
inputs. Only inputs cross the network. Because every peer computes the same result, no one
sends positions, so bandwidth is a function of player count and does not grow with the physics
scene.

That only works if the simulations genuinely agree, to the bit, forever. Most of this
framework exists to make them agree and to notice immediately when they do not.

## The governing rule

> **Only the confirmed timeline is compared between peers, and under PGS it is a pure function
> of the snapshot before each tick.**

Two different questions hide inside "is it deterministic", and conflating them causes most of
the confusion:

- **Peer agreement** — do two peers computing the same tick agree with each other? This is the
  one that matters, and the one the framework guarantees.
- **Self-transparency** — does a replayed tick match what the same peer computed the first time
  through? This matters only because, when it holds, peers that did *different amounts of work*
  (rolled back different depths, predicted different distances) still land on identical
  confirmed state.

The framework relies on peer agreement. It never compares a peer against a hypothetical
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
session **requires PGS** — [`SimConfig.Validate`](../Core/SimConfig.cs) refuses any other
solver. See [Configuration](Configuration.md).

## Free-running clock and conditional rollback

Prediction is not a fixed shared horizon. The clock advances one tick per `FixedUpdate`, and
how far a peer runs ahead of the confirmed tick is *emergent*: it grows when confirmations lag
and shrinks as they arrive. Each `Advance()`:

1. drains whatever the confirmed frontier reached into the confirmed timeline, one cold
   restore-and-step per tick, capturing each;
2. advances the clock one tick, unless doing so would outrun the snapshot ring;
3. resimulates the prediction window — but only from the earliest tick a misprediction or a
   new confirmation actually disturbed.

A peer that would lead further than the snapshot ring can retain **stalls** — a visible pause —
rather than silently losing state a late input still needs. Because confirmed hashes never
depended on how far anyone predicted or rewound, peers that did different amounts of work still
agree. [Rollback and input](RollbackAndInput.md) covers the tick lifecycle in detail.

## Latency: two peer-local knobs

Two settings shape how a late packet is handled, and neither has to match between peers because
neither changes the simulation:

- **`SimConfig.LocalInputDelay`** stamps a peer's own input that many ticks ahead of the tick
  it is simulating. An input that crosses the network faster than the delay arrives before
  anyone predicts it — so there is nothing to mispredict. The cost is local responsiveness: the
  player's own action happens `LocalInputDelay` ticks after they asked for it.
- **`SimConfig.SnapshotHistory`** bounds how far the clock may lead the confirmed tick before
  the ring can no longer retain the whole live window. That cap
  (`SnapshotHistory - LocalInputDelay - 1` ticks of lead) is where a peer stalls.

At the defaults — delay 2, history 32, 60 Hz — inputs under ~33 ms never mispredict, and a peer
can lead by ~29 ticks (~480 ms) before the ring bounds it. Both knobs are peer-local and not
hashed: a peer on a worse link simply leads less. See [Configuration](Configuration.md).

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
