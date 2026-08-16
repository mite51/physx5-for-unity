# Networking

UNDPWR has one network model: a headless authoritative server runs the deterministic simulation,
schedules commands and events, publishes canonical frames and hashes, and owns rebuild state.

## Transport contract

Implement `ISimTransport` over authenticated connections:

```csharp
public interface ISimTransport
{
    uint LocalPeerId { get; }
    void Send(uint recipientId, byte[] data, int offset, int length, SimDelivery delivery);
    bool TryReceive(out SimTransportMessage message);
}
```

`SimTransportMessage.SenderId` must come from the authenticated connection, not packet bytes.
The server uses it to prevent a client from submitting another player's commands.

Two delivery paths are required:

- `Unreliable`: input proposals, canonical frames, clock samples and hashes. Canonical frames
  include their assigned events; input and frame redundancy recover ordinary loss.
- `ReliableOrdered`: handshake, proposal decisions, event decisions, rebuild requests and
  rebuild snapshots.

Messages are whole and framed. `SimLoopbackNetwork` supplies a deterministic in-process
implementation with latency, loss and reorder simulation.

## Roles

Transport peer ID `0` is reserved for `SimServerSession`. Every client transport ID equals its
nonzero player ID.

Both sides construct the same simulation, network policy, player roster, action registration,
and `RollbackEngine`:

```csharp
var simulation = SimConfig.Deterministic;
var network = SimNetConfig.Authoritative;
var engine = new RollbackEngine(world, playerIds, network);
```

Server:

```csharp
var server = new SimServerSession(
    engine, transport, simulation, network, playerIds, host.Actions);

// FixedUpdate
server.Pump(nowMicroseconds);
server.Advance(); // finalize canonical frame, simulate, publish frame and hash
```

Client:

```csharp
var client = new SimClientSession(
    engine, transport, simulation, network,
    localPlayerId, playerIds, host.Actions);
client.Start(nowMicroseconds);

// FixedUpdate
client.Pump(nowMicroseconds);
client.SubmitLocalInput(SampleInput(), nowMicroseconds);
client.Advance();
```

## Admission

The reliable handshake checks:

- protocol version;
- authenticated player ID and roster;
- `SimConfig.ComputeHash()`;
- `SimNetConfig.ComputeHash()`;
- complete world-construction hash.

There is no old-protocol fallback. A mismatched build is rejected before joining the canonical
timeline.

## Command flow

Each input proposal carries a monotonically increasing sequence, requested server tick,
capture timestamp and fixed `SimInput`. The server:

1. authenticates the player;
2. deduplicates redundant proposals by sequence;
3. retimes late proposals to an unsimulated tick;
4. rejects proposals beyond `ServerMaxFutureTicks`;
5. finalizes one complete canonical frame per server tick.

If no new command is scheduled for a player, the preceding canonical command is held. The
server never edits a finalized tick.

## Clock and adaptive lead

Ping/pong messages estimate RTT and jitter. They never set simulation state or alter the fixed
tick clock. The client targets RTT plus a jitter margin so the server's canonical response
normally returns before the speculative tick is simulated.

Read `client.Stats` for RTT, jitter, lead, admission counts, mispredictions and rebuild count.

## Deterministic events

Register every networked `ISimAction` type in the same order through `SimActionQueue`. Submit an
event through `client.SubmitEvent(action, nowMicroseconds)`. Event proposals and decisions use
the reliable ordered path and are deduplicated by `(player, sequence)`.

The server schedules the action first and includes every event assigned to a tick in that tick's
canonical frame. This makes an input frame and its event set one confirmation unit even though
proposal decisions travel on a separate reliable channel. Clients retain events in the
rollback-external authoritative timeline. An event is decoded into the action queue immediately
before its assigned simulation tick, including during replay, so rollback cannot erase it.

## Hashes and rebuild

The server publishes physics, entity and game hashes for every confirmed tick. Clients compare
their confirmed snapshot against the server. A mismatch requests a rebuild rather than trying
to blend simulation state.

The rebuild path is automatic:

1. client enters `Resyncing` and sends `RebuildRequest`;
2. server captures its newest confirmed `SimRebuildState`;
3. snapshot travels reliably;
4. client calls the sole `PrepareForRebuild(ref state)` path;
5. the native world is always recreated before all state channels are restored;
6. held input baselines and all future authoritative events are restored;
7. stale proposal bookkeeping is cleared and the client returns to `Running`.

Observe `SimClientSession.StateChanged` to show reconnect UI. States are `Connecting`,
`Running`, `CatchingUp`, `Resyncing`, and `Disconnected`.

## Wire compatibility

Every message begins with `SimMessageKind` and `SimProtocol.Version`. Managed simulation config,
network policy and native snapshot format must all match. Protocol or hash mismatches are clean
join failures, not compatibility modes.
