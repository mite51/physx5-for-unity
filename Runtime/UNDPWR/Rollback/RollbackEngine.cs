using System;
using System.Collections.Generic;
using UNDPWR.Core;
using UNDPWR.Diagnostics;
using UNDPWR.Interop;
using UNDPWR.Net;

namespace UNDPWR.Rollback
{
    /// <summary>
    /// Applies a tick's inputs to the world before it is stepped.
    /// </summary>
    /// <remarks>
    /// Everything a game does to the simulation goes through here: forces, impulses,
    /// steering, spawning. The reason is that a rollback replays ticks, so any effect
    /// applied outside this callback happens once on the original pass and not at all on
    /// the replay, which desyncs the peer against itself.
    /// <para>
    /// Implementations must be pure with respect to the tick: given the same world state
    /// and the same inputs they must do the same thing. In particular they must not read
    /// wall-clock time, <c>UnityEngine.Random</c>, or frame-rate-dependent values.
    /// </para>
    /// </remarks>
    public interface ISimStepHandler
    {
        /// <summary>
        /// Called immediately before the world is stepped.
        /// </summary>
        /// <param name="world">The world about to be stepped.</param>
        /// <param name="tick">The tick being simulated.</param>
        /// <param name="inputs">Every player's input for this tick, in slot order.</param>
        /// <param name="isReplay">
        /// True when this tick is being resimulated after a rollback. Use it to suppress
        /// one-shot presentation effects such as sounds, never to change the simulation.
        /// </param>
        void OnBeforeStep(DeterministicWorld world, int tick, SimInputFrame inputs, bool isReplay);

        /// <summary>
        /// Called immediately after the world is stepped, before the snapshot is taken.
        /// </summary>
        void OnAfterStep(DeterministicWorld world, int tick, bool isReplay);
    }

    /// <summary>
    /// Drives the simulation forward, rolling back and resimulating when a prediction
    /// turns out to be wrong.
    /// </summary>
    /// <remarks>
    /// <b>What the engine rests on.</b> Clients compare only their confirmed timeline against
    /// the authoritative server, and advance it by a cold restore-and-step per tick. Under PGS that is
    /// bitwise transparent -- <c>restore(S); step()</c> is a pure function of <c>S</c> -- so
    /// however far each peer rewound or led, its confirmed hash for a tick is the same as
    /// everyone else's. That is why the clock can run free and the rewind depth can follow
    /// the network: what peers agree on does not depend on either.
    ///
    /// <para>What the measurements actually say, from the native suite, because the obvious
    /// explanation is the wrong one. A rewind is not lossy: two worlds driven along
    /// deliberately different histories agree bit-for-bit from the moment they are handed
    /// the same snapshot. Under PGS the cold-step discipline below makes replay bitwise
    /// transparent outright, and clients rewinding by four and by sixteen land on identical
    /// state. The framework fixes PGS for exactly this reason: TGS carries
    /// per-substep state that a restore does not reach and a data-dependent rewind under it
    /// diverges by a residual that is invisible once and fatal several hundred frames later.
    /// Documentation/DeterminismInvestigation.md section 8 records the measurements.</para>
    ///
    /// <para><b>What a tick looks like.</b> Every <see cref="Advance"/>:</para>
    /// <list type="number">
    /// <item><description>drain whatever the confirmed frontier reached into the confirmed
    /// timeline, one cold restore-and-step per tick, capturing each;</description></item>
    /// <item><description>advance the wall-clock target one tick, unless it
    /// would outrun what <see cref="SnapshotRing"/> can retain;</description></item>
    /// <item><description>resimulate the prediction window, but only from the earliest tick a
    /// misprediction or new confirmation disturbed, within the per-frame step budget.</description></item>
    /// </list>
    /// <para>The lead over the confirmed tick is emergent -- it grows when confirmations lag
    /// and shrinks as they arrive -- so a peer never predicts a shared, fixed number of ticks
    /// ahead. A peer that would lead further than the snapshot history allows stalls instead,
    /// a visible pause rather than a silent loss of the window a late input still needs.</para>
    ///
    /// <para><b>Mid-match join</b> is not handled by prediction at all. A joiner cannot
    /// reproduce a history it was not present for, so instead every peer rebuilds from
    /// one agreed snapshot at an agreed tick, which puts them all back on an identical
    /// history. See <see cref="PrepareForRebuild"/>.</para>
    /// </remarks>
    public sealed class RollbackEngine
    {
        private readonly DeterministicWorld _world;
        private readonly SimConfig _config;
        private readonly SimNetConfig _netConfig;
        private InputBuffer _inputs;
        private readonly SnapshotRing _snapshots;
        private readonly List<ISimStepHandler> _handlers = new List<ISimStepHandler>();
        private readonly List<ISimAuthoritativeEventHandler> _eventHandlers =
            new List<ISimAuthoritativeEventHandler>();
        private readonly SimAuthoritativeEventBuffer _events;
        private ISimStateProvider _stateProvider;

        private int _confirmedTick = -1;
        private int _currentTick = -1;
        private int _targetTick = -1;
        private bool _stalled;
        private string _hardResyncReason;

        // Per-entity hashes for recent confirmed ticks, parallel to the snapshot ring and
        // allocated only when the diagnostic is asked for. Null when it is off.
        private readonly SimEntryHash[][] _entityHashes;
        private readonly int[] _entityHashTicks;
        private readonly int[] _entityHashCounts;

