# UNDPWR changelog

All notable changes to **UNDPWR** — the Unity Networked Deterministic Physics With
Rollback layer over PhysX 5 — are recorded here. UNDPWR lives inside the
`dev.yafei.physx5-for-unity` package but is versioned on its own line below, because its
compatibility contract is not the package's: two peers only interoperate when they agree on
both the managed config hash (`SimConfig.ComputeHash`) and the native snapshot format
(`kStateVersion` in `PxwUndpwr.cpp`). Those two numbers are called out on every entry that
moves them.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Entries above
the first tagged release are reconstructed from the commit history of both the
`physx5-for-unity` and `physx5-native-plugin` repositories; the framework has not yet cut a
tagged release, so everything to date sits under **Unreleased**.

## [Unreleased]

Snapshot format: `kStateVersion = 3`. Config hash covers, among others, `Solver`,
`FreeRunningClock`, and — only when the clock is not free-running — `PredictionHorizon`.

### Added

- **Synchronised-rebuild roster protocol (mid-match join / leave / rejoin).** A rebuild can
  now change the player set, not just re-agree existing state. `SimRebuildState` bundles a
  confirmed tick, the resuming roster and all three snapshot channels;
  `RollbackEngine.CaptureRebuildState` exports it (buffers copied out of the ring) and the new
  `RollbackEngine.PrepareForRebuild(ref SimRebuildState, reconcile, recreateWorld)` restores it
  on any peer — including a joiner that never simulated the ticks — by restoring the managed
  channels from the payload rather than from local history, replacing the input buffer's player
  set when it changed, and running an optional `reconcile` callback after restore but before the
  resume capture so a roster change (spawning a joiner's avatar, retiring a leaver's) is baked
  into the agreed snapshot instead of replayed. `SimRebuildCodec` (with `SimByteWriter.WriteBytes`
  / `SimByteReader.ReadBytes` length-prefixed blocks and a `SimMessageKind.Rebuild` tag) moves it
  over any reliable transport. `SimSession.ReplaceRoster` / `NotifyRebuilt` keep a session's
  handshake roster and publish cursor in step afterwards. Config hash and snapshot format
  unchanged; the rebuild payload is out-of-band and not part of the interop contract.
- **Transport and desync detection.** `ISimTransport` with an in-process
  `SimLoopbackNetwork` (configurable latency, loss and reordering), the `SimByteWriter` /
  `SimByteReader` little-endian wire codec, `SimInputCodec`, and `SimMessageKind` framing.
  `SimSession` runs a config-hash handshake at join — a solver or horizon mismatch is
  refused rather than discovered as a desync — and exchanges confirmed-tick hashes through
  `SimDesyncDetector`. Inputs remain the only simulation data on the wire.
- **Conditional rollback (Phase 2, opt-in).** `SimConfig.ConditionalRollback` rewinds only
  as far as the earliest mispredicted tick instead of replaying the whole horizon every
  frame, and lifts the one-confirmed-tick-per-frame cap. Requires the PGS solver; peer-local
  and not hashed. `SimSession` forces desync detection fatal whenever it is set, because the
  fixed horizon was the previous safety net.
- **Free-running clock (Phase 3, opt-in).** `SimConfig.FreeRunningClock` advances
  `_currentTick` off the fixed update independently of the confirmed tick, stalling only when
  the lead would outrun `SnapshotHistory`. `PredictionHorizon` becomes a peer-local target
  lead and leaves the config hash; the flag itself is hashed so both peers rest on the same
  field set. Requires conditional rollback. Adds `RollbackEngine.CurrentLead`.
- **Vehicle rollback state.** A variable-size `VehiclePayload` captures integrator state only
  — per-wheel rigid-body, suspension and sticky-tire accumulators, plus engine, gearbox
  (including the in-progress shift timer), autobox and clutch state for engine-drive — while
  per-step derived output is recomputed. `PxwVehicle` exposes `CaptureState` / `RestoreState`;
  `nbWheels` and drive mode are stored on the world entry at register time and fold into
  per-entry hashing. **Bumped `kStateVersion` to 3.**
- **Contact and trigger events, native.** Typed `PxwContactEvent` / `PxwTriggerEvent`, a
  per-world `PxSimulationEventCallback`, and a notification-only filter shader that requests
  touch and contact-point reports without changing which pairs collide or are solved. Drains
  resolve actors through the existing binary search, drop unregistered actors, normalise
  contacts to `IdA < IdB` with the normal oriented A→B, sort by ID pair and truncate
  deterministically — so a replayed tick produces the identical sorted event set. Managed
  scratch buffers grow on overflow instead of silently truncating.
