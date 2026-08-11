# Plan: adaptive rollback

The netcode this framework wants to end up with is closer to Overwatch's than to lockstep's:
roll back rarely, roll back only as far as a correction actually requires, and never let one
peer's packet timing decide when another peer's simulation advances.

Peer-to-peer determinism is a fixed requirement, not a variable. Only inputs cross the wire.
Nothing below proposes an authoritative simulation server or state replication.

This document is the route from here to there. It is written to be abandoned partway if the
measurement in phase 1 comes back the wrong way, so each phase states what it needs from the
one before and what to do if it does not get it.

---

## 1. Where the current design falls short of that goal

Three gaps, all of them consequences of one thing.

**The local clock is not free-running.** This is the big one, and it is easy to miss from
reading `SimConfig.PredictionHorizon` alone. After every prediction run,
`_currentTick == _confirmedTick + PredictionHorizon` exactly, so on the next call
`_currentTick >= targetTick` is always true and the stall condition in
`RollbackEngine.Advance` reduces to "no new confirmation arrived". The simulation advances
one tick per *confirmed* tick and freezes otherwise. The horizon is a constant display lead,
not a buffer the clock can spend down: a peer whose packet is late does not coast on its
prediction, it stops, and so does everyone waiting on it.

That is lockstep behaviour. It is a defensible design — it is roughly GGPO with a fixed
window — but it is the opposite of the goal above, and no amount of tuning the horizon
changes its shape.

**Rollback is unconditional and full-width.** `RunPrediction` replays the entire window every
frame whether or not anything was mispredicted: `1 + PredictionHorizon` restore-and-step pairs
per frame, forever. "Minimise rollbacks" is not a tuning problem here, it is a structural one.

**Rollback depth is fixed rather than fitted to the correction.** A misprediction at
`c + 5` and one at `c + 1` cost exactly the same, because the engine does not look at where
the correction landed.

All three exist to hold one property: every peer performs an identical sequence of operations
every tick. Remove the need for that property and all three go away together.

---

## 2. The gate: replay transparency

Variable rewind depth across peers is safe if and only if `restore(S); step()` gives the same
answer regardless of what the world did before the restore. `DeterminismInvestigation.md`
measured this directly, and the answer depends entirely on the solver:

| configuration | variable depth across peers |
| --- | --- |
| PGS, cold steps, wake counter pinned | **bit-identical for 600 frames**, provided no contact chain exceeds 8 bodies (§9) |
| TGS, cold steps, wake counter pinned | fails on the first replayed step, 3e-09 m/s on a loose grid (§8) |

`SimConfig.ToSceneDesc` hardcodes TGS. So the entire plan reduces to one question, and it is a
question about the solver, not about the netcode:

> **Can this framework run PGS?**

Everything in phases 2 and 3 is unblocked if the answer is yes and blocked if it is no. Nothing
in phases 2 and 3 is worth designing in detail before it is answered.

The existing evidence leans yes, from an unexpected direction. TGS was chosen for stack
stability, and on the 16-high stack — the case warm starting exists to serve — it does not
have it once every step is cold. `MeasureColdStepCost` prints both solvers cold, which is the
comparison the framework actually faces since it never steps warm:

| 16-high stack, cold steps | settled height | residual velocity |
| --- | --- | --- |
| PGS | 1.499999 | 0.000146 m/s |
| TGS | 1.499998 | 0.003556 m/s |

PGS is about 24x quieter at rest. That is one workload and one metric — box stacks say nothing
about articulations, vehicles or high mass ratios, which is exactly what phase 1 is for — but
the reason TGS was picked does not survive contact with the cold-step discipline.

---

## 3. Phase 0 — local input delay (done)

`SimConfig.LocalInputDelay` stamps local input `d` ticks ahead of the tick being simulated, so
an input that crosses the network in under `d` ticks is in hand before anyone predicts it.
Architecture §7.4 works the timing through.

