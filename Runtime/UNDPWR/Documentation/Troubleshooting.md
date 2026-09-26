# Troubleshooting

Start with `SimClientSession.State`, `client.Stats`, and the rollback-engine counters.

## Admission rejected

The server checks protocol version, authenticated player ID, roster, simulation hash, network
policy hash and construction hash. A rejection means the two builds are not the same session;
there is no compatibility fallback.

Common causes:

- different prefabs, shapes, materials or mass setup;
- different action registration order;
- different `SimConfig` or `SimNetConfig`;
- a different collision group table (see below);
- transport peer ID does not equal the client player ID;
- GPU backend selected for an authoritative session.

### Collision group table mismatches

The construction hash includes the pairs of collision groups that have been told not to
collide, because that table decides whether two shapes touch at all and peers that disagree
about it simulate differently while every actor, shape and geometry still hashes the same.

The table is process-global rather than per-world: it lives in PhysX's extensions library, so
it is shared by every world in the process and it survives leaving Play mode in the editor.
Two things follow. Configure it from authored data during world construction, never from
gameplay code that might run at a different time on a different machine. And call
`PhysxCollisionGroups.ResetToDefault()` only from the process/session owner after **all** worlds
have shut down. Individual world disposal preserves filtering for the worlds still running.

Only disabled pairs contribute to the hash, so a session that never touches groups hashes as
though the feature did not exist.

## Frequent retiming or rollback

Inspect:

```csharp
client.Stats.SmoothedRttMilliseconds;
client.Stats.JitterMilliseconds;
client.Stats.InputLeadTicks;
client.Stats.RetimedInputs;
engine.LastReplayLength;
```

The adaptive controller raises lead after late samples and lowers it only after a stable period.
If it remains at `MaximumInputLead`, the configured ceiling is below the connection's relay
latency tail or the client/server fixed clocks are not advancing at the configured tick rate.

Do not set lead to zero. Use `InputAnticipated` for immediate camera, animation and cosmetic
response.

## Catch-up never finishes

`engine.IsCatchingUp` means coherent replay is continuing under
`MaxSimulationStepsPerFrame`. Check:

- `CatchUpBacklog`;
- `BudgetExhausted`;
- `LastSimulationSteps`;
- Unity profiler cost per deterministic tick.

Raise the count budget only after measuring one tick's cost. If backlog reaches
`HardResyncTicks`, the client automatically requests a server rebuild instead of entering a
rollback spiral.

## Client is resyncing

`Resyncing` is expected after:

- required history was overwritten;
- confirmation exceeded the hard backlog threshold;
- a server hash differed;
- a late authoritative correction was older than retained history.

The rebuild uses reliable ordered delivery and always recreates the native world. A client stuck
in `Resyncing` usually has a transport that does not actually provide reliable ordered delivery
or is dropping messages addressed from server peer ID `0`.

## Server hash mismatch

The server is authoritative; clients do not blend state. A mismatch requests a rebuild.

Use the reported channel:

- Physics: construction, stable IDs, forces outside simulation callbacks, architecture mismatch.
- Entity: broken `CaptureState`/`RestoreState` inverse.
- Game: broken game-mode capture/restore or action registration mismatch.

Enable `PerEntityHashDiagnostics` while diagnosing a physics mismatch.

## A fork on a near-tie contact, with every other channel agreeing

A physics-channel fork on a single tick, entity and game channels still in agreement, on a contact
tight enough to be near a tie — several bodies pressing one body from opposite sides.

That pattern means the peers hold the same bodies in a different internal *arrangement*, which is
invisible to both hashes: construction covers the blueprint and state covers poses and velocities,
and those all match. A scene's arrangement follows the order things were added to it, so a world
left as setup built it and a world that has been recreated — re-added in stable-ID order — are
arranged differently despite holding the same contents.

The trap is that the server hands a rebuild to every client it admits and applying a rebuild
recreates, so clients are on the recreated arrangement whether they joined early or late. The host
must be too. `SimGameHost.Begin` does this for you; if you drive `RollbackEngine` or
`DeterministicWorld` yourself, call `DeterministicWorld.RecreateNativeWorld` once after the world
is fully built and before the first capture.

Symptoms of this are load-dependent, not deterministic in the reporting sense: an asymmetric pair
agrees indefinitely until a contact happens to land near a tie, so "it only desyncs on
`<some specific event>`" is expected, and a path that has never desynced is not evidence that it
is symmetric.

Because construction is unchanged by any of this, the join handshake cannot detect a peer running
an older build; it will be admitted and then desync. Update every peer together.

## A vehicle rides high or steers badly

Every peer agrees, so this is not a desync — it is the road being resolved twice. A wheel with
`isSimulationShape` enabled and no collision group excluding the drivable surface is solved as a
contact *and* by the suspension raycast and tire model. Give wheels their own group and disable it
against the road's group, and prefer `Cylinder` over `Cooked Prism` so contacts do not chatter as
the wheel spins. See
[Vehicles and articulations](VehiclesAndArticulations.md#wheel-collision-shapes).

A low dynamic object nudged by a wheel that is *not* a simulation shape is a different thing and
not a bug: suspension raycasts hit any shape in their query filter, so the tire model can push
something under the wheel without any contact being generated.

## Deterministic rules

- Apply forces, steering, spawns and gameplay changes only in simulation callbacks.
- Never read `Time.deltaTime`, `UnityEngine.Random`, render transforms or asynchronous results
  into deterministic state.
- Register and commit bodies by stable ID.
- Capture and restore exactly the same managed fields in exactly the same order.
- Branch contacts on body identity, not exact impulse or contact point.
- Keep preview/anticipation presentation strictly one-way.

## Logs

`SimLog` tags messages with tick and peer name. `SimLog.AttachNativeSink()` routes native PhysX
diagnostics through the same path. Define `UNDPWR_VERBOSE_LOGGING` only while profiling detailed
replay behavior.