        // The earliest tick a misprediction has dirtied since the last Advance, or
        // int.MaxValue when nothing needs correcting. RunPredictionConditional rewinds no
        // further back than this.
        private int _pendingReplayFrom = int.MaxValue;

        /// <summary>The newest tick whose inputs are final and which will not be replayed.</summary>
        public int ConfirmedTick { get { return _confirmedTick; } }

        /// <summary>The newest tick simulated, confirmed or predicted.</summary>
        public int CurrentTick { get { return _currentTick; } }

        /// <summary>The wall-clock tick the engine is working toward.</summary>
        public int TargetTick { get { return _targetTick; } }

        /// <summary>Ticks still required to reach the wall-clock target.</summary>
        public int CatchUpBacklog { get { return Math.Max(0, _targetTick - _currentTick); } }

        /// <summary>True while budgeted replay or forward simulation remains.</summary>
        public bool IsCatchingUp { get { return CatchUpBacklog > 0 || _pendingReplayFrom <= _currentTick; } }

        /// <summary>True when backlog reached the configured soft warning threshold.</summary>
        public bool IsCatchUpWarning
        {
            get { return CatchUpBacklog >= _netConfig.CatchUpWarningTicks; }
        }

        /// <summary>True when the most recent advance used its complete simulation-step budget.</summary>
        public bool BudgetExhausted { get; private set; }

        /// <summary>True when local history can no longer recover the authoritative timeline.</summary>
        public bool NeedsHardResync { get { return _hardResyncReason != null; } }

        /// <summary>Why a server snapshot is required, or null while locally recoverable.</summary>
        public string HardResyncReason { get { return _hardResyncReason; } }

        /// <summary>Raised once when the engine first requires an authoritative rebuild.</summary>
        public event Action<string> HardResyncRequired;

        /// <summary>
        /// True when the peer is waiting for inputs rather than advancing.
        /// </summary>
        /// <remarks>
        /// Reached when the lead over the confirmed tick would outrun what
        /// <see cref="SnapshotRing"/> can retain. Stalling is the intended behaviour: leading
        /// further would overwrite a tick a late input still needs, so the peer pauses
        /// visibly instead of losing the window silently.
        /// </remarks>
        public bool IsStalled { get { return _stalled; } }

        /// <summary>
        /// How far ahead of the confirmed tick the peer is currently simulating.
        /// </summary>
        /// <remarks>
        /// Emergent rather than a shared constant: it grows when confirmations lag and shrinks
        /// as they arrive, bounded by <see cref="SimConfig.SnapshotHistory"/>. A policy that
        /// wants Overwatch-style adaptation tunes the peer-local
        /// <see cref="SimAdaptiveInputLead"/> from authoritative latency telemetry.
        /// </remarks>
        public int CurrentLead { get { return _targetTick - _confirmedTick; } }

        /// <summary>How many ticks were resimulated on the most recent advance.</summary>
        public int LastReplayLength { get; private set; }

        /// <summary>Total ticks resimulated since the session started, for profiling.</summary>
        public long TotalReplayedTicks { get; private set; }

        /// <summary>Authoritative/speculative command changes that dirtied an already simulated tick.</summary>
        public long TotalMispredictions { get; private set; }

        /// <summary>Total confirmed, replayed, and forward steps run by the last advance.</summary>
        public int LastSimulationSteps { get; private set; }

        /// <summary>The snapshot history, for the netcode layer to read hashes from.</summary>
        public SnapshotRing Snapshots { get { return _snapshots; } }

        /// <summary>The input buffer this engine predicts from.</summary>
        public InputBuffer Inputs { get { return _inputs; } }

        /// <summary>Hash of every registered body's construction parameters.</summary>
        public ulong ConstructionHash { get { return _world.HashConstruction(); } }

        /// <summary>Hash of the simulation config this engine actually drives.</summary>
        public ulong SimulationConfigHash { get { return _config.ComputeHash(); } }

        /// <summary>Hash of the authoritative network policy this engine actually uses.</summary>
        public ulong NetworkConfigHash { get { return _netConfig.ComputeHash(); } }

        /// <summary>
        /// Creates an engine over a world.
        /// </summary>
        /// <param name="world">The world to drive. Must already have its actors committed.</param>
        /// <param name="playerIds">Every player in the session.</param>
        public RollbackEngine(DeterministicWorld world, IList<uint> playerIds, SimNetConfig netConfig)
        {
            if (world == null)
            {
                throw new ArgumentNullException("world");
            }
            if (netConfig == null)
            {
                throw new ArgumentNullException("netConfig");
            }
            string reason;
            if (!netConfig.ValidateForEngine(world.Config, out reason))
            {
                throw new ArgumentException(reason, "netConfig");
            }

            _world = world;
            _config = world.Config;
            _netConfig = netConfig;
            _inputs = new InputBuffer(playerIds, _config.SnapshotHistory);
            _snapshots = new SnapshotRing(_config.SnapshotHistory, world.StateSize);
            _events = new SimAuthoritativeEventBuffer(_config.SnapshotHistory);

            if (_config.PerEntityHashDiagnostics)
            {
                _entityHashes = new SimEntryHash[_config.SnapshotHistory][];
                _entityHashTicks = new int[_config.SnapshotHistory];
                _entityHashCounts = new int[_config.SnapshotHistory];
                for (int i = 0; i < _entityHashTicks.Length; ++i)
                {
                    _entityHashTicks[i] = -1;
                }
            }
        }

