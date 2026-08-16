# UNDPWR

Unity Networked Deterministic Physics With Rollback is authoritative rollback netcode for
PhysX 5. A headless server runs the canonical deterministic timeline; clients predict from
timestamped future input proposals and reconcile against server-finalized command frames.

## Core behavior

- Server-owned input and deterministic-event scheduling.
- Late commands retimed forward; finalized history is never rewritten.
- Predicted, local-speculative and server-authoritative input provenance.
- RTT/jitter-driven adaptive input lead with immediate presentation anticipation.
- Conditional full-world rollback from the earliest changed tick.
- At most 8 complete simulation steps per Unity frame by default.
- Automatic reliable server-snapshot rebuild after excessive lag or hash mismatch.
- Construction/config admission hashes and per-tick three-channel state hashes.

## Minimal client frame

```csharp
var simulation = SimConfig.Deterministic;
var network = SimNetConfig.Authoritative;
var engine = new RollbackEngine(world, playerIds, network);

var client = new SimClientSession(
    engine, transport, simulation, network,
    localPlayerId, playerIds, host.Actions);
client.Start(nowMicroseconds);

// FixedUpdate
client.Pump(nowMicroseconds);
client.SubmitLocalInput(SampleInput(), nowMicroseconds);
client.Advance();
```

The server uses `SimServerSession` over transport peer ID `0` and calls `Pump` then `Advance`.

`ISimTransport` provides directed authenticated sends plus `Unreliable` and
`ReliableOrdered` delivery. GPU simulation remains available for non-networked worlds;
authoritative sessions require CPU and the fixed PGS solver.

Read the [manual](Documentation/README.md), beginning with
[Getting started](Documentation/GettingStarted.md).
