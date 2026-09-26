# TD-1 plugin handoff — 2026-09-26

## Delivery and scope

Latest follow-up: a native pair of 24-link SMPL humanoids reproduced another desync with
the first-fix runtime, at tick 296. The new SDK fix excludes empty contact pairs from
enhanced-determinism constraint partitioning in TGS/PGS and preserves shape-based ordering
for PGS articulation contacts. Both self-collision and inter-humanoid collision remain enabled.
All 23 native tests, 24 humanoid matrix cases and 40 simpler matrix cases pass (3,000 ticks
per matrix case), as do all 54 managed tests against both the staged and installed runtime.
The application policy/network scene has not been rerun with this replacement.

**Installation complete:** after Unity closed, `Plugins/Windows/x86_64/PhysX_64.dll` was
replaced and all six installed DLL checksums verified. The tested DLLs and `SHA256.json` remain in
`C:/Git/physx5-native-plugin/build-td1/humanoid-fix`. Only `PhysX_64.dll` changes; its new
SHA-256 is `67EFB1467B284D6A3550AC5724DB67DDD8BF666A0906B2C23ACA9807D2658552`.
`TD1_BINARY_MANIFEST.json` describes this installed humanoid-fix runtime. All 54 managed
tests passed against the installed binaries (`Tests~/Managed/bin/humanoid-installed-managed.log`). See
`C:/Git/physx5-native-plugin/tests/HUMANOID_INTERACTION_REPRO.md` for reproduction and results.

Previous delivery: the TGS contact-order rollback fix is implemented, tested and installed in
`Plugins/Windows/x86_64/PhysX_64.dll`. See
`C:/Git/physx5-native-plugin/tests/ARTICULATION_INTERACTION_REPRO.md` for that earlier delivery.
The latest runtime includes both fixes.
Upgrade all peers together and restart deterministic sessions because contact solve order changes.

P0–P3 are implemented for **Windows x86-64 CPU simulation**, tested with Unity 6000.5.6f1,
.NET 9, MSVC and the local patched PhysX 5.6.1. This is an uncommitted working-tree delivery.
Base revisions (apply the accompanying working-tree changes, not these bases alone):

- Managed: `2f6c1f464197676463d3ffb3f5d87268bf9c5146` at `C:/Git/physx5-for-unity`.
- Native: `1ea79af2ff0921a96095e23bcd3edbff1b53164b` at `C:/Git/physx5-native-plugin`.
- PhysX: `e3c7dee4b6d3b161ad1a480f43bc36a6bbf20069` at `C:/Git/PhysX`.

The PhysX changes are required: `physx/include/PxRigidDynamic.h` and
`physx/source/physx/src/NpRigidDynamic.cpp` add CPU kinematic rollback access. The two exported
functions do not change object layout or virtual tables. Follow-up changes in
`physx/source/lowleveldynamics/src/DyTGSDynamics.cpp` and `DyDynamics.cpp` stabilize
enhanced-determinism contact ordering and partitioning across speculative histories.
The latest humanoid fix is installed, as noted above. Ship the matching SDK DLLs with the
wrapper. [TD1_BINARY_MANIFEST.json](TD1_BINARY_MANIFEST.json) records SHA-256 identities of
the delivered wrapper and required dependencies in `Plugins/Windows/x86_64`.

Q1 complete queries, Q2 link/shape identity, Q3 query filters/standalone discovery, and Q4 query
integration/performance are **deferred**. Selective link-pair exclusions, per-world tables,
more than 32 groups and runtime refiltering are also deferred. No Protomotion scenes, policy
code, model, or application rollback state was edited. The sample project's Pipeline package
was installed with user authorization solely to run Editor validation.

GPU and Linux are not validated. The existing GPU build configuration failed to link against
the locally configured CPU PhysX libraries; the delivered wrapper uses the supported CPU build.
GPU particle/soft-body features are outside this delivery. A pre-existing PhysXGpu DLL may remain
in the package directory, but it is not a dependency of this CPU wrapper.

