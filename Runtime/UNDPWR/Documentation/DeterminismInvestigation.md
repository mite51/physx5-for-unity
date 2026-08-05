# Determinism investigation: state of play

A working handoff. This records what has been *measured* about bitwise determinism under
rollback in PhysX 5, what has been changed as a result, what turned out to be wrong, and
what is still open.

Read §1 and §8 first; §9 is the one live bisection. Read §5 before you propose a theory —
five plausible ones are already buried there, and the fifth was very nearly buried while
being correct.

---

## 1. The goal, and the standard

Rollback netcode where every peer simulates locally from player inputs alone. The standard
being held to, set by the project owner:

> Any bitwise divergence on a frame with identical input is a failure.

Not a tolerance to be budgeted. A bug to be tracked down. The previous generation of this
system (Unity's built-in PhysX, an earlier project) ran indefinitely bit-exact without
rollback, and bit-exact *with* rollback after some issues were fixed. So the bar is known
to be reachable.

Method that has been working: reproduce in a standalone native app with the minimum
complexity that shows the problem, bisect to a single cause, then fix.

---

## 2. Where the tests live

| binary | source | links | purpose |
| --- | --- | --- | --- |
| `PxwRollbackRepro` | `physx5-native-plugin/tests/PxwRollbackRepro.cpp` | PhysX only | bisects rollback divergence; no wrapper code compiled in, so a failure here is PhysX or how it is being driven |
| `PxwUndpwrTests` | `physx5-native-plugin/tests/PxwUndpwrTests.cpp` | PhysX + plugin sources | exercises the wrapper's world, snapshot, sleep-policy and id machinery |
| `PxwPoseRoundTripTests` | `physx5-native-plugin/tests/PxwPoseRoundTripTests.cpp` | PhysX only | actor pose round trip |

Build and run:

```powershell
cd C:\Git\physx5-native-plugin\build
cmake --build . --config Release --target PxwRollbackRepro PxwUndpwrTests
cd Release
.\PxwRollbackRepro.exe
.\PxwUndpwrTests.exe
```

`PxwRollbackRepro` distinguishes assertions from observations. `Check` is a real pass/fail.
`Characterise` records behaviour that is expected to be false and is worked around by
construction — creation-order sensitivity, for instance — so it prints `yes`/`no` and does
not count as a failure. A `no` line is a documented limit, not a regression; if you want to
know which limits are load-bearing, they are in §8.

Current status: `PxwRollbackRepro` 22 checks, 0 failures. `PxwUndpwrTests` 49 checks,
0 failures. `PxwPoseRoundTripTests` 19 checks, 0 failures.

---

## 3. Established facts

All measured, all reproducible from the two suites above.

**PhysX is deterministic given identical construction.** Two identically built scenes stay
bit-identical for 600 steps, with and without contacts.

**Capture and restore are not lossy.** Rollback with no contacts in play is bitwise exact.
The state blob is not missing anything for free bodies.

**`setGlobalPose` is the entire source of contact-related rollback divergence.** Restoring
velocity, wake counter, or clearing forces is provably harmless. Restoring *pose*
reproduces the full rollback error digit for digit — and does so when writing back the pose
the body already has, so it is not a rewind effect.

**It is a side effect, not precision loss.** The centre-of-mass local pose is already the
identity for these bodies, and 64 consecutive `setGlobalPose(getGlobalPose())` cycles move
the reported pose by exactly 0 m. Teleporting an actor invalidates its cached contact data,
so the next step solves from a cold cache.

**Restore erases history.** Two worlds driven along deliberately different histories (120
steps versus 313), handed the same snapshot and stepped together, stayed bit-identical for
600 steps. `restore(S); step()` is a pure function of `S`. This is the property rollback
netcode actually needs.

**Cold steps make rollback bitwise transparent, under PGS.** With *exactly one restore
before every step* — including steps nobody rolled back — a replayed run is bitwise
identical to a run that was never rolled back. Confirmed with persistent contact manifolds
on and off, on a loose grid, on a settled 16-high stack, and on a 16-high stack caught
mid-impact.

The asymmetry was never that restoring is lossy. It is that restoring is *different from
not restoring*: a step following a restore runs cold, a step following another step runs
warm, and the two do not agree. Making every step cold removes the difference.

**Exactly one restore. Not two.** Quaternion normalisation is not idempotent, so restoring
twice before a step shifts the rotation by one ULP (2.98e-08). This masqueraded as a real
finding for a while.

**Cold steps do *not* give transparency under TGS.** The same test that passes on PGS fails
on the first replayed step, by 3e-09 m/s on a loose grid and 6.6e-07 m/s on a stack, with
pose still bitwise equal. TGS carries per-substep state that a restore does not reach and
that no flag clears — unlike the contact cache, where `setGlobalPose` happens to do the job.
This matters because the framework runs TGS (§7).

**Contact-persistence flags do not solve any of it.** `eDISABLE_CONTACT_CACHE` changes
nothing measurable. Turning `eENABLE_PCM` off shrinks the replay error from 8.4e-05 to
2.4e-07 (about one ULP) but never to zero. None of them give transparency; only the
cold-step discipline does, and it works with PCM left on.

**The wake counter does not replay, and it is not a rounding problem.** Details in §7. It
was the last divergence standing under the cold-step discipline, and it is now removed by
construction rather than fixed.

**Creation order changes the simulation once contacts exist** (diverges around step 176 in
a 16-body scene). Free flight is entirely order-independent, so it is specifically solver
ordering. `getInternalActorIndex()` and `getInternalIslandNodeIndex()` are handed out from
insertion order.

**Cold stepping costs nothing measurable, and may help.** On a 16-high stack — the case
warm starting exists for — the warm run settles to y=1.499931 and the cold run holds
1.499999, so cold sags 68 µm *less*. Residual velocity at rest is 0.002971 m/s warm versus
0.000146 m/s cold, roughly 20x quieter. Treat as provisional: it measures settling quality,
not CPU time, and does not yet cover high mass ratios, articulations or vehicles.

**Cold PGS beats cold TGS on the workload TGS was chosen for.** The comparison above is
PGS-warm against PGS-cold, which is not the comparison the framework faces: it always runs
cold, so the question is cold against cold. `MeasureColdStepCost` already prints both, and
on the 16-high stack the cold rows are 0.000146 m/s residual under PGS against 0.003556 m/s
under TGS — PGS about 24x quieter — with settled height 1.499999 against 1.499998. TGS was
selected for stack stability, and once every step is cold it does not have it.

**The solver decision is made: PGS.** The remaining coverage from the caveat above is now in.
Articulations survive variable depth under both solvers (`PxwUndpwrTests`), a settled capsule
and a 40x mass ratio hold under PGS (`PxwRollbackRepro` §3v), and the contact-chain-depth
diagnostic (§3u) confirms the eight-deep survivability boundary is a measured graph property.
Only vehicles remain unmeasurable until their integrator state is captured. PGS is transparent
to variable depth across every measurable workload and TGS is not, so `SimConfig.Solver`
defaults to PGS; see [AdaptiveRollbackPlan.md](AdaptiveRollbackPlan.md) §4 for the full record.
The single standing limit is the content constraint — no chain deeper than eight bodies — which
holds on either solver.

---

## 4. What changed

### PhysX (`C:\Git\PhysX`)

No lasting source change. An earlier pass added a `resetSleepFilter()` virtual to reset the
unsnapshotted motion accumulators; it changed no measured number (§5) and was removed once
framework sleep made the accumulators irrelevant — the wake counter is pinned while a body is
awake, so the accumulators are never read. The only PhysX build step that remains is the
ordinary rebuild after a checkout:

```powershell
cd C:\Git\PhysX\physx\compiler\vc17win64-cpu-only-md
cmake --build . --config release
```

Takes about 25 seconds. Output lands in `physx/bin/win.x86_64.vc143.md/release`. Because
removing the virtual changes the vtable, redeploy `PhysX_64.dll` alongside `PhysXUnity.dll`
or a stale DLL faults on the first call (§6).

### Native wrapper (`C:\Git\physx5-native-plugin`)

- `include/PxwUndpwr.h`, `src/PxwUndpwr.cpp` — `PxwInternalIdEntry`,
  `PxwWorldReadInternalIds`, `PxwWorldHashInternalIds`. Lets peers verify that a stable ID
  maps to the same PhysX-side identity everywhere.
- `include/PxwUndpwr.h`, `src/PxwUndpwr.cpp` — framework-driven sleep: `PxwWorldSetSleepParams`,
  a `restTicks` field on every entry and in both snapshot payloads (state version 2), and an
  `UpdateSleep` pass after `fetchResults`. The wake counter is pinned while a body is awake —
  on creation, on restore, on re-enable, and on commit of newly added entries — so PhysX's own
  sleep path never runs; the framework sleeps a body once its speed has stayed below the
  thresholds for `sleepTicks` steps (§7). The earlier `PxwSleepPolicy` and its `resetSleepFilter`
  call were removed.
- `tests/PxwRollbackRepro.cpp` — new.
- `tests/PxwUndpwrTests.cpp` — added `TestInternalIdsMatchAcrossRegistrationOrder`,
  `TestRestoreIntoUsedWorldsDisagrees`, `TestVariableRewindDepthAgreement`,
  `TestSustainedDivergentRollback`, and the framework-sleep group
  (`TestFrameworkSleepThreshold`, `TestFrameworkSleepSurvivesRestore`,
  `TestSleepingBodyWakesOnContact`, `TestFrameworkSleepReplays`).
- `CMakeLists.txt` — rewritten around a single `PHYSX_ROOT` / `PHYSX_CRT` pair that derives
  both the link directory and the deploy directory, so they cannot drift (§6).

### C# framework (`C:\Git\physx5-for-unity`)

- `Runtime/UNDPWR/Interop/NativeTypes.cs` — `SimInternalIdEntry`.
- `Runtime/UNDPWR/Interop/NativeMethods.cs` — the new imports, and a sweep of dead ones.
- `Runtime/UNDPWR/Core/SimConfig.cs` — `SleepLinearThreshold`, `SleepAngularThreshold` and
  `SleepTicks`, all hashed; `CpuWorkerThreads` added to the hash; the solver iteration counts
  now actually applied (below).
- `Runtime/UNDPWR/Core/DeterministicWorld.cs` — `ReadInternalIds`, `HashInternalIds`,
  `CompareInternalIds` (the last names the offending stable ID when peers disagree); sets the
  sleep parameters at world creation, and pushes the configured solver iteration counts onto
  each dynamic body at registration, which nothing did before.
- `Runtime/UNDPWR/Rollback/RollbackEngine.cs` — `RunPrediction` restores before every step,
  not just at the start of the run. This is the cold-step discipline.

Verified: two worlds built in opposite application order produce identical PhysX indices
and the same id-map hash (`0x377646669C4797C2`), because the wrapper commits in stable-ID
order.

---

## 5. Conclusions that were wrong

Recorded because each cost real time, and each looked convincing at the point it was
believed.

**"PhysX warm-start impulses are unexposed, so bit-exact rollback is impossible."** Wrong.
Cold steps achieve it under PGS. The conclusion came from comparing a replay against a
*warm* reference, which is a comparison no peer ever makes.

**"A peer that rewound three ticks and one that rewound five are different worlds and
cannot be reconciled."** Wrong, and it was in `Architecture.md`. Peers that rewound 4 and 16
land on bit-identical state. Restore erases history.

**"The divergence is centre-of-mass precision loss from `PxDiagonalize` on a degenerate
inertia tensor."** Wrong. The bodies are cubes, the CoM local pose is already the identity,
and the pose round trip is exact. This theory had a lot of surface plausibility and
survived several rounds; killing it needed a direct measurement, not more reasoning.

**"Sleep accumulators double-count on replay, so `resetSleepFilter()` will fix the wake
counter drift."** Wrong. The patch was correct as far as it went — those accumulators
genuinely are unsnapshotted and unreset — but it changed the failing numbers *not at all*,
identical to the digit, which is itself evidence they were not participating. It has since
been removed; pinning the wake counter makes the accumulators moot.

**"The interaction-count theory is dead, because both peers reset with the same count."**
Nearly wrong, and the most instructive of the five. The instrumentation asked the question
at the tick where the *state* diverged (171), and at the nearest reset before it (168) both
peers reset from one counted interaction. That looks conclusive. It was not: the reset that
mattered had happened at a different tick, and probing replay directly — the same tick
replayed from 24 different rewind depths — showed resets landing on 0 and 1 interactions
for the same body with bitwise identical pose. The theory was right. The measurement was
aimed one tick to the side of it.

The pattern across all five: reasoning from mechanism outran measurement, and once
measurement started, a *negative* result got trusted more than it had earned. When a
hypothesis about hidden state is cheap to test, test it — and when a test clears it, check
that the test was pointed at the right frame.

---

## 6. Build hazard — resolved

Previously `PHYSX_RUNTIME_DIR` in `physx5-native-plugin/CMakeLists.txt` named a directory
holding a **different, older** PhysX build than the one being linked, and the post-build
step copied those stale DLLs next to the test executables on every build. It produced a page
of confident, entirely fictitious failures, and once a virtual was added to `PxRigidBody`,
an outright access violation.

The CMake file now derives every PhysX path from one `PHYSX_ROOT` / `PHYSX_CRT` pair:
link directory, deploy directory and test-adjacent DLLs all come from the same build, and
`CMAKE_MSVC_RUNTIME_LIBRARY` is set from `PHYSX_CRT` so the plugin cannot be built against a
CRT the PhysX binaries do not use. `BUILD_PHYSX_FIRST` will rebuild the PhysX targets ahead
of the plugin if you want the guarantee rather than the convention.

The manual `Copy-Item` workaround is no longer needed. If you find yourself typing it,
something has regressed in the build files — fix that instead.

---

## 7. The wake counter: what it was, and how it was closed

`TestVariableDepthUnderColdSteps`. Two peers roll back by different, varying depths every
frame under the cold-step discipline. Pose, rotation, and linear and angular velocity stayed
**bitwise identical**. The wake counter did not:

```
first difference at body 7
   position delta 0   rotation delta (0 0 0 0)
   linVel delta 0     angVel delta 0
   wake 0.349999934 -> 0.366666615
diverged at frame 111 of 600
```

The difference is 0.016666681, exactly one timestep at 60 Hz.

### The cause

`ScBodySim.cpp` computes the reset value as:

```cpp
wc = factor * 0.5f * wakeCounterResetTime + dt * (clusterFactor - 1.0f);
```

`clusterFactor` is `1 + getNumCountedInteractions()` — how many contact interactions the
body currently has. That is broadphase and island bookkeeping about which *pairs exist*, not
about where anything is, and nothing in the snapshot describes it.

`TestReplayedTickMatchesOriginal` measures this directly: replay one tick from 24 different
rewind depths and compare each against what the un-rolled-back run computed for that same
tick. All 24 depths reproduced pose and velocity exactly. Only 22 reproduced the wake
counter, and the two that did not show the mechanism in the open:

```
depth 5, tick 195, body 0: 0.399999976 vs 0.416666657 replayed
                           reset both times, to 0 and 1 interactions
```

Same body, same tick, bitwise identical pose, reset from a different interaction count — one
extra interaction, contributing exactly one `dt` through `dt * (clusterFactor - 1)`, which
is the whole of the observed discrepancy. A restore puts the bodies back; it does not put
the pair bookkeeping back, and the wake counter is the one piece of captured state that
reads from it.

### The fix: take the decision away from PhysX

Fixing this inside PhysX would mean making pair bookkeeping restorable, which is a much
larger surface than a virtual on `PxRigidBody`, in code with no public entry points and no
guarantee the next PhysX release keeps the same shape.

So the wake counter is removed from play instead. While a body is awake its counter is pinned
at `PX_MAX_F32`. A counter that never decays never resets, the reset expression is never
evaluated, and there is nothing left to read the interaction count. The pin is re-applied
after every restore, so it survives rollback, and after re-enable and commit, so a body
cannot enter the world without it.

Measured result: `TestVariableDepthUnderColdSteps` on the configuration that failed at frame
111 now runs all 600 frames bit-identical. That failure is closed.

Pinning alone leaves resting bodies being solved, so sleeping is then done by the framework
rather than PhysX. Each body carries a `restTicks` counter — in the snapshot, so it replays —
that counts steps spent below configured speed thresholds; once it reaches `sleepTicks` the
body is `putToSleep`. Because the decision is a pure function of restored velocities and a
restored counter, it reproduces under rollback. `PxwWorldSetSleepParams` sets the thresholds;
`SleepTicks = 0` disables sleeping and keeps the pin-only behaviour.

Measured result: `TestFrameworkSleepReplays` runs a settling scene straight for a reference,
then again with a fixed-depth rollback on every frame, and the two stay byte-identical for
the whole run while bodies sleep and wake — so the sleep decision, and PhysX's auto-wake when
a body is disturbed, both replay. The one part not exercised there is a sleeper woken by a
*new* contact under rollback; if that ever proves not to replay, the next step is a
contact-event layer, not a workaround. The wake counter is no longer captured or restored: an
awake body is re-pinned, a sleeping one is slept, so the value the snapshot used to carry is
gone.

---

## 8. What is still open

The wake counter closing did **not** make variable rollback depth safe in general. Two
limits remain, both recorded as `Characterise` rather than asserted:

**TGS is not transparent.** Cold steps give bitwise transparency under PGS and never under
TGS — 3e-09 m/s on a loose grid, 6.6e-07 m/s on a 16-high stack, on the *first* replayed
step, with pose still bitwise equal. Transparency is the property variable depth rests on,
so under TGS variable depth cannot work at all, wake counter or no wake counter.

This is the one that has a decision attached to it. `SimConfig.ToSceneDesc` hardcodes TGS,
chosen for stack and articulation stability. That choice is still defensible — the framework
does not depend on transparency, because it makes every peer perform an identical operation
sequence (`Architecture.md` §2) — but it does mean the fixed prediction horizon is not
merely conservative, it is required. Reconsidering the horizon means first finding out
whether TGS's substep state can be reset, and that has not been attempted.

**Deep contact chains diverge under variable depth even on PGS.** Bisected in §9. The rule
that came out of it: a contact chain up to 8 bodies deep survives peers rewinding by
different amounts; 9 does not. Everything else about the scene turned out not to matter.

**Creation order still changes the simulation** once contacts exist. Worked around by
committing in stable-ID order, verified by the id-map hash, and not a live risk — but it is
a permanent constraint on anything that registers actors, not a bug that will be fixed.

**Mid-match join is now correct locally.** `PrepareForRebuild` recreates the native world by
default (`DeterministicWorld.RecreateNativeWorld`): it destroys the scene, re-registers the
same actors in stable-ID order into a fresh one, and restores the agreed snapshot — so a
joining peer's fresh world and every existing peer's rebuilt world reach the identical PhysX
internal arrangement, which a restore into a warmed world could not. What remains is the *wire*
flow that negotiates the resume tick and agreed snapshot between peers; the local rebuild it
drives is done. Unrelated to everything above.

### Why the horizon stays (by default)

If TGS transparency and the chain-depth limit both closed, rollback depth would no longer
need to be synchronised across peers, and the fixed prediction horizon could go — peers would
rewind by whatever their own latency demanded, which is materially better netcode. TGS is not
close and the chain-depth limit is understood but not explained. Keep the fixed horizon.

That is why it stays *by default*. The route to transparency by leaving TGS (below) did close,
so the framework now ships that better netcode as an opt-in: PGS plus cold steps is measured
transparent (§4 of AdaptiveRollbackPlan.md), and on that basis `SimConfig.ConditionalRollback`
and `SimConfig.FreeRunningClock` let a PGS session rewind and lead by whatever its own latency
demands, with confirmed-hash desync detection made mandatory to replace the safety net the
fixed horizon provided. TGS sessions keep the fixed horizon, which remains the default.

One option that framing skips: the route to transparency does not have to run through fixing
TGS, because *leaving* TGS also reaches it. PGS plus cold steps is already measured transparent,
and §3 measured PGS-cold settling a 16-high stack **better** than warm — 1.499999 against
1.499931, residual velocity 0.000146 m/s against 0.002971 m/s — which undercuts the stack
stability that TGS was selected for in the first place. Articulations, vehicles and high mass
ratios are unmeasured on PGS and are the reason this is not simply a one-line change. That
measurement, and what it unblocks if it comes back clean, is staged in
[AdaptiveRollbackPlan.md](AdaptiveRollbackPlan.md).

---

## 9. The contact-chain depth limit

The one result in §8 that had no explanation at all. It now has a rule and a list of things
it is not, though not yet a named mechanism.

### The rule

With the wake counter pinned, PGS selected and every step cold, two peers rewinding by
different, varying amounts every frame stay bitwise identical **provided no contact chain is
more than 8 bodies deep**. At 9 they diverge. The threshold is sharp and does not move with
the window:

| column height | 2 | 4 | 6 | 8 | 9 | 10 | 11 | 12 | 16 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 400 frames of varying depth | ok | ok | ok | ok | **fails** | **fails** | **fails** | **fails** | **fails** |

"Chain depth" means how far load has to propagate through body-to-body contacts, not how
many bodies are involved. A 4×4 slab of sixteen touching boxes, all resting directly on the
ground, is one island of sixteen and is clean. A column of nine is not.

### What it is not

Each of these was measured, and each removes a plausible story:

| ruled out | evidence |
| --- | --- |
| the broadphase implementation | eSAP, eABP and ePABP all fail at the identical frame and tick |
| persistent contact manifolds | fails identically with PCM on and off |
| the contact cache | fails identically with the cache on and off |
| the carried pair set | calling `resetFiltering` on every body on every restore does not close it |
| violent impact | a column released 0.1 m above the ground fails the same way as one dropped 39 m |
| solver conditioning under load | a 4-high column with 120x the load is clean; a 12-high with 11x is not |
| island size | a sixteen-body island one contact deep is clean |
| contacts being made and broken | a scene with churn 34 is clean, one with churn 42 fails; churn alone predicts nothing |
| sleep, and the wake counter | pinned throughout all of the above |
| thread scheduling | the dispatcher has zero worker threads; everything runs on the calling thread |

### What is known about the mechanism

At the diverging tick, both peers step from **bitwise identical** body state and get
different answers. One of them loses a broadphase pair that step and the other does not, and
one generates contacts for 8 of its 9 narrow-phase pairs while the other does for all 9. So
there is carried state, it is not any of the things above, and the divergence it produces is
discrete rather than a rounding drift — the first difference is already 0.048 m and 1.05 m/s,
not one ulp.

Both remaining suspects live in the island manager, which is the one piece of carried
bookkeeping with no public reset: the island graph's edge set, and the order edges were
inserted into it. Both are maintained from touch-found and touch-lost *transitions*, which
is precisely the shape of the wake-counter bug in §7 — and a body's counted interaction
count, the quantity that caused that bug, is read from this same graph. It would be tidy if
the two failures had one cause. That is a hypothesis, not a measurement.

Why depth 9 specifically is not explained at all.

### What to do about it

Nothing, for now, and the framework is not exposed: it runs a fixed horizon, so every peer
rewinds the same amount every frame and none of this is reachable. The threshold is asserted
in `PxwRollbackRepro` so that a PhysX upgrade which moves it gets noticed.

If it is picked up again, the next step is to instrument the island graph directly —
`Sc::Interaction` insertion order and the island manager's edge list, per body per tick,
compared between two peers at differing depths. That means building PhysX with
instrumentation rather than reasoning from the public API, which is why it stopped here.

---

## 10. Still to build

Most of the original list has since landed: the transport interface and wire messages
(`ISimTransport`, `SimSession`), articulation and vehicle rollback state (measured in the
native suite), the multi-peer test harness (`RunMultiPeerTests`), the scene-query and
contact/trigger native layers, and framework-side sleeping (§7). Adaptive rollback landed too,
opt-in under PGS: conditional rollback and the free-running clock (AdaptiveRollbackPlan.md
§5–6).

What remains: the synchronised-rebuild *message flow* that carries a mid-match join over the
transport — the local rebuild it drives is done (`DeterministicWorld.RecreateNativeWorld`),
but nothing yet negotiates the resume tick and agreed snapshot between peers — and diagnostics
and the editor overlay. CPU/GPU backend selection exists (`SimConfig.Backend`), with GPU gated
behind an explicit experimental override since PhysX gives no cross-machine determinism
guarantee for it.