        /// <summary>Registers a step handler. Handlers run in registration order.</summary>
        /// <remarks>
        /// Registration order is part of the simulation, since two handlers applying
        /// forces in a different order produce different floating point results. Register
        /// them from one place at session start, not from each object's initialisation.
        /// </remarks>
        public void AddHandler(ISimStepHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException("handler");
            }
            _handlers.Add(handler);
        }

        /// <summary>Removes a previously registered handler.</summary>
        public bool RemoveHandler(ISimStepHandler handler)
        {
            return _handlers.Remove(handler);
        }

        /// <summary>Registers the deterministic consumer of server-assigned gameplay events.</summary>
        public void AddEventHandler(ISimAuthoritativeEventHandler handler)
        {
            if (handler == null) throw new ArgumentNullException("handler");
            _eventHandlers.Add(handler);
        }

        /// <summary>
        /// Registers the managed state channels that are captured and restored with the
        /// physics blob, or clears them by passing null.
        /// </summary>
        /// <remarks>
        /// Set once at session start, before <see cref="Initialise"/>, so tick zero already
        /// carries the managed state. A physics-only world leaves it unset; the engine then
        /// behaves exactly as it did before the gameplay layer existed.
        /// </remarks>
        public void SetStateProvider(ISimStateProvider provider)
        {
            _stateProvider = provider;
        }

        /// <summary>
        /// Captures the initial state as tick zero, before any stepping.
        /// </summary>
        /// <remarks>
        /// Must run after every actor has been registered and committed, and must produce
        /// an identical snapshot on every peer. That is the common starting point the
        /// whole session is built on.
        /// </remarks>
        public void Initialise()
        {
            _world.CommitPending();

            Snapshot slot = _snapshots.BeginWrite(0);
            ulong hash;
            byte[] buffer = slot.Data;
            int size = _world.CaptureState(ref buffer, out hash);
            slot.Data = buffer;
            _snapshots.CompleteWrite(slot, size, hash);
            CaptureManagedInto(slot);
            _snapshots.MarkConfirmedThrough(0);
            CaptureEntityHashes(0);
            _inputs.ResetAfterConfirmed(0);

            _confirmedTick = 0;
            _currentTick = 0;
            _targetTick = 0;

            SimLog.Info(string.Format(
                "Initialised at tick 0; physics {0} bytes hash 0x{1:X16}, combined hash 0x{2:X16}",
                size, hash, slot.CombinedHash));
        }

        /// <summary>
        /// Records a locally speculative input proposal.
        /// </summary>
        /// <remarks>
        /// The client session assigns its future server tick from adaptive lead telemetry.
        /// <para>
        /// Note what this deliberately does not do: it does not itself rewind. It records how
        /// far back the next <see cref="Advance"/> has to replay from -- the earliest tick a
        /// misprediction disturbed -- and the rewind happens there.
        /// </para>
        /// </remarks>
        public void SubmitSpeculativeInput(SimInput input, uint sequence)
        {
            RecordCorrection(_inputs.SubmitSpeculative(input, sequence));
        }

        /// <summary>Moves a local speculative command to the tick assigned by the server.</summary>
        public void RetimeSpeculativeInput(uint playerId, uint sequence, int requestedTick, int assignedTick)
        {
            RecordCorrection(_inputs.RetimeSpeculative(playerId, sequence, requestedTick, assignedTick));
        }

        /// <summary>Removes a local proposal rejected by the server.</summary>
        public void RejectSpeculativeInput(uint playerId, uint sequence, int requestedTick)
        {
            RecordCorrection(_inputs.ClearSpeculative(playerId, sequence, requestedTick));
        }

        /// <summary>Records every input in one finalized authoritative server frame.</summary>
        public void SubmitAuthoritativeFrame(SimCanonicalFrame frame)
        {
            if (frame == null || frame.Inputs == null)
            {
                throw new ArgumentNullException("frame");
            }
            SimInput[] inputs = new SimInput[frame.Inputs.Length];
            uint[] sequences = new uint[frame.Inputs.Length];
            for (int i = 0; i < frame.Inputs.Length; ++i)
            {
                inputs[i] = frame.Inputs[i].Input;
                sequences[i] = frame.Inputs[i].Sequence;
            }
            RecordCorrection(_inputs.SubmitAuthoritativeFrame(frame.Tick, inputs, sequences));
        }

        private void RecordCorrection(int tick)
        {
            if (tick >= 0)
            {
                TotalMispredictions += 1;
            }
            RecordDirty(tick);
        }

        /// <summary>Records an immutable server-assigned deterministic event.</summary>
        public void SubmitAuthoritativeEvent(SimAuthoritativeEvent command)
        {
            if (command.Tick < 0 || command.Payload == null)
            {
                throw new ArgumentException("An authoritative event requires a nonnegative tick and payload.");
            }
            if (_events.Contains(command.PlayerId, command.Sequence))
            {
                return;
            }
            if (command.Tick < _snapshots.OldestTick)
            {
                RequireHardResync("An authoritative event arrived after its tick left snapshot history.");
                return;
            }
            if (command.Tick <= _confirmedTick)
            {
                RequireHardResync(
                    "An authoritative event arrived after its assigned tick was already confirmed.");
                return;
            }
            if (_events.Submit(command) && command.Tick <= _currentTick)
            {
                RecordCorrection(command.Tick);
            }
        }

