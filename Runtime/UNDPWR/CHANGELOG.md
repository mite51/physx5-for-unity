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

### Fixed

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
