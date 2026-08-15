# UNDPWR manual

**Unity Networked Deterministic Physics With Rollback** — rollback netcode for PhysX 5 in
Unity. Peers exchange only inputs and recompute the physics identically, so bandwidth stays
flat as the scene grows. The [package README](../README.md) is the short version and the quick
start; this is the full guide.

## Chapters

| Chapter | What it covers |
| --- | --- |
| [Getting started](GettingStarted.md) | Prerequisites, the two assemblies, the fixed-update contract, and building up from a local loop to a networked game. Start here. |
| [Concepts](Concepts.md) | What rollback is doing and why: the governing rule, cold steps, why PGS, the free-running clock, the latency knobs, the three state channels, stable IDs. |
| [World and actors](WorldAndActors.md) | `DeterministicWorld`, stable IDs, deferred stable-ID-ordered registration, the Unity actor bridge, mass, enabling vs unregistering. The #1 desync source. |
| [Rollback and input](RollbackAndInput.md) | The step handler, the tick lifecycle, and how to submit input without stalling. |
| [The gameplay layer](Gameplay.md) | Entities, pooling, actions, game modes, the game host's fixed tick order, players and camera-relative input, presentation. The recommended way to build a game. |
| [Networking](Networking.md) | The `ISimTransport` seam, the session loop, the handshake, desync detection, and mid-match join. |
| [Simulation APIs](SimulationAPIs.md) | Forces (`SimBody`), scene queries (`SimQuery`), and contact/trigger events (`SimContacts`), and their determinism rules. |
| [Vehicles and articulations](VehiclesAndArticulations.md) | Handle kinds and registering non-rigid bodies; vehicle commands as input. |
| [Configuration](Configuration.md) | Every `SimConfig` field, hashed vs peer-local, the PGS requirement, sleeping, the GPU backend. |
| [Limits and platforms](LimitsAndPlatforms.md) | The same-architecture requirement, contact/wake bit-exactness caveats, chain depth, GPU, the PhysX patch dependency. |
| [Troubleshooting](Troubleshooting.md) | Symptom-to-cause tables for desyncs and stalls, and the classic mistakes. |

## Reading paths

- **Integrate a game** → [Getting started](GettingStarted.md) → [World and actors](WorldAndActors.md) → [The gameplay layer](Gameplay.md) → [Networking](Networking.md)
- **Tune latency / understand rollback** → [Concepts](Concepts.md) → [Rollback and input](RollbackAndInput.md) → [Configuration](Configuration.md)
- **Fix a desync or a stall** → [Troubleshooting](Troubleshooting.md) → [Limits and platforms](LimitsAndPlatforms.md)
- **Historical design notes** → [Archive](Archive/) (background and measurements, not maintained — do not start here)

## Interop and versioning

[CHANGELOG.md](../CHANGELOG.md) records what has landed and each change to the two numbers peers
must agree on: the managed config hash (`SimConfig.ComputeHash`) and the native snapshot format
(`kStateVersion`). Two peers interoperate only when both match.