This is independent of everything above: it does not touch the operation sequence, it is
peer-local, and it is not hashed. It reduces misprediction *frequency* today, and it is what
makes conditional rollback worth having later — conditional rollback only pays off if most
frames have nothing to correct, and the delay is what makes most frames have nothing to
correct.

One fix came with it. `InputBuffer.Submit` previously reported a misprediction for any input
landing on a recycled frame slot, because `EnsureFrame` marks a recycled frame predicted and
an empty slot is indistinguishable from a guess. With input stamped ahead, that is every local
input, every tick. The buffer now tracks the newest tick actually served by `GetOrPredict` and
reports a misprediction only at or below it, so the return value means what its documentation
says. Phase 2 depends on that return value being trustworthy.

---

## 4. Phase 1 — the solver decision

Remaining work happens in `physx5-native-plugin`, in `tests/PxwRollbackRepro.cpp`. The default
solver does not change until this concludes.

**The solver is now a variable (done).** `SimConfig.Solver` replaces the hardcoded
`desc.SolverType = 1`, and it is hashed, because a session with one peer on PGS and one on TGS
is not a session. The default is unchanged at TGS: the field exists so the experiment can be
run, not so the default can be changed casually. No native work was needed —
`PxwSceneDesc.solverType` was already passed through to `PxSceneDesc` unmodified; only the
managed side was pinning it.

**The measured baseline**, from `PxwRollbackRepro` as it stands today (22 checks, 0 failures):

| scene | solver | replay transparent | peers at differing depths |
| --- | --- | --- | --- |
| 4×4 grid | PGS | yes | **600 frames bit-identical** |
| 4×4 grid | TGS | no | diverges |
| 16-high stack | PGS | yes | diverges |
| 16-high stack | TGS | no | diverges |
| contact chain ≤ 8 | PGS | — | **survives** (asserted) |

Note the third row, because it is the one that decides the phase. Transparency and variable
depth are not the same property: PGS replay is transparent even on a 16-high stack, and peers
at differing depths still diverge there. Transparency is necessary and not sufficient, and the
chain-depth limit is what stands between the two.

**Answer in this order, and stop at the first no.**

1. *Does PGS hold variable depth on the scene shapes this game actually has?* Extend
   `TestVariableDepthUnderColdSteps` past its synthetic grids and columns to the real
   workloads. Exit criterion: two peers rewinding by different, varying amounts stay
   bit-identical for 600 frames.
2. *Is the chain-depth-8 rule survivable as a content constraint?* This one cannot be fixed in
   code and does not have a known mechanism (§9). It now has a diagnostic. `PxwRollbackRepro`
   installs a notification-only filter shader and a `PxSimulationEventCallback` on an opt-in
   scene, rebuilds the contact graph from the reported touches, and reports the deepest chain
   of resting contacts rooted at the ground (section 3u). Measured against settled columns the
   walk returns the exact column height — 2, 4, 8, 9, 12 all read back true — and a flat grid
   reads 1, so the depth-8 boundary the divergence table shows (eight survives, nine diverges)
   is now a measured property of the graph rather than a remembered column height. The shader
   only ORs in notification flags and changes no collision or solve decision, which is the
   same property the shipping contact-event work (stage 3a) rests on, so the same walk can be
   promoted into the runtime to enforce the limit on real content. Decide against measured
   scenes, not against intuition.
