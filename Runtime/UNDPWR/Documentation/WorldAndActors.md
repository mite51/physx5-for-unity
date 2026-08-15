# World and actors

The deterministic world owns the PhysX scene, the stable-ID registry, and the snapshot
operations rollback needs. Getting actors into it correctly is the single most important thing
you do — the majority of desyncs that "look like a physics bug" are actually an actor
registered in the wrong order or added to the scene twice.

## Creating the world

[`DeterministicWorld`](../Core/DeterministicWorld.cs) is created from a validated
[`SimConfig`](../Core/SimConfig.cs). A peer runs exactly one world.

```csharp
var config = SimConfig.Deterministic;
var world = new DeterministicWorld(config); // throws if config.Validate fails
```

The constructor validates the config (PGS required, ring large enough, etc.) and creates the
native scene. `world.ScenePtr` is the native `PxScene` you create actors in;
`world.Dispose()` frees it.

## Stable IDs

Every object a peer must agree on carries a stable ID from
[`StableIdAllocator`](../Core/StableIdAllocator.cs). The ID space is partitioned so different
sources cannot collide:

| Range | Values | Use |
| --- | --- | --- |
| Authored | `1 .. 0x0FFFFFFF` | Content placed in a scene. Assigned at author time and baked; register with `RegisterAuthored` to catch collisions at load. |
| Deterministic runtime | `0x10000000 .. 0x7FFFFFFF` | Objects spawned during a session. `Allocate(currentTick)` returns a pure function of the session seed and the allocation count, so every peer allocates the same IDs in the same order. |
| Local | `0x80000000 ..` | Objects that exist on one peer only (a debug visualiser). `AllocateLocal()`. Never registered with the world. |

```csharp
var ids = new StableIdAllocator(sessionSeed); // sessionSeed agreed by every peer
uint spawnedId = ids.Allocate(context.CurrentTick);
```

> Runtime IDs are only deterministic while allocation happens **inside the tick loop**, on a
> tick every peer runs. Allocating from a UI callback, coroutine or network handler desyncs the
> allocator itself; `Allocate` logs a warning when the tick goes backwards, which is the signal
> you have done this.

## Registration is deferred and ordered

PhysX only guarantees reproducible results when actors enter the scene in the same order.
Gameplay spawns things in whatever order it likes, and two peers will not agree on that order.
So [`DeterministicWorld.Register`](../Core/DeterministicWorld.cs) only *records intent* — the
actor reaches the scene when `CommitPending` runs, sorted by stable ID.

```csharp
SimEntity crate = world.Register(crateId, cratePtr, SimHandleKind.RigidDynamic);
SimEntity ramp  = world.Register(rampId, rampPtr, SimHandleKind.RigidStatic);
// Registration order above is irrelevant; the commit inserts by ascending stable ID.
```

`SimHandleKind` distinguishes `RigidDynamic`, `RigidStatic`, `RigidKinematic`, `Articulation`
and `Vehicle`. Dynamic bodies additionally get the hashed solver iteration counts and the
deterministic defaults (speculative CCD off, bounded depenetration) pushed onto them at
registration.

**You do not usually call `CommitPending` yourself.** The engine (`Initialise`, and each
`Advance`) commits at the tick boundary. Committing mid-replay would give an actor a different
history than the peers that committed it at the boundary — never do it.

## Registering Unity PhysX actors

The core `DeterministicWorld.Register` takes a raw native handle so the core does not depend on
Unity component types. When you have a `PhysxActor`, use
[`SimActorBridge`](../../UNDPWR.Unity/SimActorBridge.cs) (in the `UNDPWR.Unity` assembly),
which resolves the correct handle and kind for you:

```csharp
using UNDPWR.Unity;

SimEntity entity = SimActorBridge.Register(world, stableId, physxActor);
```

The handle is **not always** the component's `NativeObjectPtr`:

- An articulation registers the root's `PxArticulationReducedCoordinate`
  (`PhysxArticulationBody.GetArticulation()`), not a link's actor. Register the root, not a link.
- A vehicle registers its `PxwVehicle` (`PhysxVehicle.VehiclePtr`), not the chassis actor.

`SimActorBridge.TryResolveHandle` encapsulates that rule; `Register` calls it and throws a
clear error if the actor has no usable handle yet (not created, or a non-root link).

## Two lifecycle rules the bridge cannot enforce

These are the classic desync sources, and nothing can check them for you:

1. **Create the actor in the world's scene.** Build the `PhysxActor` against a `PhysxScene`
   whose handle is the world's, using `SimActorBridge.CreateWorldScene(world)`, with
   `externalSceneMembership = true`. The returned scene is a view onto the world's scene, not a
   second scene.

   ```csharp
   PhysxScene worldScene = SimActorBridge.CreateWorldScene(world);
   myActor.Scene = worldScene;
   myActor.externalSceneMembership = true;
   ```

2. **Do not let the Unity component add itself to the scene** through its own `OnEnable`. The
   world adds it, in stable-ID order, at `CommitPending`. Adding it twice, or in a different
   order on another peer, is a desync that presents as a physics bug.

## Mass properties

Mass looks like local setup but is simulation input, and it is subtler than it looks. PhysX
diagonalises the inertia tensor and stores the eigenvector rotation as the centre-of-mass
orientation; for a near-isotropic body those eigenvectors are ill-defined, so a tiny change in
shape layout can swing the mass frame wildly and diverge peers.

[`SimMass`](../Core/SimMass.cs) removes that: it sorts shape contributions into a canonical
order before summing, collapses a near-isotropic mass frame to identity (within
`SimConfig.MassIsotropyTolerance`), and canonicalises the quaternion sign. Set it up once per
body after registration:

```csharp
SimMass.Setup(cratePtr, config.DefaultDensity, config.MassIsotropyTolerance);
```

Compute once and **replicate anyway** — do not rely on every peer recomputing the same thing.
`SimMass.Hash` covers mass, inertia, mass frame and shape count, so a mismatched peer is caught
at join rather than twenty seconds into a match. A near-spherical compound just above the
default tolerance can be given a wider tolerance for that one body rather than widening the
global default.

## Enabling and disabling instead of unregistering

For anything that comes and goes — a pooled projectile — prefer
`world.SetEntityEnabled(stableId, false)` over `Unregister`. Enabling and disabling keeps the
stable ID and the snapshot layout, and the enabled flag lives in the snapshot so it rolls back.
Unregistering changes the snapshot layout, and a layout change part-way through a session has
to be agreed by every peer. The gameplay pool ([Gameplay](Gameplay.md)) is built entirely on
this: spawn is enable-plus-teleport, despawn is disable.

## Reading poses for rendering

`world.ReadPoses(out count)` returns the committed poses for display. Never read a Unity
`transform` back into the simulation. The gameplay layer's
[`SimPresentationBinder`](../Gameplay/Presentation/SimPresentationBinder.cs) does interpolated
readback for every entity; see [The gameplay layer](Gameplay.md).