        /// <summary>Copies the complete authoritative event set assigned to one tick.</summary>
        public SimAuthoritativeEvent[] CopyAuthoritativeEvents(int tick)
        {
            return _events.CopyAt(tick);
        }

        private void RecordDirty(int tick)
        {
            if (tick >= 0 && tick < _pendingReplayFrom)
            {
                _pendingReplayFrom = tick;
            }
        }

        /// <summary>
        /// Advances the simulation by one tick of wall time.
        /// </summary>
        /// <remarks>
        /// Called once per fixed update. The target advances one tick per call while retained
        /// history can cover the configured maximum input lead. Confirmed drain, replay and
        /// catch-up together consume at most <see cref="SimNetConfig.MaxSimulationStepsPerFrame"/>
        /// complete ticks.
        /// <para>
        /// Clients run different-length prediction windows, but agree with server-confirmed
        /// state because replay is bitwise transparent under the fixed PGS solver.
        /// </para>
        /// </remarks>
        /// <returns>False when the peer is stalled waiting for inputs.</returns>
        public bool Advance()
        {
            LastReplayLength = 0;
            LastSimulationSteps = 0;
            BudgetExhausted = false;
            if (NeedsHardResync)
            {
                return false;
            }

            int proposedTarget = _targetTick + 1;
            int maxLead = _config.SnapshotHistory - _netConfig.MaximumInputLead - 1;
            if (proposedTarget - _confirmedTick <= maxLead)
            {
                _targetTick = proposedTarget;
                _stalled = false;
            }
            else
            {
                _stalled = true;
            }

            int budget = _netConfig.MaxSimulationStepsPerFrame;
            int newConfirmed = _inputs.ConfirmedThrough;
            if (newConfirmed > _targetTick)
            {
                newConfirmed = _targetTick;
            }
            AdvanceConfirmed(newConfirmed, ref budget);
            LastSimulationSteps = _netConfig.MaxSimulationStepsPerFrame - budget;
            if (NeedsHardResync)
            {
                return false;
            }
            if (_targetTick - _confirmedTick >= _netConfig.HardResyncTicks)
            {
                RequireHardResync(string.Format(
                    "Authoritative confirmation is {0} ticks behind the target.",
                    _targetTick - _confirmedTick));
                return false;
            }

            RunPredictionConditional(_targetTick, ref budget);
            LastSimulationSteps = _netConfig.MaxSimulationStepsPerFrame - budget;
            BudgetExhausted = budget == 0 && IsCatchingUp;
            return !_stalled || LastSimulationSteps > 0;
        }

        /// <summary>
        /// Steps the confirmed timeline forward to <paramref name="newConfirmed"/>.
        /// </summary>
        /// <remarks>
        /// Each confirmed tick is computed by restoring the previous confirmed snapshot
        /// and taking exactly one step, which is the same operation on every peer, from
        /// the same bytes, with the same inputs. That is what makes the resulting hash
        /// comparable bit-for-bit. The whole confirmed backlog is drained in one call: under
        /// PGS a confirmed tick is a pure function of the snapshot before it, so how many
        /// arrive in a frame -- a property of the network, not the simulation -- cannot change
        /// the state two peers agree on.
        /// </remarks>
        private void AdvanceConfirmed(int newConfirmed, ref int budget)
        {
            bool advanced = false;
            int previouslySimulatedThrough = _currentTick;
            while (_confirmedTick < newConfirmed && budget > 0)
            {
                int tick = _confirmedTick + 1;

                if (!TryRestoreTo(_confirmedTick))
                {
                    return;
                }
                StepOnce(tick, tick <= previouslySimulatedThrough);
                CaptureInto(tick, true);
                CaptureEntityHashes(tick);
                budget -= 1;
                advanced = true;

                _confirmedTick = tick;
                _currentTick = tick;
            }
            if (advanced && _currentTick < _targetTick)
            {
                _pendingReplayFrom = _confirmedTick + 1;
            }
        }

        /// <summary>
        /// Records a hash per entity for a freshly confirmed tick, while the world still holds
        /// exactly that state.
        /// </summary>
        /// <remarks>
        /// This is the only moment the table can be taken cheaply: <see cref="AdvanceConfirmed"/>
        /// has just restored, stepped and captured, so the live world *is* the confirmed tick.
        /// Asking for the same table later would mean restoring the world away from wherever
        /// prediction had left it and putting it back again.
        /// </remarks>
        private void CaptureEntityHashes(int tick)
        {
            if (_entityHashes == null)
            {
                return;
            }

            int count;
            SimEntryHash[] scratch = _world.HashPerEntity(out count);

            int slot = Slot(tick, _entityHashes.Length);
            SimEntryHash[] destination = _entityHashes[slot];
            if (destination == null || destination.Length < count)
            {
                destination = new SimEntryHash[count];
                _entityHashes[slot] = destination;
            }
            Array.Copy(scratch, destination, count);
            _entityHashTicks[slot] = tick;
            _entityHashCounts[slot] = count;
        }

