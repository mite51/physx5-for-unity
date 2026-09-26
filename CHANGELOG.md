# Changes

## TD-1 — 2026-09-26 (working tree delivery)

- Direction-aware capsule authoring now composes native local X into X/Y/Z exactly once,
  including articulation child poses. Legacy assets retain their original physical X frame
  until explicitly migrated. Shared shape identity includes the full local pose.
- Capsule dimensions/direction reject changes after native geometry creation. Repeated geometry
  acquisition is idempotent, avoiding leaked cache references during shape creation.
- Disposing one deterministic world no longer resets the process-wide collision-group table.
- Corrected the position-first ABI conversion for the legacy COM pose setter.
- CPU kinematic rollback restores velocity, pending targets and movement/sleep phase. This uses
  two additive exports in the patched PhysX runtime without changing its virtual tables.
  **Snapshot version 4 is required; version 3 snapshots are rejected. Upgrade all peers together.**
- Corrected query body-kind masks, capsule axis, collection limits and allocation documentation.
- Added native regressions, a delivered-DLL .NET smoke test, and a Unity Editor fixture.

See [TD1_HANDOFF.md](TD1_HANDOFF.md) for tested platforms, API contracts and migration steps.