## Validation

- Native: `PxwReadbackTests`, `PxwArticulationContactTests`, `PxwUndpwrTests` pass.
  Coverage includes offset COM/shape readback, static/dynamic/kinematic/link APIs, immediate
  restore readback, pending kinematic targets, moving/settling/sleeping/disabled states,
  repeated restore, fresh-world reconstruction and replay (including a moving kinematic
  pusher contacting a dynamic body with cold contact caches), X/Y/Z native capsule dimensions
  and endpoints, group filtering/contact reporting, independent self-collision flags, and
  existing construction-hash/determinism regressions.
- .NET: **54 tests pass**, including `DeliveredPluginTests`, which loads the deployed DLL by
  absolute path, verifies exports/layouts, reads all four actor types, exercises restore and
  COM conversion, and verifies that disposing another managed world preserves group filtering.
- Unity: **490 checks pass** across 48 actor/direction/legacy/sharing cases plus simultaneous
  shared shapes. Covers actual component attachment paths, rotated parents, articulation child
  offsets, non-unit parent scale, native local-pose readback, reconstruction without accumulated
  rotation, cache identity and release, invalid dimensions, and rejected live authoring changes.

Reproduce from the native repository:

```powershell
cmake -S . -B build-td1 -DBUILD_TESTS=ON -DBUILD_PHYSX_FIRST=ON -DUSE_GPU=OFF -DDEPLOY_TO_UNITY=ON
cmake --build build-td1 --config Release --target PhysXUnity PxwReadbackTests PxwArticulationContactTests PxwUndpwrTests --parallel 6
ctest --test-dir build-td1 -C Release -R 'Pxw(Readback|ArticulationContact|Undpwr)Tests' --output-on-failure
```

Run managed tests from the managed repository in a fresh process:

```powershell
$env:PHYSX_NATIVE_DIR = 'C:/Git/physx5-for-unity/Plugins/Windows/x86_64'
$env:PATH = $env:PHYSX_NATIVE_DIR + ';' + $env:PATH
& './Tests~/Managed/run-tests.ps1'
```

In the sample Editor, use **Tools / PhysX / Run TD1 Contract Checks**, or:

```powershell
unity command eval 'return PhysX5ForUnity.Tests.Td1ContractChecks.Run();' --timeout 30000 --project-path C:/Git/UNDPWR2_sample
```

## Live readback contract

Use native handles only while their owner is alive, between completed simulation/fetch calls.
After a successful native/managed restore these reads are immediately current; no FixedUpdate,
Unity Transform synchronization or extra simulation step is needed. Re-resolve every handle
after reconstruction. Do not persist addresses as identity. Registry IDs belong to the caller;
an articulation's registry handle is the articulation, whereas body readback needs a **link**.

| Managed API | Native export / pointer | Meaning |
| --- | --- | --- |
| `SimBody.GetPose/GetPosition/GetRotation` | `PxwBodyGetPose(PxActor*)` | World actor/link origin and orientation; works for static, dynamic, kinematic, link |
| `Physx.GetRigidActorPose` | `GetRigidActorPose(PxRigidActor*)` | Same physical actor frame, using legacy position-first transform layout |
| `Physx.GetArticulationLinkGlobalPose` | `GetArticulationLinkGlobalPose(PxArticulationLink*)` | World link frame |
| `Physx.GetShapeLocalPose` | `GetShapeLocalPose(PxShape*)` | Shape pose relative to actor/link, including capsule correction |
| `SimBody.GetLinearVelocity` | `PxwBodyGetLinearVelocity(PxActor*)` | World COM linear velocity, metres/second; static returns zero |
| `SimBody.GetAngularVelocity` | `PxwBodyGetAngularVelocity(PxActor*)` | World angular velocity, radians/second; static returns zero |
| `SimBody.GetMass` | `PxwBodyGetMass(PxActor*)` | Kilograms through `is<PxRigidBody>()`; static returns zero sentinel, not physical zero mass |
| `SimMass.Read(handle)` | `PxwGetMassProperties(PxRigidBody*)` | Mass, principal inertia and COM-local position **and rotation**; dynamic, kinematic or link only |
| `Physx.GetArticulationLinkLinearVelocity/AngularVelocity` | Corresponding `PxArticulationLink*` exports | Direct link velocity alternatives |
| `Physx.GetShapeSimulationFilterData` | `PxShape*` and four output uints | Simulation group in word0 |
| `PhysxCollisionGroups.SetPairCollides/GetPairCollides/ResetToDefault` | Group-table exports | Process-global symmetric 32-group table |

