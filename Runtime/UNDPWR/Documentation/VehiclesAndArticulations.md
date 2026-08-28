# Vehicles and articulations

Vehicles and articulations roll back like any other body — their integrator state is in the
physics channel — but registering them and driving them has two wrinkles worth stating on their
own.

## Handle kinds

The native handle an entity registers under is **not always** its `PhysxActor.NativeObjectPtr`:

| Object | Registered handle | `SimHandleKind` |
| --- | --- | --- |
| Rigid body | the actor's `NativeObjectPtr` | `RigidDynamic` / `RigidStatic` / `RigidKinematic` |
| Articulation | the root's `PxArticulationReducedCoordinate` (`GetArticulation()`) | `Articulation` |
| Vehicle | the `PxwVehicle` (`VehiclePtr`) | `Vehicle` |

Register the articulation **root**, not a link; register the vehicle's `PxwVehicle`, not the
chassis actor. [`SimActorBridge.TryResolveHandle`](../../UNDPWR.Unity/SimActorBridge.cs)
resolves all of this from a `PhysxActor`, so a concrete entity does not have to know the rule —
it returns `false` (with defaulted outs) when the handle is not ready yet (not created, or a
non-root link).

For body I/O, `SimEntity.BodyHandle` resolves to the right thing: a vehicle is pushed and read
through its **chassis** rigid actor even though it registered under the `PxwVehicle` handle, so
`SimBody` calls on a vehicle entity act on the chassis.

## An articulation entity

```csharp
public sealed class Ragdoll : SimGameEntity
{
    [SerializeField] private PhysxArticulationBody _root; // the root body of the articulation

    public override IntPtr ResolveNativeHandle(out SimHandleKind kind)
    {
        IntPtr handle;
        if (!SimActorBridge.TryResolveHandle(_root, out handle, out kind))
            throw new InvalidOperationException("Ragdoll articulation not created yet");
        return handle; // kind == SimHandleKind.Articulation
    }

    protected override void CaptureState(ref SimStateWriter writer) { }
    protected override void RestoreState(ref SimStateReader reader)  { }
}
```

The joint state rolls back inside the physics channel, so `CaptureState`/`RestoreState` only
carry whatever *gameplay* state the entity adds on top.

## A vehicle entity

Registration is the same. The one addition: **a vehicle's commands are input, not simulation
state.** The native snapshot deliberately excludes `PxVehicleCommandState`, so commands must
ride the input frame, exactly like a force — decode the tick's `Input` and apply it from a sim
callback that runs on both the live tick and every replay.

```csharp
using UNDPWR.Unity;

public sealed class Car : SimGameEntity
{
    [SerializeField] private PhysxVehicle _vehicle;

    public override IntPtr ResolveNativeHandle(out SimHandleKind kind)
    {
        IntPtr handle;
        if (!SimActorBridge.TryResolveHandle(_vehicle, out handle, out kind))
            throw new InvalidOperationException("Vehicle not finalized yet");
        return handle; // kind == SimHandleKind.Vehicle
    }

    public override void OnSimUpdate(int tick, bool isReplay)
    {
        // Runs on the original tick and on every replay, so the command is reproduced exactly.
        // Applying it from Update instead would land on one pass and not the other.
        SimVehicleCommands.Apply(_vehicle, Input);
    }

    protected override void CaptureState(ref SimStateWriter writer) { }
    protected override void RestoreState(ref SimStateReader reader)  { }
}
```

[`SimVehicleCommands`](../../UNDPWR.Unity/SimVehicleCommands.cs) does the decode and the
`SetCommands` call. Its default decode follows the `SimInput` axis convention: `AxisW` is
throttle when positive and brake when negative, `AxisZ` is steer, and the low `Buttons` bit
(`SimVehicleCommands.HandbrakeButton`) is the handbrake. A game with a different mapping builds
its own `SimVehicleCommand` and calls the decoded overload:

```csharp
var command = new SimVehicleCommand { Throttle = t, Brake = b, Steer = s };
SimVehicleCommands.Apply(_vehicle, command);
```

The vehicle's wheel, suspension, sticky-tire and drivetrain state (per-wheel rigid-body,
suspension and sticky-tire accumulators, plus engine, gearbox including the in-progress shift
timer, autobox and clutch for engine-drive) is captured in the physics channel; per-step derived
output is recomputed. So the managed `CaptureState`/`RestoreState` only needs whatever gameplay
state the car adds, such as a boost charge.

## Wheel collision shapes

A wheel's collision shape is off by default, and that is a real default rather than an oversight.
PhysX's vehicle SDK gives each wheel a cooked 16-sided convex prism with no shape flags set, so it
collides with nothing and is invisible to scene queries; the suspension raycast and the tire model
are what resolve the wheel against the road. A car works fine like this, and one left alone hashes
exactly as it did before any of this was configurable.

