# Simulation APIs

Three groups of calls let gameplay push into and read from the world: forces (`SimBody`),
scene queries (`SimQuery`), and contact/trigger events (`SimContacts`). All three are backed by
the native plugin and run today. They share one rule.

## The one rule

**Every one of these runs inside the tick, and the tick is replayed.** Call them only from a
step handler — `OnBeforeStep` for forces and queries, `OnAfterStep` for the contact drain, or
the equivalent `SimGameEntity`/`SimGameMode` callbacks the host runs at those points. A force
applied or a query issued from an ordinary `Update` or a collision callback happens on the
original pass and not on the replay, which desyncs a peer against itself — the most confusing
failure this framework has.

## Forces and body I/O: `SimBody`

[`SimBody`](../Gameplay/SimBody.cs) is a thin static wrapper over the native per-body entry
points. Overloads take either a raw `IntPtr` handle or a `SimEntity` (resolved through
`SimEntity.BodyHandle`, which is the chassis actor for a vehicle and the actor itself for
everything else). From a `SimGameEntity`, pass its `Registration` (the `SimEntity`).

```csharp
public override void OnSimUpdate(int tick, bool isReplay)
{
    SimBody.AddForce(Registration, direction * 20f, SimForceMode.Acceleration);
    SimBody.AddTorque(Registration, Vector3.up * spin, SimForceMode.Force);

    Vector3 v = SimBody.GetLinearVelocity(Registration);   // reads are safe in a step handler
    if (v.magnitude > maxSpeed)
        SimBody.SetLinearVelocity(Registration, v.normalized * maxSpeed);
}
```

`SimForceMode` mirrors PhysX: `Force`, `Impulse`, `VelocityChange`, `Acceleration`. Reads
(`GetPose`, `GetPosition`, `GetRotation`, `GetLinearVelocity`, `GetAngularVelocity`, `GetMass`)
are safe anywhere the world is not mid-simulate, but anything that feeds a read back into a
force must do both inside the same step handler.

`SimBody.Teleport(handle, position, rotation, velocity, angularVelocity)` places a body rather
than pushing it, and re-pins the wake counter like a restore does — use it only when activating
a pooled entity, never for ordinary movement. The pool does this for you on spawn.

## Scene queries: `SimQuery`

[`SimQuery`](../Gameplay/SimQuery.cs) (reachable as `context.Query`) provides raycasts, overlaps
and sweeps that resolve every hit to a stable ID and return them in a **reproducible order** —
rays and sweeps sorted by distance with stable ID breaking ties, overlaps sorted by stable ID.
This is the whole point: Unity's own `Physics.Raycast`/`OverlapSphere` return hits in an order
PhysX does not guarantee, so two peers iterating the same hits could pick a different "first"
one and diverge.

```csharp
public override void OnResolveVolumes(int tick)
{
    var hits = _overlapScratch; // a caller-owned SimOverlapHit[], reused each tick — no allocation
    int count = context.Query.OverlapSphere(hillCenter, hillRadius, filterMask: 0, hits);
    for (int i = 0; i < count; ++i)
        Capture(hits[i].StableId);
}
```

Methods: `Raycast`, `OverlapSphere`, `OverlapBox`, `OverlapCapsule`, `SweepSphere`. Each fills a
caller-owned array and returns how many hits were written, so a query in the tick loop does not
allocate. If the array is smaller than the number of hits, the deterministically-ordered front
of the list is kept (nearest for rays/sweeps, lowest-ID for overlaps). A `filterMask` of zero
matches everything; otherwise it is compared against each shape's query group.

For a one-off overlap check, an explicit `OverlapSphere`/`OverlapBox` in a step handler is often
clearer than a trigger volume, because it is evaluated at a known point in the tick rather than
reported after it.

## Contacts and triggers: `SimContacts`

[`SimContacts`](../Gameplay/SimContacts.cs) (reachable as `context.Contacts`) drains the
contact and trigger events a step produced. PhysX reports them in an order that follows internal
pair bookkeeping — the kind of state a snapshot cannot carry — so the native layer normalises
each pair to ascending stable-ID order (`idA < idB`, normal oriented A→B) and sorts the whole
buffer before it crosses the boundary. Peers and replays see the same events in the same order.

If you use `SimGameHost`, you do not drain manually — the host drains after each step and calls
`ISimGameMode.OnContact` / `OnTrigger` once per event, growing its scratch buffers on overflow.
Draining by hand is for the bare-engine path:

```csharp
public void OnAfterStep(DeterministicWorld world, int tick, bool isReplay)
{
    int count = context.Contacts.Drain(_contactScratch);
    for (int i = 0; i < count; ++i) { /* react to _contactScratch[i] */ }
}
```

### Two disciplines

1. **A contact exists only for the tick it happened on.** Anything a later tick needs to know
   about it must be written into the entity or game channel during the same handler. A contact
   remembered in a plain field is forgotten by the next restore and reappears differently on the
   replay.
2. **Ordering is reproducible; the contact point, normal and impulse are not bit-exact across a
   cold restore.** They derive from solver warm-start state the snapshot deliberately does not
   carry — the same "as close as possible, not bit-exact" property poses have. Branch your
   hashed state on *which* bodies touched (the pair and order are reproducible), never on the
   exact impulse or point.
