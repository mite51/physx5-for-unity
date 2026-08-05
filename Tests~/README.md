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

It also cannot cover anything that needs the native PhysX plugin, which is most of what the
framework actually does. Rollback, snapshots, hashing of real world state and the determinism
measurements all live in the native suite in `physx5-native-plugin/tests`. This harness covers
the pure-managed contracts underneath them: configuration validation and hashing, the input
buffer, the state channels, action queue and phase machine serialization, and input encoding.
