# Configuration

Configuration is split by responsibility:

- `SimConfig` contains deterministic simulation and snapshot storage.
- `SimNetConfig` contains authoritative scheduling, adaptive lead, per-frame work and recovery.

Both hashes are checked during admission.

```csharp
var simulation = SimConfig.Deterministic;
var network = SimNetConfig.Authoritative;

if (!simulation.Validate(out string simulationProblem))
    throw new InvalidOperationException(simulationProblem);
if (!network.Validate(simulation, out string networkProblem))
    throw new InvalidOperationException(networkProblem);
```

## Authoritative defaults at 60 Hz

| Setting | Default | Purpose |
| --- | ---: | --- |
| `SimConfig.TickRate` | 60 | Fixed deterministic ticks per second. |
| `SimConfig.SnapshotHistory` | 64 | Rollback and recovery history. |
| `InitialInputLead` | 3 | Starting future scheduling lead. |
| `MinimumInputLead` | 1 | Zero-delay networking is intentionally unsupported. |
| `MaximumInputLead` | 8 | Adaptive lead and fairness ceiling. |
| `InputLeadSafetyMargin` | 1 | Extra tick above measured relay latency. |
| `ServerMaxFutureTicks` | 12 | Furthest proposal accepted by the server. |
| `InputRedundancy` | 4 | Recent proposals resent on each unreliable send. |
| `CanonicalFrameRedundancy` | 4 | Recent canonical frames resent by the server. |
| `MaxSimulationStepsPerFrame` | 8 | Confirmed, replay and catch-up steps combined. |
| `CatchUpWarningTicks` | 12 | Soft backlog warning. |
| `HardResyncTicks` | 30 | Rebuild threshold (500 ms). |
| `LateSamplesBeforeLeadIncrease` | 2 | Fast upward hysteresis. |
| `StableSecondsBeforeLeadDecrease` | 5 | Slow downward hysteresis. |

`SimNetConfig.Validate` requires snapshot history to exceed hard-resync distance plus maximum
input lead and one predecessor tick. Snapshot memory scales with world-state size, so profile
large worlds before increasing history further.

## Fixed PGS solver

UNDPWR always uses PhysX Projected Gauss-Seidel. Solver selection is not a public option: TGS
carries state snapshots cannot restore and cannot support variable-depth rollback. Position and
velocity iteration counts remain deterministic `SimConfig` fields.

## Simulation settings

`SimConfig.ComputeHash()` covers tick rate, gravity, solver iterations, collision thresholds,
CCD, stabilization, PCM, framework sleep policy, density, mass isotropy, backend and CPU worker
count. Diagnostics and `SnapshotHistory` are excluded because they do not alter a tick's result.

Sleeping is off by default (`SleepTicks = 0`). CCD and stabilization are off; PCM is on.

## GPU backend

`SimBackendMode.GpuExperimental` remains available for non-networked or presentation worlds.
PhysX does not guarantee GPU determinism across machines, so `SimNetConfig.Validate` rejects it
for authoritative sessions. There is no experimental networking bypass.

## Diagnostics

- `DisablePvd`: suppresses the PhysX Visual Debugger connection.
- `PerEntityHashDiagnostics`: retains per-entity hashes for desync attribution.
- `SimLog.Level`: runtime logging verbosity.

These do not change simulation and are not part of the admission hash.
