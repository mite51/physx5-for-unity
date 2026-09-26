# PhysX 5 for Unity
*Built for PhysX SDK 5.6.1*

A Unity plugin for using PhysX 5 to simulate FEM soft bodies, PBD cloth and fluid, rigid bodies, articulations and vehicles.

This is a basic implementation with many features missing. Bounded scene queries are exposed through UNDPWR's `SimQuery`; general standalone collider discovery is not provided. Collision filtering is available through `PhysxCollisionGroups`.

The [TD-1 handoff](TD1_HANDOFF.md) documents verified live readback, capsule migration, filtering ownership, and CPU kinematic rollback. See [release notes](CHANGELOG.md) before upgrading existing assets or snapshot sessions.

## Geometries

Alongside the primitive and mesh geometries (`PhysxBoxGeometry`, `PhysxSphereGeometry`, `PhysxCapsuleGeometry`, `PhysxPlaneGeometry`, `PhysxTriangleMeshGeometry`), the `PhysX 5/Geometries` menu has three components built on PhysX 5.6's convex core geometry: `PhysxCylinderGeometry`, `PhysxConeGeometry` and `PhysxEllipsoidGeometry`. A convex core is a small analytic core swept by a margin and resolved by GJK, so it stays exact as it rotates instead of presenting the flat facets of a cooked hull. Cylinder and cone cores run along local +X, following the capsule convention; PhysX has no scale term for these, so the owning transform's scale is ignored and the dimensions are authored directly.

**A note on "margin", which means two opposite things.** On a standalone convex core geometry the margin is *additive*: the shape is the authored core *grown* by the margin, so a cylinder's true radius is `radius + margin` and its true length is `height + 2 * margin`. On a vehicle wheel (`PhysxVehicleWheelAttachment.cylinderMargin`) it is *size-preserving*: the wheel has an authored radius and width it must keep, so the core is *shrunk* by the margin and the outer size is unchanged. Both round the edges; only the standalone geometry changes overall size.

## Vehicles (PhysX Vehicle2)

Vehicles are authored with an Omniverse-style component model. Reusable parts are `ScriptableObject` assets created from the `Assets → Create → PhysX 5/Vehicle/*` menu:

- `PhysxVehicleWheel`, `PhysxVehicleTire`, `PhysxVehicleSuspension`
- `PhysxVehicleEngine`, `PhysxVehicleGearbox`, `PhysxVehicleAutobox`, `PhysxVehicleClutch`
- `PhysxVehicleTireFrictionTable`

A vehicle is a `PhysxVehicle` chassis component with one `PhysxVehicleWheelAttachment` per wheel (as child GameObjects) referencing those assets. Driving is done either through a `PhysxVehicleController` (throttle / brake / steer / gears, plus tank thrusts) or, for raw per-wheel control, by adding a `PhysxVehicleWheelController` to each wheel (this automatically switches the vehicle to direct wheel control). All three PhysX drive models are supported: **Direct** (Omniverse `DriveBasic`), **Engine** (Omniverse `DriveStandard`, with multi-wheel / four-wheel / tank differentials) and raw per-wheel control.

### Wheel collision shapes

PhysX's vehicle SDK gives each wheel a cooked 16-sided convex prism with no shape flags set, so by default a wheel collides with nothing and is invisible to scene queries — the suspension raycast and tire model are what resolve the wheel against the road. `PhysxVehicleWheelAttachment` makes that a per-wheel choice: `geometryMode` picks the cooked prism, a true cylinder sized from the wheel asset, or a geometry you supply; `isSceneQueryShape` and `isSimulationShape` set the shape flags; and `collisionGroup` sets the filter data.

`isSceneQueryShape` is safe on its own. `isSimulationShape` is not — a simulating wheel must be filtered off the drivable surface with `PhysxCollisionGroups`, or the road gets resolved twice and the car rides high and steers badly. Prefer `Cylinder` for a simulating wheel: the vehicle writes the spin angle into the shape's local pose every step, so a prism's facets rotate with the wheel and contact points snap between them, while a cylinder is rotationally invariant. None of this changes the handling model, because the road geometry query never looks at the wheel's collision shape. See [Vehicles and articulations](Runtime/UNDPWR/Documentation/VehiclesAndArticulations.md#wheel-collision-shapes) for the full setup.

See the **Vehicle** sample (`Samples/Vehicle`) for direct-drive and engine-drive demo scenes. Note that the legacy robot layer (`PhysxArticulationRobot` / `PhysxArticulationKinematicTree`) has been removed; use `PhysxArticulationBody` for articulations.

## Installation

Download and unzip the main package from the releases section, and place it in the appropriate directory. Then, download the corresponding libraries from the releases section.

Place the libraries for Linux (`\*.so`) and Windows (`\*.dll`) under `Plugins/Linux/x86_64` and `Plugins/Windows/x86_64`, respectively.

Install the package in a Unity project by going to "Window – Package Manager – `+` – Add package from disk…"

## Getting Started

Please refer to the sample scenes, which can be imported from the package after installation.

## Citation

This package is part of the [CRESSim](https://tbs-ualberta.github.io/CRESSim/) project, which was mainly focused on developing simulated surgical task environments. If you find this package useful for your research, please cite the following paper:

```bibtex
@inproceedings{ou2024realistic,
  title={A Realistic Surgical Simulator for Non-Rigid and Contact-Rich Manipulation in Surgeries with the da Vinci Research Kit}, 
  author={Ou, Yafei and Zargarzadeh, Sadra and Sedighi, Paniz and Tavakoli, Mahdi},
  booktitle={2024 21st International Conference on Ubiquitous Robots (UR)}, 
  year={2024},
  pages={64-70}
}
```