All quaternions use xyzw components. `SimTransform` is quaternion-first (28 bytes, position at
offset 16); `PxTransformData` is position-first (28 bytes, quaternion at offset 12). The legacy
COM setter retains its native position-first ABI; its internal managed binding now converts
correctly. Do not substitute one transform layout for the other. Do not pass a static actor,
articulation root/container, world or vehicle wrapper to `SimMass.Read`; a vehicle needs its chassis.
Use `SimBody.GetMass` for links instead of the legacy `GetMass(PxRigidDynamic*)` export.

`PhysxDynamicRigidActor.linearVelocity/angularVelocity` and Unity transforms are presentation
caches, not the restore readback contract. Body position is not COM position. At world point P:

`v(P) = vCOM + cross(omegaWorld, P - (actorPosition + actorRotation * comLocalPosition))`.

Kinematic target assignment does not immediately change velocity. PhysX computes COM velocity
from target displacement/rotation during simulation; it becomes current after fetch. Restore
now restores that last completed velocity without advancing time. Pending targets and the
kinematic settling/sleep phase also restore. CPU snapshot **version 4** is required: discard
old saved snapshots and restart deterministic sessions after upgrading all peers. Snapshots
remain construction-dependent and are not portable between different SDK/plugin builds. GPU
kinematic capture is explicitly unsupported (native capture returns failure, not a valid blob).

## Capsule authoring and migration

Native capsule geometry always runs along local +X. Radius is in metres; `Height` is **cylinder
length**; native half-height is `Height / 2`; full length is `Height + 2 * Radius`. Radius must
be finite and positive; Height finite and nonnegative. Height zero is supported.

`UseAuthoredDirection=true` maps +X to the selected X/Y/Z axis in the shape-local rotation.
The final pose is `child/local pose * direction rotation`; actor/link state contains no policy
conversion. Articulation attachment does not overwrite this pose. Shared-shape cache identity
includes dimensions, material, group and full local pose. Shared native shapes with differing
offsets or directions are distinct. Standalone rigid components attach their same-object shape;
articulations also gather enabled descendant shapes, stopping at other actors.

Legacy assets have no serialized `m_useAuthoredDirection`; its default is false, so they retain
their original physical X-axis behavior even if Direction says Y/Z. Newly added Editor components
opt in through Reset. Runtime builders should explicitly set `UseAuthoredDirection = true` on an
inactive object before native creation. Untouched X-axis assets retain the same physical frame.

Migration is explicit: inspect a legacy asset, remove any compensating Transform rotation as
appropriate, enable Use Authored Direction, then rebuild and review its physical pose. No automatic
bulk migration runs, because a stored Y/Z field cannot reveal whether its Transform was compensated.
Gizmos now show the selected physical convention, including legacy local X. The inspector labels
cylinder height and explains migration.

Primitive dimensions ignore Transform scale. Direction-aware capsule child positions use their
world position expressed in the unscaled actor frame; hierarchy scale can move that center, but
does not enlarge the radius/length. Legacy child offsets preserve the previous InverseTransformPoint
interpretation. Prefer unit-scale legacy hierarchies; migrate/review scaled ones explicitly.