3. *Is PGS quality acceptable where TGS was chosen for it?* Stacks are measured and favour
   PGS-cold (§2). Articulations are now measured too, and the result is unexpectedly strong.
   Vehicles are now measured as well (below); high mass ratios are covered by §2's stacks.

   **Articulations survive variable-depth rollback under both solvers.** `PxwUndpwrTests`
   builds a five-link fixed-base revolute chain and runs the same battery the box scenes get.
   The table, from a green run (76 checks, 0 failures):

   | scene | solver | baseline determinism | fixed depth 4 | variable depth, 600 frames |
   | --- | --- | --- | --- | --- |
   | free-swinging chain | TGS | ok | exact | **identical** |
   | free-swinging chain | PGS | ok | exact | **identical** |
   | chain resting on ground | TGS | ok | exact | **identical** |
   | chain resting on ground | PGS | ok | exact | **identical** |

   TGS holding variable depth is the surprise, since it defeats a box grid. The likely reason
   is that an articulation's integrator state is captured *explicitly* through
   `PxArticulationCache` — joint positions, velocities and forces, per DOF — rather than left
   in the solver's per-substep scratch the way a rigid contact's warm-start impulse is. A
   restore therefore reaches the articulation's real state on either solver, so replay is
   transparent regardless. The one caveat already visible: capture is a fixed point only after
   one round trip, not immediately (the `RestoreRoundTrip` line reads `no` then `ok`), the same
   one-cycle settle a rigid pose shows, so the cold-step "restore before every step" discipline
   is what makes this hold and must not be relaxed for articulations.

   This is one chain at one depth. Five links is well under the eight-body contact-chain limit,
   and the ground contacts here are shallow. A longer chain, or a chain in a pile deep enough to
   build a contact chain past eight, has not been measured and is exactly where the limit from
   item 2 would reappear.

   **Representative rigid workloads hold under PGS.** `PxwRollbackRepro` section 3v adds the two
   shapes a character game leans on, each under both solvers over 600 frames of differing
   rollback depth:

   | scene | PGS | TGS |
   | --- | --- | --- |
   | upright capsule settling on terrain | **identical** | identical |
   | 40x mass ratio, two-high | **identical** | identical |

   Both hold under PGS, which is the result that matters. TGS also holds here, but only because
   these scenes are quiet and shallow: the same TGS diverges under variable depth on a box grid
   at impact, on the deep stacks, and on every `TestColdStepsGiveTransparency` case. TGS is
   transparent on settled shallow contact, not in general, so it cannot be relied on for
   adaptive rollback.

   **Vehicles survive variable-depth rollback under both solvers.** With the drivetrain
   integrator state now in the snapshot (stage 3b), `PxwUndpwrTests` builds a four-wheeled
   vehicle on the ground and runs the articulation battery over it, for direct and engine drive:

   | scene | solver | baseline | fixed depth 4 | variable depth, 400 frames |
   | --- | --- | --- | --- | --- |
   | direct drive, driving | TGS / PGS | ok | exact | **identical** |
   | direct drive, at rest (sticky tires) | TGS / PGS | ok | exact | — |
   | engine drive, driving | TGS / PGS | ok | exact | **identical** |

   The vehicle is the case where TGS holding is *not* a surprise: its wheels ride the ground on
   suspension raycasts, not rigid contacts, so the chassis has no persistent manifold and no
   uncaptured warm-start — the same reason articulations are transparent. Everything the solver
   would otherwise carry between steps is either captured (wheel, suspension, sticky, engine,
   gearbox, autobox, clutch) or recomputed each step from the captured state and a fresh road
   query. The at-rest case is the sticky-tire regression: the low-speed timer only accumulates
   once the vehicle has stopped, so a snapshot that dropped it would diverge exactly there, and
   it does not.

**The decision: PGS.** Every workload the framework can measure — box grids, stacks up to eight
deep, the capsule, the mass ratio, and articulations on both a free and a grounded chain — is
bitwise transparent under PGS with the cold-step discipline, so two peers rolling back by
different depths agree. TGS is not, so the adaptive horizon in phases 2 and 3 requires PGS.
`SimConfig.Solver` now defaults to `ProjectedGaussSeidel`; the field stays, hashed, so a
strictly fixed-horizon session can still opt into TGS for its marginally tighter stacks. The
one standing limit is a content constraint, not a solver one: a contact chain deeper than eight
bodies defeats variable depth on either solver, and the section 3u diagnostic is how that is
kept enforced rather than remembered. This unblocks phases 2 and 3 below.

