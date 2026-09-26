# Tests~

Test projects that live outside Unity's asset pipeline. Unity ignores any folder whose name
ends in `~`, so nothing here is imported, compiled by the editor, or given a `.meta` file.

## Managed/

Runs the UNDPWR EditMode tests on the .NET SDK, with no editor process.

```powershell
.\Managed\run-tests.ps1
```

Everything under `Runtime/UNDPWR` is ordinary managed C#. The only Unity surface it touches
is `UnityEngine.CoreModule`, and only for `Vector3`, `Quaternion`, `Mathf` and `[Tooltip]`,
so the whole package compiles and runs against a Unity installation's managed assemblies
without Unity itself. `run-tests.ps1` finds the newest installation under the Unity Hub root;
override it with `-UnityManagedDir` or `UNITY_HUB_EDITOR_DIR` if that guess is wrong.

Requires the .NET SDK (9.0 or newer) and, on first run, NuGet access for NUnit.

### What it does and does not cover

It compiles the *entire* package rather than a hand-picked subset, so a file that quietly
grows an editor dependency shows up as a build failure here. It then runs every test that
does not need the Unity runtime.

It cannot run tests that call into Unity native code — `Quaternion.Euler` and friends throw
`ECall methods must be packaged into a system module` outside the editor. Those are marked
`[Category("RequiresUnityRuntime")]` and excluded by `undpwr.runsettings`. If you add a test
that needs the editor, mark it the same way rather than letting this suite go red, and run it
from Unity's Test Runner.

The default run covers pure-managed contracts: configuration validation and hashing, the input
buffer, state channels, action queue, phase machine serialization, and input encoding.
Set `PHYSX_NATIVE_DIR` to the absolute delivered plugin directory and put that directory on PATH
to enable `DeliveredPluginTests`. It verifies exports/ABI and readback/restore for all four actor
types against the actual shipped DLL, including kinematic velocity rollback and offset COM.
Run it in a fresh test process, with matching PhysX dependencies. Broader determinism tests live
in `physx5-native-plugin/tests`. The component attachment fixture is available in Unity via
Tools / PhysX / Run TD1 Contract Checks; see `TD1_HANDOFF.md` for reproduction commands.
