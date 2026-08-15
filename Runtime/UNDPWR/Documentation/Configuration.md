# Configuration

[`SimConfig`](../Core/SimConfig.cs) is the single place simulation-affecting settings live, so
they can be hashed as a unit and checked at join. A field that changes the simulation and is
not in `SimConfig` is a determinism bug waiting to happen.

Start from `SimConfig.Deterministic` and change only what a design actually requires — every
field is set explicitly rather than left to a PhysX default, because a default that shifts
between SDK versions is a silent break.

```csharp
var config = SimConfig.Deterministic;
config.TickRate = 60;
config.LocalInputDelay = 2;   // peer-local
config.SnapshotHistory = 32;  // peer-local
```

## Hashed vs peer-local

`ComputeHash()` covers every field that affects the simulation, and the handshake refuses a
peer whose hash differs. Two categories are deliberately **excluded**:

- **Diagnostics** (`DisablePvd`, `PerEntityHashDiagnostics`) — they change what is observed,
  never what is simulated, so a peer investigating a desync must not be rejected.
- **Peer-local latency** (`LocalInputDelay`, `SnapshotHistory`) — they change when a peer
  produces an input and how long it keeps a snapshot, never what the simulation does. Hashing
  them would reject a session over a difference that cannot desync it.

Everything else — tick rate, gravity, solver and iterations, thresholds, density, isotropy
tolerance, sleep policy, backend, worker threads — is hashed and must match on every peer.

| Field | Default | Hashed | Notes |
| --- | --- | :---: | --- |
| `TickRate` | 60 | yes | Ticks per second. Same inputs at 50 and 60 Hz diverge. `FixedDeltaTime` derives from it. |
| `LocalInputDelay` | 2 | no | Ticks a peer stamps its own input ahead. Peer-local responsiveness knob. See below. |
| `SnapshotHistory` | 32 | no | Snapshots retained; bounds rollback distance and lead. Must exceed `LocalInputDelay + 1`. |
| `Gravity` | (0, -9.81, 0) | yes | m/s². |
| `Solver` | `ProjectedGaussSeidel` | yes | **Must be PGS** for a networked session. See below. |
| `SolverPositionIterations` | 8 | yes | Per dynamic body. |
| `SolverVelocityIterations` | 2 | yes | Per dynamic body. |
| `BounceThresholdVelocity` | 0.2 | yes | Below this, contacts stop bouncing. |
| `FrictionOffsetThreshold` | 0.04 | yes | Friction anchor merge distance. |
| `CcdMaxPasses` | 1 | yes | Max CCD passes per step. |
| `EnableCcd` | false | yes | Continuous collision detection. |
| `EnableStabilization` | false | yes | Stabilization pass for resting stacks. |
| `EnablePcm` | true | yes | Persistent contact manifolds. |
| `SleepLinearThreshold` | 0.05 | yes | m/s below which a body counts as at rest. |
| `SleepAngularThreshold` | 0.05 | yes | rad/s below which a body counts as at rest. |
| `SleepTicks` | 0 | yes | Ticks at rest before sleeping. 0 keeps everything awake. See below. |
| `DefaultDensity` | 1000 | yes | Used when mass is computed rather than authored. |
| `MassIsotropyTolerance` | 0.01 | yes | Relative spread below which a mass frame collapses to identity. See [World and actors](WorldAndActors.md#mass-properties). |
| `Backend` | `Cpu` | yes | `Cpu` or `GpuExperimental`. See below. |
| `CpuWorkerThreads` | 0 | yes | 0 keeps the sim on the calling thread. |
| `DisablePvd` | true | no | Suppresses the PhysX Visual Debugger connection. |
| `PerEntityHashDiagnostics` | false | no | Records per-entity hashes each confirmed tick so a desync can name the body. |

## Latency knobs

`LocalInputDelay` and `SnapshotHistory` are the two knobs that shape latency handling, covered
conceptually in [Concepts](Concepts.md#latency-two-peer-local-knobs):

- **`LocalInputDelay`** buys mispredictions that never happen — an input crossing the network
  faster than the delay arrives before anyone predicts it — at the cost of the local player's
  own action landing that many ticks later. Zero is the most responsive and mispredicts most.
  Because it is peer-local, a peer on a worse link can raise it without agreeing with anyone,
  though a competitive game may choose to agree on it for fairness.
- **`SnapshotHistory`** bounds how far the clock may lead the confirmed tick:
  `SnapshotHistory - LocalInputDelay - 1` ticks of lead, which is where the peer stalls.
  `Validate` requires it to exceed `LocalInputDelay + 1`. A peer that retains more history just
  tolerates a later input and a larger lead.

## The solver must be PGS

`Validate` refuses any solver but `ProjectedGaussSeidel` for a networked session. The rollback
engine rewinds a data-dependent depth and leads a data-dependent distance, which only lands on
the same state a full re-simulation would when replay is bitwise transparent — a property PGS
has under the cold-step discipline and TGS does not. `SimSolverType.TemporalGaussSeidel` exists
in the enum for interop numbering, but selecting it for a networked world is rejected at world
creation. See [Concepts](Concepts.md#cold-steps).

## Sleeping

The framework decides sleeping, not PhysX, because PhysX's sleep timing depends on internal
contact bookkeeping a snapshot cannot carry and so would not replay. The rest counter that
drives the framework's decision is in the snapshot, and the wake counter is pinned high while a
body is awake so PhysX's own path never runs. Sleeping is **off by default** (`SleepTicks = 0`);
set it to a positive count to enable it. All three sleep fields are hashed, because a peer that
slept on a different schedule would diverge only once something settles — late and hard to
attribute. Note the one caveat in [Limits and platforms](LimitsAndPlatforms.md): a sleeper woken
by a *new* contact under rollback is not bit-exact.

## The GPU backend

`SimBackendMode.GpuExperimental` exists, but PhysX gives no cross-machine determinism guarantee
for GPU simulation — results depend on the driver, the card, and the scheduling of thousands of
concurrent blocks. A networked world **refuses to start** in that mode unless
`AllowExperimentalGpuNetworking` is set, and logs why. Use it for single-player or
presentation-only worlds.

## Validation

Always let the world constructor validate — it throws `ArgumentException` with a reason when a
field would make the simulation non-deterministic or the ring unusable. To check without
constructing:

```csharp
if (!config.Validate(out string reason))
    Debug.LogError(reason);
```
