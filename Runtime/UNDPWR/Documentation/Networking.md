# Networking

The framework supplies the netcode; you supply the socket. This chapter covers the transport
you implement, the session that drives it, how desyncs are detected, and how a peer joins or
recovers mid-match.

## The transport seam

The one thing the framework needs from a network is to move opaque byte messages between peers.
Implement [`ISimTransport`](../Net/ISimTransport.cs):

```csharp
public interface ISimTransport
{
    void Broadcast(byte[] data, int offset, int length);  // reaches every OTHER peer
    bool TryReceive(out ArraySegment<byte> message);       // next received message, or false
}
```

The contract is intentionally thin:

- **Messages are whole.** A message sent as one `Broadcast` is received as one `TryReceive`,
  never split or merged. UDP gives this per datagram; a stream transport must frame.
- **Delivery is best-effort.** Messages may be lost, duplicated or reordered. The framework
  tolerates all three — inputs are idempotent and re-sent in a small redundancy window, hashes
  are per-tick — so the transport does not need to be reliable.
- **`Broadcast` reaches every *other* peer**, not the sender.
- The received `ArraySegment` is only valid until the next `TryReceive`; copy anything that
  must outlive it.

For local testing, [`SimLoopbackNetwork`](../Net/SimLoopbackTransport.cs) is an in-process
transport with configurable latency, loss and reordering.

## The session

[`SimSession`](../Net/SimSession.cs) ties a `RollbackEngine` to an `ISimTransport`: it sends
this peer's inputs, feeds arriving inputs into the engine, agrees the session at join, and
exchanges confirmed-tick hashes.

```csharp
var session = new SimSession(engine, transport, config, localPlayerId, playerIds);
session.Start(); // announce: broadcast the config hash and player set
```

Two invariants it depends on: every peer constructs its engine with the **same player-ID set**
(so slot order agrees), and every peer computes the **same `SimConfig.ComputeHash()`**.

The per-`FixedUpdate` loop:

```csharp
session.Pump();                          // remote inputs -> engine, peer hashes -> detector, handshakes
session.SubmitLocalInput(SampleInput()); // stamp for engine.LocalInputTick and fill the run
engine.Advance();                        // one simulated tick
session.PublishConfirmed();              // publish and check the confirmed-tick hash
```

`SubmitLocalInput` fills the tick run and re-sends a few recent inputs each frame, so a lost
datagram is recovered by the next one. See
[Rollback and input](RollbackAndInput.md#submitting-input-without-stalling) for why the run
matters.

## The handshake

`Start()` broadcasts this peer's config hash and player set. When a peer's handshake arrives,
the session compares both: a config-hash mismatch (a different solver, tick rate, gravity, …)
or a player-set mismatch is **refused at join** rather than discovered as a desync mid-match.
Subscribe to `session.HandshakeReceived` to observe accepted and rejected peers:

```csharp
session.HandshakeReceived += result =>
{
    if (!result.Accepted)
        Debug.LogError($"Peer {result.PeerId} rejected: {result.Reason}");
};
```

## Desync detection

Because the engine rewinds a data-dependent depth and runs a data-dependent-length window,
there is no fixed identical-sequence property to fall back on — only PGS transparency, which
has to be *verified* rather than assumed. So confirmed-hash detection is **mandatory and
fatal**: `SimSession` sets `SimDesyncDetector.Fatal = true`.

`PublishConfirmed` publishes this peer's combined hash for each newly confirmed tick (all three
channels — physics, entity, game) and checks it against peer hashes already received. On a
disagreement, [`SimDesyncDetector`](../Net/SimDesyncDetector.cs) raises, and the report names
the channel that diverged.

To attribute a *physics* desync to a specific body, set
`SimConfig.PerEntityHashDiagnostics = true` on every peer. Each peer then logs its own
per-entity hash table for the disagreeing tick and sends it to the others; the entry whose hash
differs between two peers' logs is the diverged body. It costs a native walk over every entry
each confirmed tick, so leave it off until you need it. See
[Troubleshooting](Troubleshooting.md).

There is also a registration-order check: once the first confirmed step assigns PhysX its actor
indices, each peer sends its registration table, and a build-order mismatch is logged as a
named line rather than surfacing later as a gradual physics desync.

## Wire messages

The framework only ever sends inputs and control messages — never simulation state.
[`SimMessageKind`](../Net/SimWire.cs) enumerates them: `Input`, `Handshake`, `Hash`,
`InternalIds` (registration tables), `EntityHashes` (per-entity diagnostics), and `Rebuild`
(below). Framing uses `SimByteWriter`/`SimByteReader` (little-endian); inputs use
`SimInputCodec`.

## Mid-match join and resync

A joiner has no history and cannot manufacture one, so join is **not** a catch-up. Instead every
peer restores one agreed snapshot at one agreed tick and continues from there — a brief hitch
for everyone, after which all peers are back on an identical footing. The same procedure is the
recovery path after a desync.

The local mechanics are in place:

- [`SimRebuildState`](../Rollback/SimRebuildState.cs) bundles a confirmed tick, the resuming
  roster and all three snapshot channels.
- [`RollbackEngine.CaptureRebuildState`](../Rollback/RollbackEngine.cs) exports it (buffers
  copied out of the ring), and `PrepareForRebuild(ref state, reconcile, recreateWorld)` restores
  it on any peer — including a joiner that never simulated the ticks. By default it recreates the
  native world (`DeterministicWorld.RecreateNativeWorld`), re-registering every actor in
  stable-ID order so the joiner reaches the identical internal PhysX arrangement as the
  incumbents. An optional `reconcile` callback runs after restore but before the resume capture,
  so a roster change (spawning a joiner's avatar, retiring a leaver's) is baked into the agreed
  snapshot rather than replayed.
- [`SimRebuildCodec`](../Net/SimRebuildCodec.cs) serialises the payload over any reliable
  transport (tagged `SimMessageKind.Rebuild`).
- After applying a rebuild, call `session.ReplaceRoster(newPlayerIds)` (so the handshake reflects
  the new player set) and `session.NotifyRebuilt(resumeTick)` (so the session republishes from
  the resume tick and re-exchanges the registration table).

```csharp
// On a peer that holds history, produce the agreed payload for a joiner:
if (engine.TryProduceRebuildState(newRoster, reconcile, out SimRebuildState state))
{
    byte[] bytes = SimRebuildCodec.Encode(ref state);
    reliableTransport.SendTo(joiner, bytes);
}

// On any peer, apply an agreed payload:
SimRebuildState incoming = SimRebuildCodec.Decode(bytes, 0, bytes.Length);
engine.PrepareForRebuild(ref incoming, reconcile);
session.ReplaceRoster(newRoster);
session.NotifyRebuilt(incoming.ResumeTick);
```

> The out-of-band **negotiation** of *which* snapshot and resume tick the peers agree on is left
> to your session logic — the rebuild payload is out-of-band and not part of the interop
> contract. The framework provides the capture, transport codec and restore; you decide when a
> rebuild happens and who is authoritative for the agreed snapshot, and you carry the payload
> over a reliable channel.

## Interop versioning

Two peers interoperate only when they agree on the managed config hash
(`SimConfig.ComputeHash`) **and** the native snapshot format (`kStateVersion`). These are
tracked in [CHANGELOG.md](../CHANGELOG.md), not the package version. Check it before shipping an
update that peers with an older build.
