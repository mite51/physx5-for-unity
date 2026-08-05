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
   code and does not have a known mechanism (§9). It needs a runtime diagnostic that reports
   the deepest contact chain per tick, so the rule is enforced rather than remembered. Decide
   against measured scenes, not against intuition.
3. *Is PGS quality acceptable where TGS was chosen for it?* Stacks are measured and favour
   PGS-cold (§2). Articulations are now measured too, and the result is unexpectedly strong.
   Vehicles and high mass ratios remain open.

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
   item 2 would reappear. Vehicles and high mass ratios are still not measured at all; vehicles
   in particular cannot be until their integrator state is actually in the snapshot, which today
   it is not (only the chassis rigid body is captured).

**If any answer is no**, stop. Keep the fixed horizon and keep this document as the record of
why. The only remaining lever is resetting TGS substep state, which §9 says requires an
instrumented PhysX build rather than reasoning from the public API — a much larger project
than this one, and not obviously possible.

---

## 5. Phase 2 — conditional rollback

Only if phase 1 passes. Roll back when something is actually wrong, and only to where it went
wrong.

- Drive rewind from the tick `InputBuffer.Submit` returns, replaying from there to
  `CurrentTick`. The plumbing exists and is documented as advisory precisely so this could be
  built on it later.
- **Keep the cold-step discipline.** Restore before every step, exactly one restore, never two.
  It is independent of the horizon — a free-running loop does `restore(T-1); step(T)` once per
  frame and satisfies it more cheaply than the current design does — and it is what buys
  determinism. The horizon is what costs responsiveness. They were bundled together and they
  separate cleanly.
- **Remove the one-confirmed-tick-per-frame cap at the same time** (§6.3). It is the other half
  of the same constraint: it exists because a confirmed step's predecessor differs between
  peers whose packets clumped differently, which is precisely what transparency makes
  irrelevant. Leaving it in place would cap confirmation throughput for no remaining reason.
- **Desync detection stops being optional.** Today, identical operation sequences are the
  safety net and hash comparison is a diagnostic. Afterwards there is no net, and confirmed-tick
  hash exchange is the only thing standing between a drift and a match that silently stops
  agreeing. It ships *with* this phase, not after it. `TryGetConfirmedSnapshot`,
  `Snapshot.CombinedHash` and `HashPerEntity` are already in place; the transport is not.

Expected cost change: from `1 + PredictionHorizon` restore-and-step pairs per frame to one,
plus a burst on the frames a correction actually lands. That reintroduces the frame-time spike
the current design deliberately traded away, so budget for the worst-case burst rather than the
average — `SnapshotHistory` bounds it.

---

## 6. Phase 3 — a free-running clock

Only if phase 2 lands.

- Advance `_currentTick` once per fixed update, independently of `_confirmedTick`. Stall only
  when the lead would outrun `SnapshotHistory`, which is a real physical limit, rather than
  when a hashed constant says so.
- `PredictionHorizon` stops being a simulation parameter and leaves the hash. What remains is a
  peer-local target lead, and it can then be adapted to measured latency, which is what the
  investigation meant by "materially better netcode".
- Adapt the lead from observed input lateness rather than from a configured constant. This is
  where Overwatch's time dilation belongs, and it is already legal: `LocalInputDelay` is
  peer-local and unhashed, so a peer may retune its own lead mid-session without agreement.

At that point the local player never waits on a remote packet, remote inputs that beat the
delay are never predicted at all, and the ones that do not are corrected from the tick they
went wrong.

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
| 1 — solver decision | `SimConfig.Solver` exists and is hashed; the three measurements are open | stop, keep the fixed horizon |
| 2 — conditional rollback | blocked on phase 1; desync detection ships with it | stop, keep the fixed horizon |
| 3 — free-running clock | blocked on phase 2 | keep conditional rollback, keep the fixed lead |

The order matters more than the schedule. Phase 1 is a measurement, not a build, and it decides
whether phases 2 and 3 exist at all.
