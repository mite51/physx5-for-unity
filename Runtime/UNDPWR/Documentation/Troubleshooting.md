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
- transport peer ID does not equal the client player ID;
- GPU backend selected for an authoritative session.

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
