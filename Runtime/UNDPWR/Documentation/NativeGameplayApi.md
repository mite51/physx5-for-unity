# Native gameplay API

The determinism layer captures, restores and steps a world, but it gives gameplay no way
to *push* anything into that world or *ask* anything of it. A game needs forces, scene
queries and contact reports, and none of those exist in the native plugin yet:
`Physx.Core.cs` exposes only `SetLinearVelocity`, `SetAngularVelocity`, `SetRigidActorPose`
and `SetMass`.

This document specifies the entry points the managed gameplay layer already calls, so the
native work can proceed against a fixed contract. The managed side is written and compiles;
until the native side lands, any of these calls will fail to resolve at load. Every one is
declared in [`Interop/NativeMethods.cs`](../Interop/NativeMethods.cs) and wrapped by
[`Gameplay/SimBody.cs`](../Gameplay/SimBody.cs),
[`Gameplay/SimQuery.cs`](../Gameplay/SimQuery.cs) and
[`Gameplay/SimContacts.cs`](../Gameplay/SimContacts.cs).

All functions are `extern "C"`, `cdecl`, and follow the existing `Pxw` naming. Structs must
match the layouts in [`Interop/NativeGameplayTypes.cs`](../Interop/NativeGameplayTypes.cs)
field-for-field, since arrays of them are memcpy'd across the boundary in bulk.

## The one rule these must not break

Everything in this API runs inside the tick loop, and the tick loop is replayed. For the
confirmed step to match across peers every operation within a tick must be issued in the same
order on each — a requirement that survives independent of how far any peer predicts — so it
extends to every function here. Two obligations follow, and both are about *order*, because
order is what a snapshot cannot restore and therefore what silently diverges.

- **Forces** must be applied only in `OnBeforeStep`, and the framework guarantees it calls
  the step handlers in a fixed order, so the native side does not need to sort force
  application. It only needs to apply forces exactly as PhysX would, so a replayed
  application reproduces the original.
- **Queries and contact reports** return *sets*, and PhysX has no reproducible order for the
  members of a set. The native side must impose one. This is not optional and not a
  nicety: an unsorted hit list is a desync that presents as a gameplay bug and is diagnosed
  the same expensive way as a physics one.

## Forces on a body

```c
void PxwBodyAddForce (PxActor* actor, const PxVec3* force,  uint32_t mode);
void PxwBodyAddTorque(PxActor* actor, const PxVec3* torque, uint32_t mode);
```

`mode` is a `physx::PxForceMode` value, mirrored by `SimForceMode` (Force 0, Impulse 1,
VelocityChange 2, Acceleration 3). These are thin forwarders to
`PxRigidBody::addForce` / `addTorque`. They accumulate into the body's per-step
accumulator, which PhysX clears on the next step, so a replayed `OnBeforeStep` that applies
the same force to the same restored state reproduces the original step. No sorting is
required because the framework fixes the handler order.

## Reads and direct writes on a body

```c
void  PxwBodyGetPose           (PxActor* actor, PxwTransformData* outPose);
void  PxwBodyTeleport          (PxActor* actor, const PxwTransformData* pose,
                                const PxVec3* velocity, const PxVec3* angularVelocity);
void  PxwBodyGetLinearVelocity (PxActor* actor, PxVec3* outVelocity);
void  PxwBodySetLinearVelocity (PxActor* actor, const PxVec3* velocity);
void  PxwBodyGetAngularVelocity(PxActor* actor, PxVec3* outVelocity);
void  PxwBodySetAngularVelocity(PxActor* actor, const PxVec3* velocity);
float PxwBodyGetMass           (PxActor* actor);
```

Straight forwarders to the corresponding `PxRigidBody` / `PxRigidActor` methods. Gameplay
uses the reads to clamp speed and to orient toward motion, and the velocity writes for the
same speed clamp; they are captured by the physics snapshot like any other body state, so
nothing special is needed to make them replay. `PxwBodyGetPose` returns a `PxTransform`
in the same quaternion-first layout as the rest of the framework's `SimTransform`.

`PxwBodyTeleport` places a body and sets both velocities in one call, for bringing a pooled
object into play. It must re-pin the wake counter exactly as a restore does (see
`Architecture.md` §5.6), so a spawned body is awake and simulated rather than inheriting
whatever sleep state the pooled slot last held. It is a placement, not a physical move, and
gameplay uses it only when activating a pooled entity — never for ordinary movement.