- **Managed registration bridge** for `PhysxArticulationBody` and `PhysxVehicle`, plus a
  vehicle command channel (`SimVehicleCommand`) that routes throttle, brake, steer and gear
  through the input frame rather than live Unity input. Worked examples in `Gameplay.md`.
- **Mid-match join as a synchronised rebuild.** `DeterministicWorld.RecreateNativeWorld`
  destroys the native scene, recreates it and re-registers every entity in stable-ID order,
  so a joiner and the incumbents reach an identical internal PhysX arrangement.
  `RollbackEngine.PrepareForRebuild` takes a `recreateWorld` flag and uses it by default.
- **Native diagnostics and coverage:** a contact-chain-depth diagnostic that walks the
  contact graph each tick so the eight-body limit is enforced rather than remembered; a
  native multi-peer harness that drives two worlds and checks confirmed-hash agreement under
  latency, loss and a deliberately diverged control; articulation, vehicle and
  capsule/mass-ratio workloads under both solvers; and `TestSleeperWokenUnderRollback`, which
  characterises the one place a rollback is not bit-exact (a sleeper woken by a *new* contact,
  where warm-start state the original tick had cannot be captured).
- Native `SetArticulationJointMaxVelocity` is now exposed rather than commented out.

### Changed

- **Default solver is now PGS** (`SimConfig.Solver`), following the Phase 1 measurements; the
  field and its hashing already existed. This is the gate the adaptive-rollback phases sit
  behind.
- `DeterministicWorld.Register` now applies `PxwApplyDeterministicRigidDefaults` — disabling
  speculative CCD and clamping max depenetration velocity — instead of only setting solver
  iterations, so the runtime matches the determinism the native suite measures.
- UNDPWR-registered vehicles are stepped and honour enable/disable: `AddEntryToScene` /
  `RemoveEntryFromScene` now call `VehicleRegister` / `VehicleUnregister`, `ApplyEnabled`
  handles `eVEHICLE`, and `VehicleStepScene` skips disabled vehicles. `eREINSERT` routes
  vehicles through `PxwVehicle::RemoveFromScene` / `AddToScene` (with a new `wakeOnLostTouch`
  parameter) instead of leaving `mInScene` stale.
- Documentation brought in line with the code: forces and scene queries are documented as
  implemented (not pending), vehicle snapshot contents are described in `Architecture.md`
  §5.1, and the "still to build" list in `DeterminismInvestigation.md` is pruned to what
  actually remains.
- **Peers now verify they registered bodies in the same order.** After the first confirmed step
  assigns PhysX its actor indices, each peer sends its stable-ID → actor-index table once
  (`SimMessageKind.InternalIds`), re-sent after every rebuild, and `SimRegistrationCheck` names the
  first body PhysX identifies differently on the two peers. This catches the case where the same
  framework entity is a different PhysX body on each peer — a registration-order bug that the
  config and roster handshake cannot see and that otherwise surfaces only as a gradual physics
  desync after the first contact. The comparison ignores the island node index, which legitimately
  changes when a body sleeps, so a resting ball does not trip it.
- **Peers now exchange per-entity hashes on a physics desync, so one log names the diverged body.**
  A disagreement that includes the physics channel sends that tick's table
  (`SimMessageKind.EntityHashes`) as well as logging it, and `SimEntityHashDiff` reports the
  entities whose hashes differ. Both peers detect the same tick and both send, so each side can
  name the body without a request, a reply, or deciding which peer is correct — and without a human
  diffing two consoles. `SimSession` also logs once, at info, when a peer's registration order is
  confirmed to *match*, since a check that only speaks up on failure is indistinguishable from one
  that never ran.
- **A physics desync can now name the body.** `SimConfig.PerEntityHashDiagnostics` records
  `DeterministicWorld.HashPerEntity` alongside every confirmed snapshot — the one moment the live
  world *is* the confirmed tick, so no extra restore is needed — and `SimSession` logs the table
  whenever a disagreement includes the physics channel. Every peer detects the same tick and logs
  its own table, so the diverged body is found by diffing two logs rather than by adding a request
  message and a round trip to a peer that may itself be the wrong one. Retrieval is
  `RollbackEngine.TryGetConfirmedEntityHashes`. Off by default and excluded from `ComputeHash`,
  like `DisablePvd`: it changes what is observed, never what is simulated.