        /// <summary>
        /// The per-entity hashes recorded for a confirmed tick, when
        /// <see cref="SimConfig.PerEntityHashDiagnostics"/> is on and the tick is still retained.
        /// </summary>
        /// <param name="tick">The confirmed tick to look up.</param>
        /// <param name="entries">The recorded table, valid until the slot is reused.</param>
        /// <param name="count">How many entries of <paramref name="entries"/> are meaningful.</param>
        public bool TryGetConfirmedEntityHashes(int tick, out SimEntryHash[] entries, out int count)
        {
            entries = null;
            count = 0;
            if (_entityHashes == null || tick < 0)
            {
                return false;
            }

            int slot = Slot(tick, _entityHashes.Length);
            if (_entityHashTicks[slot] != tick || _entityHashes[slot] == null)
            {
                return false;
            }

            entries = _entityHashes[slot];
            count = _entityHashCounts[slot];
            return true;
        }

        private static int Slot(int tick, int capacity)
        {
            int slot = tick % capacity;
            return slot < 0 ? slot + capacity : slot;
        }

        /// <summary>
        /// Resimulates the prediction window up to <paramref name="windowEnd"/>, but only
        /// from the earliest tick a misprediction or a new confirmation actually disturbed.
        /// </summary>
        /// <remarks>
        /// PGS has transparent replay -- restore-and-step is a pure function of the restored
        /// snapshot (§4) -- so a shorter, data-dependent rewind lands on exactly the state a
        /// full re-simulation would, and any redundant work would be pure cost. This replays
        /// only the ticks whose input changed (<see cref="InputBuffer.Submit"/>'s return) or
        /// that were newly exposed above the last simulated tick, and reuses every valid
        /// snapshot below that.
        /// <para>
        /// The window end is a parameter because the free-running clock hands it the tick it
        /// just advanced to (<c>CurrentTick + 1</c>), or the current tick when it is pinned by
        /// the history bound. On entry <see cref="_currentTick"/> is the last tick that
        /// already has a snapshot; on exit it is <paramref name="windowEnd"/>.
        /// </para>
        /// <para>
        /// The confirmed timeline is advanced first, and its hashes are what peers compare,
        /// so a bug here can only smear this peer's own prediction between confirmations; it
        /// cannot desync the session. Even so this keeps the cold-step discipline: one restore
        /// before every step, the first being the rewind to the tick before the replay start,
        /// and never two restores in a row.
        /// </para>
        /// </remarks>
        private void RunPredictionConditional(int windowEnd, ref int budget)
        {
            if (windowEnd < _confirmedTick)
            {
                windowEnd = _confirmedTick;
            }

            // Everything above the newest tick already simulated is new and unconditionally
            // needs a step. A misprediction pulls the start earlier, but never below the
            // first predicted tick: anything at or under the confirmed frontier was folded
            // into the confirmed drain above, which leaves the freshly confirmed snapshot
            // at _confirmedTick as the predecessor the prediction window replays from.
            int replayFrom = _currentTick + 1;
            if (_pendingReplayFrom != int.MaxValue)
            {
                int dirty = _pendingReplayFrom;
                if (dirty < _confirmedTick + 1)
                {
                    dirty = _confirmedTick + 1;
                }
                if (dirty < replayFrom)
                {
                    replayFrom = dirty;
                }
            }
            int replayed = 0;
            if (replayFrom <= windowEnd && budget > 0)
            {
                // The predecessor snapshot is valid by construction: replayFrom is the
                // earliest dirty tick, so replayFrom - 1 was not disturbed and its snapshot
                // -- confirmed when it equals _confirmedTick, otherwise a prediction from a
                // prior frame -- still holds.
                if (!TryRestoreTo(replayFrom - 1))
                {
                    return;
                }
                int lastReplayed = replayFrom - 1;
                for (int tick = replayFrom; tick <= windowEnd && budget > 0; ++tick)
                {
                    // One restore before every step, including steps that were not rolled
                    // back to. PhysX warm-starts its solver from cached contact data, and
                    // moving an actor throws that cache away, so a step that follows a restore
                    // runs cold while a step that follows another step runs warm, and the two
                    // do not agree. Restoring before every step makes every step cold and
                    // removes the asymmetry; under PGS that makes a replayed run bitwise
                    // identical to a run that was never rolled back. The first restore is the
                    // rewind above, so only subsequent steps re-restore. Restoring twice would
                    // be as wrong as not at all: quaternion normalisation is not idempotent and
                    // a second restore shifts the rotation by one ULP.
                    if (tick > replayFrom)
                    {
                        if (!TryRestoreTo(tick - 1))
                        {
                            return;
                        }
                    }

                    StepOnce(tick, true);
                    CaptureInto(tick, false);
                    ++replayed;
                    budget -= 1;
                    lastReplayed = tick;
                }

                _currentTick = lastReplayed;
                _pendingReplayFrom = lastReplayed < windowEnd
                    ? lastReplayed + 1
                    : int.MaxValue;
            }
            else if (replayFrom > windowEnd)
            {
                _pendingReplayFrom = int.MaxValue;
            }

            LastReplayLength = replayed;
            TotalReplayedTicks += replayed;

            SimLog.Verbose(string.Format(
                "Conditionally replayed {0} tick(s) from tick {1} to {2}", replayed, replayFrom, windowEnd));
        }