## Scene queries

```c
uint32_t PxwWorldRaycast(
    PxwWorld* world,
    const PxVec3* origin, const PxVec3* direction, float maxDistance,
    uint32_t filterMask,
    PxwRaycastHit* hits, uint32_t capacity);

uint32_t PxwWorldOverlap(
    PxwWorld* world,
    uint32_t shape,                         // PxwQueryShape: 0 sphere, 1 box, 2 capsule
    const PxVec3* center, const PxVec3* halfExtents, float radius,
    const PxQuat* rotation,
    uint32_t filterMask,
    PxwOverlapHit* hits, uint32_t capacity);

uint32_t PxwWorldSweep(
    PxwWorld* world,
    uint32_t shape,
    const PxVec3* origin, const PxVec3* halfExtents, float radius,
    const PxQuat* rotation,
    const PxVec3* direction, float maxDistance,
    uint32_t filterMask,
    PxwRaycastHit* hits, uint32_t capacity);
```

Each returns the number of hits written, which may be fewer than were found if `capacity`
is smaller. Requirements:

- **Every hit resolves to a stable ID.** The registry already maps `PxActor*` to stable ID;
  a hit on an actor not in the registry is dropped rather than reported with a fabricated
  ID.
- **`PxwWorldRaycast` and `PxwWorldSweep` return hits sorted by `Distance` ascending, with
  `StableId` ascending as the tiebreak.** Ties are common — two shapes touched at the same
  range — and must resolve identically on every peer.
- **`PxwWorldOverlap` returns hits sorted by `StableId` ascending.** An overlap has no
  distance to sort by, so ID order is the imposed order.
- **When `capacity` truncates, the retained hits are the front of the sorted list** — the
  nearest for rays and sweeps, the lowest IDs for overlaps — not an arbitrary subset.
- **`filterMask` is ANDed against a per-shape query group**; zero matches everything. The
  group is a game concept; the native side only needs to store a `uint32_t` per shape and
  compare it. (Where that word is set is a later question; a sensible default is the actor's
  registered kind.)
- Queries run against the current committed scene. They must not be called while the scene
  is mid-`simulate`; the managed layer only calls them from step handlers, which are outside
  the simulate window.

## Contacts and triggers

```c
uint32_t PxwWorldDrainContacts(PxwWorld* world, PxwContactEvent* dst, uint32_t capacity);
uint32_t PxwWorldDrainTriggers(PxwWorld* world, PxwTriggerEvent* dst, uint32_t capacity);
```

Both drain the events accumulated by the most recent step and return how many were written.
Requirements:

- The world's `PxSimulationEventCallback` collects contacts (with
  `PxPairFlag::eNOTIFY_TOUCH_FOUND | eNOTIFY_TOUCH_PERSISTS | eNOTIFY_CONTACT_POINTS`) and
  trigger pairs into an internal buffer during `simulate`.
- **`PxwContactEvent` pairs are normalised so `IdA < IdB`**, and the buffer is **sorted by
  `(IdA, IdB)`**. `Normal` points from A toward B; `Impulse` is the summed normal impulse.
- **`PxwTriggerEvent` pairs are sorted by `(TriggerId, OtherId)`**, with `Status` a
  `PxwTriggerStatus` (0 lost, 1 found).
- Draining clears the internal buffer, so a second drain in the same tick returns nothing.
  The framework drains exactly once per step, in `OnAfterStep`.
- Both must resolve `PxActor*` to stable IDs and drop pairs involving an unregistered actor.

Gameplay is expected to prefer explicit `PxwWorldOverlap` in a step handler over trigger
events for anything that must apply a force, because a query is evaluated at a known point
in the tick whereas a trigger merely records that an overlap happened. Triggers exist for
the cases where polling every volume every tick would be wasteful.

## Why the sort lives here and not in managed code

Two reasons. The distances and the pair identities are already in hand natively, so sorting
there is a single pass over data that is already resident; doing it in managed code means
marshalling an unsorted array across the boundary first. And putting it here makes it
impossible for a caller to forget — there is exactly one place the order is decided, and it
is the place that also decides the stable IDs the order is defined in terms of.