The only case a **no** would still apply to is a game whose core loop needs deep stacks
(nine-plus) resolved under varying rollback depth. That is out of reach for either solver
without resetting TGS substep state, which §9 says needs an instrumented PhysX build rather than
the public API — a much larger project than this one, and not obviously possible. Such a game
keeps the fixed horizon and this document as the record of why.

---

## 5. Phase 2 — conditional rollback

**Status: implemented, opt-in.** Phase 1 chose PGS (§4), which unblocked this. It ships behind
`SimConfig.ConditionalRollback`, off by default so the fixed horizon remains the tested default
and TGS sessions are unaffected. `Validate` refuses the flag under any solver but PGS, since a
data-dependent rewind depth only lands where a full re-simulation would when replay is
transparent — the property PGS was measured to have and TGS was measured to lack.

Roll back when something is actually wrong, and only to where it went wrong.

- **Rewind is driven from the tick `InputBuffer.Submit` returns.** `RollbackEngine.SubmitInput`
  now folds that return into `_pendingReplayFrom`, the earliest tick a misprediction has dirtied
  since the last `Advance`. `RunPredictionConditional` replays from there to the horizon end and
  reuses every snapshot below it. A tick that changed nothing costs nothing. The `_predictedThrough`
  gate in `InputBuffer` is what makes the return trustworthy: local input stamped ahead by
  `LocalInputDelay` lands as "not yet guessed" rather than as a misprediction, so it does not
  provoke a rewind every frame.
- **Cold-step discipline kept.** Restore before every step, exactly one restore, never two.
  `RunPredictionConditional` restores once to `replayFrom - 1` and then re-restores before each
  subsequent step, identical to the fixed path — the replay is just shorter.
- **The one-confirmed-tick-per-frame cap stays.** It was lifted under the flag at first, on the
  grounds that the cap existed only because a confirmed step's predecessor differs between TGS
  peers whose packets clumped differently, and transparency makes the predecessor irrelevant — so
  under PGS a confirmed tick is a pure function of the confirmed snapshot before it however many
  drain in one frame. That reasoning is still right, and lifting the cap was still wrong: the cap
  had a second job nobody had written down. Here the clock is the confirmed tick plus the horizon,
  so confirming *n* ticks in a frame advances the clock by *n*, and the cap is the only thing
  pacing the simulation against wall time. Local input is stamped `LocalInputDelay` ticks ahead, so
  a peer whose own input is the last one a tick waits on — a solo host, or anyone during a lull —
  finds the frontier permanently in the future and drains to it every frame, running the whole
  simulation `LocalInputDelay` times too fast. Conditional rollback still pays for itself in
  shorter replays; it does not get to set the rate. Phase 3 states the same rule directly, since
  there the clock is wall time rather than a function of the frontier.
- **Desync detection is mandatory with the flag.** The fixed horizon was the safety net;
  conditional rollback removes it, leaving confirmed-tick hash exchange as the only thing between
  a drift and a silent divergence. `SimSession` forces `SimDesyncDetector.Fatal = true` whenever
  the config sets `ConditionalRollback`, so a session cannot turn the optimisation on and leave
  the check off. The transport, `TryGetConfirmedSnapshot`, `Snapshot.CombinedHash` and
  `HashPerEntity` are all in place (§9 of Architecture.md).

Why this cannot desync a session even if the bookkeeping were wrong: the confirmed timeline is
advanced first, by the same cold restore-and-step per tick as the fixed path, and its hashes are
what peers compare. `RunPredictionConditional` only rewrites *predicted* snapshots, which are
never exchanged. A bug in the replay-start computation could smear a peer's own prediction
between confirmations; it cannot make two peers disagree on a confirmed tick.