You need to change it when something other than the road has to notice the wheels — a kerb, a
bollard, debris the tire should knock aside — or when a gameplay raycast needs to hit a wheel.
`PhysxVehicleWheelAttachment` carries the per-wheel choices:

| Field | What it does |
| --- | --- |
| `geometryMode` | `CookedPrism` (the SDK's hull, the default), `Cylinder` (a true cylinder sized from the wheel asset), or `Geometry` (uses `collisionGeometry` as-is) |
| `cylinderMargin` | Convex core margin for `Cylinder`, and *size-preserving* — the opposite of a standalone `PhysxCylinderGeometry` margin, which grows the shape. The core shrinks by this much so the wheel keeps the asset's size; a small non-zero value rounds the rim and can steady contact |
| `isSceneQueryShape` | Lets queries hit the wheel. Safe on its own — it changes nothing the solver does |
| `isSimulationShape` | Lets the wheel collide. **Not safe on its own**; see below |
| `collisionGroup` | Group index for the wheel's filter data, for use with `PhysxCollisionGroups` |

### A simulating wheel must be filtered off the road

Turning on `isSimulationShape` without excluding the drivable surface makes the road get resolved
twice: once by the suspension query and tire model, once by the contact solver. The car rides high
and steers badly. So give wheels their own group and disable it against the road's group, during
world construction:

```csharp
const int RoadGroup  = 1;
const int WheelGroup = 2;

PhysxCollisionGroups.SetPairCollides(WheelGroup, RoadGroup, false);
```

Then put the drivable surface's shapes in `RoadGroup` and set each attachment's `collisionGroup`
to `WheelGroup`. Leave the chassis and everything else in `PhysxCollisionGroups.DefaultGroup`,
which collides with everything, so wheels still hit obstacles and the chassis still hits the road.
Call `PhysxCollisionGroups.ResetToDefault()` at teardown — the table is process-global and
outlives the world. The group table folds into the construction hash, so a peer that configures it
differently is caught at join rather than desyncing later; see
[World and actors](WorldAndActors.md#collision-geometry-and-filtering).

Prefer `Cylinder` over `CookedPrism` once a wheel simulates. The vehicle writes the wheel's spin
angle into the shape's local pose every step, so a prism's facets rotate with the wheel and contact
points snap from facet to facet as it turns. A cylinder is rotationally invariant and does not.

### It does not change how the car drives

Swapping the collision geometry does not touch the handling model. The tire model is driven by the
road geometry query, and that query never looks at the wheel's collision shape: the default
raycast mode casts along the suspension travel direction, and even sweep mode sweeps PhysX's own
unit cylinder mesh scaled by the wheel's radius and half width. Measured in the vehicle sample,
prism and cylinder wheels produced bit-identical confirmed-hash streams over a thousand ticks and
identical chassis ride heights, so `Cylinder` is a safe change to make to an already-tuned car.

The one caveat is that suspension raycasts hit *any* shape in their query filter, dynamic obstacles
included, so a low object under a wheel can be nudged by the tire model even with wheel collision
switched off entirely. That is the road query doing its job, not a leak from the collision shape.

### Determinism

Convex core narrowphase is a different code path from the cooked hull it replaces, so it was
soaked before wheels were allowed to depend on it. `TestConvexCoreOnMeshIsReproducibleUnderRollback`
runs cylinders resting and rolling on an uneven triangle mesh — mesh contact is resolved per
triangle, a rolling cylinder re-derives its contact set constantly, and a cylinder's contact patch
is a line rather than a point or a facet. Over 600 rolled-back ticks two peers agree tick for tick,
and a run that rewinds every eight ticks and resimulates the window lands on the state a run that
never rewound reached, bit for bit, for cylinders and for boxes alike — the box is carried
alongside so that a shortfall could be read as the solver rather than the geometry.
`TestVehicleCylinderWheelsAreDeterministic` then drives two vehicles on simulating, filtered
cylinder wheels for 200 rollback ticks and requires them to stay bit-identical.

Both pass under PGS and TGS, and the vehicle test under both drive models. As everywhere else in
the suite, bit-exact replay is asserted under PGS, the solver the framework requires, and
characterised rather than asserted under TGS — where it currently comes out bit-exact as well.

## Setup rules (same as any actor)

Both apply the two lifecycle rules from [World and actors](WorldAndActors.md): create the actor
in the world's scene (`SimActorBridge.CreateWorldScene(world)`, `externalSceneMembership = true`),
and do not let the Unity component add itself to the scene through its own `OnEnable` — the world
adds it in stable-ID order at commit.