- **A desync now names the channel that diverged.** The `Hash` message carries a snapshot's three
  channel hashes (`SimStateHashes`: physics, entity, game) instead of only the fold peers compare,
  and `SimDesyncReport` gains `Local`, `Peer`, `Channels` and `Describe()`. Sixteen extra bytes on
  a message already sent every confirmed tick, in exchange for turning "the simulations diverged"
  into "the game channel diverged and physics agrees" — which rules out most of the codebase before
  any bisecting starts, since the three causes barely overlap. `SimDesyncDetector.RecordLocal` and
  `RecordPeer` now take `SimStateHashes` rather than a `ulong`; `SimDesyncReport.LocalHash` and
  `PeerHash` remain as the folded values.

### Fixed

- **No session had ever actually enabled `eENABLE_ENHANCED_DETERMINISM`, and every session had
  enabled CCD.** `SimSceneFlags` claimed to mirror `pxw::PxwSceneFlag` and did not: it declared
  `EnhancedDeterminism` at bit 0 where the native header has `eENABLE_PCM`, and each subsequent
  member was off by one from there. The default preset sent `0b100011`, which the native side
  read as PCM plus CCD plus a GPU-only flag, with enhanced determinism and the PVD suppression
  both absent. Enhanced determinism is the flag that makes a result independent of the order
  PhysX visits actors and islands in, which is the entire basis for two peers agreeing, so it
  was missing from precisely the thing it exists to guarantee. Nothing caught it: the bits still
  compiled, the raw switch on the native side has no unknown-flag case, and the config hash
  covers the managed booleans rather than the translated flags, so both peers computed the same
  wrong number and agreed at the handshake. Symptom was a physics-only desync that appeared only
  once bodies formed a multi-constraint island — several dynamics resting against several statics
  at once — and stayed away while they only touched a single static apiece. The values now match
  the   header, and `SimTimingTests.SceneFlagsMatchTheNativeHeader` pins all eight against a
  hand transcription of it.

- **A peer with `LocalInputDelay > 0` stalled before it ran a single tick.** Stamping local input
  for `RollbackEngine.LocalInputTick` — what the README, `Architecture.md` §7.4 and `Gameplay.md`
  all told you to do — left the first `LocalInputDelay` ticks of the session covered by nobody, and
  `InputBuffer.ConfirmedThrough` advances only over ticks every player has filled. So the frontier
  never left the start, the clock ran out to its bound and stopped, and the game came up with input
  doing nothing at all. A second gap opened later in the same stream: the clock does not move one
  tick per frame, since the first `Advance` under a fixed horizon jumps it from the confirmed tick
  to the end of the prediction window, and the tick to stamp jumped with it.
  `SimSession.SubmitLocalInput` now submits the whole run from the last tick it sent through the
  one being stamped, seeded by `Start` and re-seeded by `NotifyRebuilt`, which closes both — a
  copied sample is also what the other peers' prediction already assumed for the skipped ticks.
  Callers driving `RollbackEngine` directly must loop the same way; the docs above now show it.
- **Conditional rollback ran the simulation `LocalInputDelay` times too fast.** Draining the whole
  confirmed backlog per `Advance` (Phase 2) was justified on the grounds that PGS transparency
  makes a confirmed tick independent of its predecessor, which is true, but the
  one-confirmation-per-call cap had a second, unwritten job: pacing the simulation against wall
  time. `AdvanceConfirmed` drags the clock up to whatever it confirms, and local input is stamped
  ahead, so a peer whose own input is the last one a tick waits on — guaranteed on a solo host —
  found the frontier permanently in the future and drained to it every frame. Confirmation is now
  capped at one tick per call on the fixed-horizon path and at the wall clock under
  `FreeRunningClock`. Shorter replays are unaffected; only the rate is.
- Vehicles registered through UNDPWR were added to the scene but never stepped, and disabling
  a vehicle did nothing — both silent failures, now corrected (see above).
- Contact and trigger draining were no-op native stubs that returned nothing to a managed
  layer that drains every tick; they now deliver deterministic, sorted events.

---

## Determinism-contract history

For quick reference when diagnosing a version mismatch between peers:

| change | `kStateVersion` | notes |
| --- | --- | --- |
| vehicle integrator payload | 3 | per-wheel and drivetrain state added to the snapshot |
| native gameplay API + pose-layout fix | 2 | `PxwPose` quaternion-first across the interop boundary |
| initial rollback layer | 1 | rigid + articulation cold-step capture/restore |