        private bool TryRestoreTo(int tick)
        {
            Snapshot snapshot;
            if (!_snapshots.TryGet(tick, out snapshot))
            {
                RequireHardResync(string.Format(
                    "Cannot rewind to tick {0}; the oldest retained tick is {1}. This peer has fallen further " +
                    "behind than SimConfig.SnapshotHistory ({2} ticks) allows and needs a synchronised rebuild.",
                    tick, _snapshots.OldestTick, _config.SnapshotHistory));
                return false;
            }

            // Just reinstate the snapshot. No contact-state reset is needed or wanted here:
            // the cold-step discipline re-poses every body with setGlobalPose at the top of
            // each step, which invalidates PhysX's contact cache, so there is no carried
            // warm-start residue left to diverge -- restore + step is a pure function of the
            // restored state. This was verified in isolation (PxwOffsetShapeRepro Stage G:
            // restore + step stays bit-identical across seven deliberately hostile histories,
            // for the plain sphere, the compound ball, and a pooled world). Resetting contacts
            // on every restore was tried and is actively harmful under variable-depth rollback,
            // where peers rewind by different amounts and so would reset a different number of
            // times. Hard resynchronisation recreates the native world instead.
            _world.RestoreState(snapshot.Data, snapshot.Size);
            RestoreManagedFrom(snapshot);
            return true;
        }

        private void RequireHardResync(string reason)
        {
            if (_hardResyncReason != null)
            {
                return;
            }
            _hardResyncReason = reason;
            SimLog.Warning(reason);
            Action<string> handler = HardResyncRequired;
            if (handler != null)
            {
                handler(reason);
            }
        }

        private void StepOnce(int tick, bool isReplay)
        {
            SimLog.CurrentTick = tick;

            SimInputFrame frame = _inputs.GetOrPredict(tick);

            IList<SimAuthoritativeEvent> events = _events.Get(tick);
            for (int eventIndex = 0; eventIndex < events.Count; ++eventIndex)
            {
                for (int handlerIndex = 0; handlerIndex < _eventHandlers.Count; ++handlerIndex)
                {
                    _eventHandlers[handlerIndex].OnAuthoritativeEvent(events[eventIndex], isReplay);
                }
            }

            for (int i = 0; i < _handlers.Count; ++i)
            {
                _handlers[i].OnBeforeStep(_world, tick, frame, isReplay);
            }

            _world.Step();

            for (int i = 0; i < _handlers.Count; ++i)
            {
                _handlers[i].OnAfterStep(_world, tick, isReplay);
            }

            SimLog.CurrentTick = -1;
        }

        private void CaptureInto(int tick, bool confirmed)
        {
            Snapshot slot = _snapshots.BeginWrite(tick);
            ulong hash;
            byte[] buffer = slot.Data;
            int size = _world.CaptureState(ref buffer, out hash);
            slot.Data = buffer;
            _snapshots.CompleteWrite(slot, size, hash);
            CaptureManagedInto(slot);
            slot.IsConfirmed = confirmed;
        }

        /// <summary>
        /// Captures the two managed channels into a slot the physics channel has already
        /// been written to. A no-op that leaves the channels empty when no provider is set.
        /// </summary>
        private void CaptureManagedInto(Snapshot slot)
        {
            if (_stateProvider == null)
            {
                slot.EntitySize = 0;
                slot.EntityHash = 0;
                slot.GameSize = 0;
                slot.GameHash = 0;
                return;
            }

            SimStateWriter entityWriter = new SimStateWriter(slot.EntityData);
            _stateProvider.CaptureEntityState(ref entityWriter);
            slot.EntityData = entityWriter.Buffer;
            slot.EntitySize = entityWriter.Position;
            slot.EntityHash = entityWriter.Hash;

            SimStateWriter gameWriter = new SimStateWriter(slot.GameData);
            _stateProvider.CaptureGameState(ref gameWriter);
            slot.GameData = gameWriter.Buffer;
            slot.GameSize = gameWriter.Position;
            slot.GameHash = gameWriter.Hash;
        }

        /// <summary>
        /// Restores the two managed channels from a slot. A no-op when no provider is set.
        /// </summary>
        private void RestoreManagedFrom(Snapshot slot)
        {
            if (_stateProvider == null)
            {
                return;
            }

            SimStateReader entityReader = new SimStateReader(slot.EntityData, slot.EntitySize);
            _stateProvider.RestoreEntityState(ref entityReader);

            SimStateReader gameReader = new SimStateReader(slot.GameData, slot.GameSize);
            _stateProvider.RestoreGameState(ref gameReader);
        }

        /// <summary>
        /// Returns the confirmed snapshot for a tick, for hashing against other peers or
        /// for handing to a joining peer.
        /// </summary>
        /// <returns>False when the tick is not retained or is not yet confirmed.</returns>
        public bool TryGetConfirmedSnapshot(int tick, out Snapshot snapshot)
        {
            if (!_snapshots.TryGet(tick, out snapshot))
            {
                return false;
            }
            if (!snapshot.IsConfirmed)
            {
                snapshot = null;
                return false;
            }
            return true;
        }

        /// <summary>The sorted player-ID set this engine currently runs with.</summary>
        public uint[] CopyPlayerIds()
        {
            return _inputs.CopyPlayerIds();
        }