Expected cost change: from `1 + PredictionHorizon` restore-and-step pairs per frame to one plus
the confirmed backlog, plus a burst on the frames a correction actually lands. That reintroduces
the frame-time spike the fixed design deliberately traded away, so budget for the worst-case
burst rather than the average — `SnapshotHistory` bounds it.

---

## 6. Phase 3 — a free-running clock

**Status: implemented, opt-in.** Behind `SimConfig.FreeRunningClock`, which requires
`ConditionalRollback` (and so PGS) and is refused otherwise. Off by default.

- `RollbackEngine.AdvanceFreeRunning` advances `_currentTick` once per fixed update,
  independently of `_confirmedTick`. It stalls only when the lead would outrun the snapshot
  ring — `SnapshotHistory - LocalInputDelay - 1`, the same live-window bound `Validate` uses,
  which is a real physical limit rather than a hashed constant. New confirmation is always
  processed even while pinned, because it shrinks the lead and frees the clock next frame.
- `PredictionHorizon` stops being a simulation parameter and **leaves the hash** while the flag
  is set (`ComputeHash` gates it on `!FreeRunningClock`, and hashes `FreeRunningClock` itself so
  both peers agree on the rule). What remains is a peer-local target lead, readable as
  `RollbackEngine.CurrentLead`.
- The lead can be adapted to observed latency rather than a configured constant. This is where
  Overwatch's time dilation belongs, and it is already legal: `LocalInputDelay` is peer-local
  and unhashed, so a peer may retune its own lead mid-session without agreement, watching
  `CurrentLead` and how late inputs arrive.
- The prediction window is now variable-width, so it shares `RunPredictionConditional` with
  Phase 2 — the routine takes the window end as a parameter (`_confirmedTick + PredictionHorizon`
  for the fixed horizon, `_currentTick` for the free clock) and is otherwise identical.
- **Confirmation may not run the clock past wall time.** `AdvanceConfirmed` drags `_currentTick`
  up to whatever it confirms, so `newConfirmed` is capped at `_currentTick + 1`. Without it a peer
  whose own input is the last one a tick waits on is pulled to a frontier that is permanently
  `LocalInputDelay` ticks in the future, and the simulation runs that many times faster than real
  time — most visibly on a solo host, where it is guaranteed rather than occasional. Confirming
  settles the past; it is not licence to simulate the future early, and nothing is delayed by
  saying so, since a tick held back is confirmed on the next call once the clock reaches it.

At that point the local player never waits on a remote packet, remote inputs that beat the
delay are never predicted at all, and the ones that do not are corrected from the tick they
went wrong. As with Phase 2, none of this can desync a session: the confirmed timeline is
advanced by the same cold restore-and-step whatever the clock is doing, and only its hashes are
compared — mandatory and fatal here, since the fixed horizon is gone.

---

## 7. What does not change, in any phase

These are load-bearing for reasons unrelated to the horizon, and each has a measurement behind
it:

- The cold-step discipline: one restore before every step, never two.
- Commit ordering by stable ID. Creation order changes the simulation once contacts exist.
- The input quantize-then-dequantize round trip, so a sender simulates the value its receivers
  decode.
- Framework-driven sleeping with the wake counter pinned.
- Inputs are the only thing on the wire.

---

## 8. Summary

| phase | state | if it fails |
| --- | --- | --- |
| 0 — local input delay | **done** | — |
| 1 — solver decision | **done**: PGS chosen and defaulted (§4) | stop, keep the fixed horizon |
| 2 — conditional rollback | **done**, opt-in behind `SimConfig.ConditionalRollback`; requires PGS; ships with mandatory fatal desync detection | stop, keep the fixed horizon |
| 3 — free-running clock | **done**, opt-in behind `SimConfig.FreeRunningClock`; requires conditional rollback; `PredictionHorizon` leaves the hash and becomes a peer-local lead | keep conditional rollback, keep the fixed lead |

The order matters more than the schedule. Phase 1 is a measurement, not a build, and it decides
whether phases 2 and 3 exist at all.
