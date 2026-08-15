# Limits and platforms

What the framework does not guarantee, and where. Read this before choosing target platforms or
designing gameplay that leans on physics edge cases.

## Peers must share a CPU architecture

This is the hard one. PhysX compiles fundamentally different arithmetic on different
architectures — x86 uses SSE, ARM falls through to a scalar backend, and their approximate
reciprocals (`_mm_rcp_ps`, `_mm_rsqrt_ps` and equivalents) differ by about 3.7e-4, roughly four
orders of magnitude above the framework's noise floor. Mixing them does not drift apart slowly;
it never agrees at all, from the first step.

- **x86 ↔ ARM (e.g. PC ↔ mobile) does not work** and is not a configuration toggle. It is a
  PhysX build-configuration project (`PX_SIMD_DISABLED` and more), sitting underneath the fact
  that this PhysX 5 distribution has no Android build at all. Treat cross-architecture play as a
  separate project, and design architecture-segregated matchmaking (PC with PC, mobile with
  mobile) as the fallback.
- **Intel ↔ AMD** may also disagree, because those approximate-reciprocal instructions are only
  specified by an error bound and have historically differed between vendors. If your PC-to-PC
  testing has all been on one vendor, run the managed/native determinism tests on one Intel and
  one AMD machine and diff the hashes before assuming desktop crossplay is safe.

Nothing in the netcode assumes a single architecture — it assumes the same operation on the same
bytes gives the same result. The full analysis, and what it would take to change, is in the
archived [cross-platform determinism investigation](Archive/CrossPlatformDeterminism.md); for
shipping decisions the summary above is the operative part.

## The GPU backend is not cross-machine deterministic

`SimBackendMode.GpuExperimental` has no cross-machine determinism guarantee (driver, card and
block scheduling all vary), so a networked world refuses to start on it unless
`AllowExperimentalGpuNetworking` is set. Single-player and presentation-only worlds are fine.
See [Configuration](Configuration.md#the-gpu-backend).

## Contact points and impulses are not bit-exact across a rollback

A contact's *point, normal and impulse* derive from solver warm-start state the snapshot
deliberately does not carry, so they are only approximate across a cold restore — the same "as
close as possible" property poses have. The pair of bodies and their order are fully
reproducible. Branch hashed gameplay state on *which* bodies touched, never on the exact impulse
or point. See [Simulation APIs](SimulationAPIs.md#contacts-and-triggers-simcontacts).

## Waking a sleeper under rollback is not bit-exact

Sleeping is deterministic and replays (the rest counter is snapshotted), but the *wake
transition itself* is not bit-exact when a body is woken by a **new** contact under rollback:
that fresh contact's solver warm-start state is exactly the uncaptured state a cold restore
cannot reproduce. The settling before the wake replays bit-exactly; the wake tick is where a
divergence can appear. Gameplay must treat a rollback-spanning wake the way it treats a contact
impulse — branch on the fact that a body woke, never on the exact tick or the resulting
velocity. Sleeping is off by default; see [Configuration](Configuration.md#sleeping).

## Contact chains deeper than eight bodies

Variable rollback depth stays bit-exact up to a contact chain about eight bodies deep; a chain
of nine or more can diverge under variable-depth rollback, on either solver. This is a content
constraint the native chain-depth diagnostic measures rather than a solver choice — avoid
designing around tall stacks that must survive deep rollbacks.

## PhysX patch dependency

The framework depends on two patches to PhysX itself (a self-validating actor-pose cache and a
guard against an unchanged mass frame rotating the actor by an ulp per call) that make the pose
round trip lossless for bodies with a rotated mass frame. Do not upgrade the underlying PhysX
without re-applying them and re-running the pose round-trip tests, or capture/restore becomes
lossy for such bodies.

## Scope

The framework is in scope for rigid bodies, articulations and vehicles; rollback and prediction;
mid-match join and desync recovery; deterministic identity and mass. It is **out of scope** for
the transport implementation and matchmaking (you supply `ISimTransport`), rendering, gameplay
logic, and cross-architecture play.