        /// <summary>
        /// Packages a confirmed tick's full state — every channel plus the current roster —
        /// into a <see cref="SimRebuildState"/> for a synchronised rebuild.
        /// </summary>
        /// <remarks>
        /// Called on the peer that owns the timeline (the host) to produce the agreed state
        /// every other peer, including a mid-match joiner, restores through
        /// <see cref="PrepareForRebuild(ref SimRebuildState, Action)"/>. The returned state owns
        /// its buffers (they are copied out of the ring), so it is safe to serialise and send
        /// after later ticks have recycled the slot.
        /// </remarks>
        /// <param name="tick">The confirmed tick to capture. Usually <see cref="ConfirmedTick"/>.</param>
        /// <param name="state">The captured state, valid only when this returns true.</param>
        /// <returns>False when the tick is not retained or not yet confirmed.</returns>
        public bool CaptureRebuildState(int tick, out SimRebuildState state)
        {
            Snapshot snapshot;
            if (!TryGetConfirmedSnapshot(tick, out snapshot))
            {
                state = new SimRebuildState();
                return false;
            }

            SimRebuildState raw = new SimRebuildState();
            raw.ResumeTick = tick;
            raw.PlayerIds = _inputs.CopyPlayerIds();
            _inputs.CopyBaseline(tick, out raw.LastInputs, out raw.LastInputSequences);
            raw.PendingEvents = _events.CopyAfter(tick);
            raw.PhysicsData = snapshot.Data;
            raw.PhysicsSize = snapshot.Size;
            raw.PhysicsHash = snapshot.Hash;
            raw.EntityData = snapshot.EntityData;
            raw.EntitySize = snapshot.EntitySize;
            raw.GameData = snapshot.GameData;
            raw.GameSize = snapshot.GameSize;

            // The ring reuses these buffers, so hand back copies that own their bytes.
            state = raw.Compact();
            return true;
        }

        /// <summary>
        /// Produces a finalized rebuild for a roster change and leaves the producing peer on
        /// the exact same restore path as every receiver.
        /// </summary>
        /// <remarks>
        /// A roster change usually needs two states: the current confirmed state is restored
        /// under the new player set, then <paramref name="reconcile"/> activates or retires
        /// pooled bodies and that result becomes the payload sent to the other peers. Merely
        /// capturing that result is not enough. The producer performed the reconcile's native
        /// enable/disable transitions, while a receiver restores an already-final payload and
        /// its reconcile is idempotent. PhysX keeps scene and island bookkeeping that is not in
        /// the snapshot, so those two histories can later solve a shared contact island
        /// differently even though the captured bytes agree.
        /// <para>
        /// This method owns the complete producer protocol: capture the old confirmed state,
        /// apply it with the target roster, reconcile, capture the finalized payload, then
        /// apply that finalized payload once more on the producer. The returned state is the
        /// same state the producer consumed and is safe to serialize. Samples should use this
        /// instead of composing <see cref="CaptureRebuildState"/> and
        /// <see cref="PrepareForRebuild(ref SimRebuildState, Action)"/> themselves.
        /// </para>
        /// The caller's gameplay roster must already describe <paramref name="playerIds"/>
        /// when this method is entered, because the reconcile callback normally reads it.
        /// The callback must be idempotent, as required by <c>PrepareForRebuild</c>.
        /// </remarks>
        /// <param name="playerIds">The complete player set after the rebuild.</param>
        /// <param name="reconcile">
        /// Brings managed entities and pooled native bodies into line with the target roster.
        /// </param>
        /// <param name="state">The finalized, self-owned payload to broadcast.</param>
        /// <returns>False only when the current confirmed snapshot is unavailable.</returns>
        public bool TryProduceRebuildState(IList<uint> playerIds, Action reconcile,
            out SimRebuildState state)
        {
            if (playerIds == null)
            {
                throw new ArgumentNullException("playerIds");
            }

            SimRebuildState seed;
            if (!CaptureRebuildState(_confirmedTick, out seed))
            {
                state = new SimRebuildState();
                return false;
            }

            uint[] priorPlayerIds = seed.PlayerIds;
            SimInput[] priorInputs = seed.LastInputs;
            uint[] priorSequences = seed.LastInputSequences;
            seed.PlayerIds = new uint[playerIds.Count];
            for (int i = 0; i < playerIds.Count; ++i)
            {
                seed.PlayerIds[i] = playerIds[i];
            }
            Array.Sort(seed.PlayerIds);
            seed.LastInputs = new SimInput[seed.PlayerIds.Length];
            seed.LastInputSequences = new uint[seed.PlayerIds.Length];
            for (int i = 0; i < seed.PlayerIds.Length; ++i)
            {
                uint playerId = seed.PlayerIds[i];
                int priorSlot = Array.BinarySearch(priorPlayerIds, playerId);
                if (priorSlot >= 0 && priorInputs != null && priorSequences != null
                    && priorSlot < priorInputs.Length && priorSlot < priorSequences.Length)
                {
                    seed.LastInputs[i] = priorInputs[priorSlot];
                    seed.LastInputSequences[i] = priorSequences[priorSlot];
                }
                else
                {
                    seed.LastInputs[i] = SimInput.Neutral(playerId, seed.ResumeTick);
                }
                seed.LastInputs[i].PlayerId = playerId;
                seed.LastInputs[i].Tick = seed.ResumeTick;
            }

            // First apply produces the desired final state from the old confirmed state.
            PrepareForRebuild(ref seed, reconcile);
            if (!CaptureRebuildState(_confirmedTick, out state))
            {
                return false;
            }

            // Then consume exactly what receivers consume. This second apply is the invariant
            // that cannot safely be left to each sample's rebuild orchestration.
            PrepareForRebuild(ref state, reconcile);
            return true;
        }

