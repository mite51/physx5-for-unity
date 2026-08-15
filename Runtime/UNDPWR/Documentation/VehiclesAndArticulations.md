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

## Setup rules (same as any actor)

Both apply the two lifecycle rules from [World and actors](WorldAndActors.md): create the actor
in the world's scene (`SimActorBridge.CreateWorldScene(world)`, `externalSceneMembership = true`),
and do not let the Unity component add itself to the scene through its own `OnEnable` — the world
adds it in stable-ID order at commit.