Capsule setters reject changes after geometry creation. Runtime reparenting, changes to child
poses/dimensions/direction or serialized-field mutation do not update existing attachments.
Rebuild the **owning actor, shape and geometry** from descriptors (actor released first), then
refresh registry handles and descriptor caches. Shape/geometry `Recreate()` alone does not replace
the old shape retained by an attached actor. Box Size is full edge length, sphere Radius is radius;
their existing setters only change authoring fields, so cache creation-time dimensions and rebuild
the whole owner when changing them. The native constructed geometry is authoritative.

## Activation and filtering

Default component/native-created shapes have simulation and scene-query flags enabled and are
not triggers. Component creation does not offer a trigger toggle. Shape.enabled affects
articulation collection during construction; disabling an already attached shape component does
not detach its native shape. Static/dynamic/kinematic actor disabling removes scene membership
under standalone ownership; externalSceneMembership delegates membership to the caller. A
registered-source adapter must track actual attachment, scene membership, enabled/parked entity
state, and any explicitly authored native flags/trigger state. Do not infer these solely from
Unity visibility or transforms. This delivery does not add arbitrary native shape discovery.

Assign `CollisionGroup` before shape creation. Changes after creation are rejected; low-level
native callers can set simulation filter word0 before attachment. Group indices are 0–31, not
bitmasks. Configure the process-global symmetric table before constructing worlds and before
construction-hash exchange. All worlds in a process share it. Only a caller-owned process/session
coordinator may reset it after **every** world has shut down. Individual world disposal no longer
resets it. Runtime table edits/refiltering are outside the supported deterministic lifecycle.

`disableSelfCollision` independently suppresses articulation self collision. Group suppression
affects every owner using those groups, not a particular character or link pair. Selective
same-owner/link-pair exclusions are not provided. The standalone/default shader and the event
shader obey the same solid-contact group table. PhysX's trigger path bypasses the solid group
table; do not use groups as a trigger exclusion contract. Contact flags retain their existing
OR accumulation until Clear; sleeping articulations need not produce resting-contact reports.

## Queries and minimal registry example

Existing SimQuery is available only through DeterministicWorld. Masks are `1u << SimHandleKind`:
dynamic 1, static 2, kinematic 4, vehicle 16; zero selects all registered kinds. Articulation
entries/unregistered actors are omitted. Hits lack link/shape identity. Native collection takes
at most 256 touches before filtering/sorting, then truncates to caller capacity without overflow
status. Membership is not guaranteed complete/deterministic under overflow. Native vectors may
allocate. Query eligibility is separate from simulation groups. Use an explicit registry for TD-1.

At a committed tick, for a registered dynamic/kinematic body or articulation **link** and shape:

```csharp
using PhysX5ForUnity;
using UNDPWR.Core;
using UNDPWR.Gameplay;
using UnityEngine;

// actor is a live PxRigidDynamic* or PxArticulationLink*, shape is its live PxShape*.
var body = SimBody.GetPose(actor);
Physx.GetShapeLocalPose(shape, out var local);
var mass = SimMass.Read(actor);
Vector3 center = body.Position + body.Rotation * local.position;
Quaternion frame = body.Rotation * local.quaternion;
Vector3 com = body.Position + body.Rotation * mass.CenterOfMassLocalPose.Position;
Vector3 centerVelocity = SimBody.GetLinearVelocity(actor)
    + Vector3.Cross(SimBody.GetAngularVelocity(actor), center - com);
Vector3 capsuleAxis = frame * Vector3.right;
// Native endpoints: center +/- capsuleAxis * (createdCylinderHeight / 2).
// Endcap tips add createdRadius. Statics use zero velocity and skip SimMass.Read.
```

Application integration must refresh its source registry after reconstruction, migrate capsules
deliberately, move group reset ownership to the session coordinator, upgrade snapshot sessions
together, and perform any policy-specific local-Z conversion on the application side.