        /// <summary>
        /// Restores an agreed <see cref="SimRebuildState"/> on this peer: rebuilds the native
        /// world, restores every channel from the supplied bytes rather than from local
        /// history, replaces the roster when it changed, and resumes from the agreed tick.
        /// </summary>
        /// <remarks>
        /// Managed channels are restored straight from the payload, so a fresh joiner
        /// that has built the identical static world (same pool, same stable IDs) reaches the
        /// exact entity and game state every existing peer holds, without simulating a tick.
        /// <para>
        /// A changed roster is applied by rebuilding the input buffer for the new player set.
        /// Because the pool is fixed and spawning only toggles an active flag that lives in the
        /// entity channel (see <see cref="Gameplay.SimEntityPool"/>), the physics layout does
        /// not depend on the roster: the new player's avatar is spawned by a deterministic
        /// action after resume, not carried in this snapshot.
        /// </para>
        /// </remarks>
        /// <param name="state">The agreed state, by reference to avoid copying its buffers.</param>
        /// <param name="reconcile">
        /// An optional callback run after the world and roster have been restored but before the
        /// resume snapshot is captured, so the game can bring its entities into line with the new
        /// roster — activate a joiner's avatar, retire a leaver's — in a way that is baked into
        /// the captured tick rather than replayed afterwards. It must be a pure function of the
        /// restored state and the roster (both identical on every peer), so the snapshot it
        /// produces agrees bit-for-bit. Running it again on a peer that received an
        /// already-reconciled snapshot is harmless because it is idempotent: the desired set
        /// already matches.
        /// </param>
        public void PrepareForRebuild(ref SimRebuildState state, Action reconcile = null)
        {
            if (state.PhysicsData == null)
            {
                throw new ArgumentException("Rebuild state has no physics payload.", "state");
            }
            if (state.ResumeTick < 0)
            {
                throw new ArgumentOutOfRangeException("state", "Tick numbers start at zero.");
            }

            int resumeTick = state.ResumeTick;
            SimLog.Info(string.Format(
                "Synchronised rebuild: resuming at tick {0} from a {1} byte snapshot, {2} player(s), rebuilding the native world",
                resumeTick, state.PhysicsSize, state.PlayerIds == null ? 0 : state.PlayerIds.Length));

            _world.RecreateNativeWorld();

            _world.RestoreState(state.PhysicsData, state.PhysicsSize);

            // Restore the managed channels from the payload, not from local history, so a peer
            // that never simulated these ticks still lands on the agreed managed state.
            if (_stateProvider != null)
            {
                SimStateReader entityReader = new SimStateReader(
                    state.EntityData ?? EmptyBytes, state.EntitySize);
                _stateProvider.RestoreEntityState(ref entityReader);

                SimStateReader gameReader = new SimStateReader(
                    state.GameData ?? EmptyBytes, state.GameSize);
                _stateProvider.RestoreGameState(ref gameReader);
            }

            // Replace the roster when it changed. A rebuild is the only safe moment to do this,
            // because it discards the timeline the old input buffer described anyway.
            if (state.PlayerIds != null && !SamePlayerSet(state.PlayerIds))
            {
                _inputs = new InputBuffer(state.PlayerIds, _config.SnapshotHistory);
            }

            // Bring entities into line with the new roster before the tick is captured, so the
            // change rides in the snapshot every peer restores rather than being replayed after.
            if (reconcile != null)
            {
                reconcile();
                _world.CommitPending();
            }

            // Everything before the rebuild describes a timeline that no longer exists.
            _snapshots.Clear();
            _inputs.ResetAfterConfirmed(resumeTick);
            if (state.LastInputs != null && state.LastInputSequences != null
                && state.LastInputs.Length == _inputs.PlayerCount
                && state.LastInputSequences.Length == _inputs.PlayerCount)
            {
                _inputs.SeedBaseline(
                    resumeTick, state.LastInputs, state.LastInputSequences);
            }
            _events.ResetAfterConfirmed(resumeTick, state.PendingEvents);

            Snapshot slot = _snapshots.BeginWrite(resumeTick);
            ulong hash;
            byte[] buffer = slot.Data;
            int captured = _world.CaptureState(ref buffer, out hash);
            slot.Data = buffer;
            _snapshots.CompleteWrite(slot, captured, hash);
            CaptureManagedInto(slot);
            slot.IsConfirmed = true;
            if (reconcile == null && state.PhysicsHash != 0 && hash != state.PhysicsHash)
            {
                throw new InvalidOperationException(string.Format(
                    "Rebuild physics hash 0x{0:X16} restored as 0x{1:X16}.",
                    state.PhysicsHash, hash));
            }

            _confirmedTick = resumeTick;
            _currentTick = resumeTick;
            _targetTick = resumeTick;
            _stalled = false;
            LastReplayLength = 0;
            LastSimulationSteps = 0;
            BudgetExhausted = false;
            _pendingReplayFrom = int.MaxValue;
            _hardResyncReason = null;

            SimLog.Info(string.Format("Rebuild complete; state hash is 0x{0:X16}", hash));
        }

        private static readonly byte[] EmptyBytes = new byte[0];

        private bool SamePlayerSet(uint[] candidate)
        {
            uint[] current = _inputs.CopyPlayerIds();
            if (candidate.Length != current.Length)
            {
                return false;
            }
            uint[] sorted = new uint[candidate.Length];
            Array.Copy(candidate, sorted, candidate.Length);
            Array.Sort(sorted);
            for (int i = 0; i < current.Length; ++i)
            {
                if (sorted[i] != current[i])
                {
                    return false;
                }
            }
            return true;
        }

    }
}
