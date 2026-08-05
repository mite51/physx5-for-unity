using System;
using System.Collections.Generic;
using UNDPWR.Core;
using UNDPWR.Diagnostics;
using UNDPWR.Interop;

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
    /// <b>The rule this engine exists to enforce.</b> Every peer performs an identical
    /// sequence of operations every tick. Not a similar one, an identical one. That is
    /// the entire reason confirmed-tick hashes can be compared bit-for-bit, and it is why
    /// the prediction horizon is fixed rather than adapted to measured latency.
    ///
    /// <para>What the measurements actually say, from the native suite, because the
    /// obvious explanation is the wrong one. A rewind is not lossy: <c>restore(S);
    /// step()</c> is a pure function of <c>S</c>, and two worlds driven along
    /// deliberately different histories agree bit-for-bit from the moment they are handed
    /// the same snapshot. Under PGS the cold-step discipline below makes replay bitwise
    /// transparent outright, and peers rewinding by four and by sixteen land on identical
    /// state. The framework runs TGS, which carries per-substep state that a restore does
    /// not reach and that nothing exposed clears, so a peer that rewound three ticks and
    /// one that rewound five differ by a residual of about 3e-09 m/s on the first replayed
    /// step. That is far below the resolution of captured state, so a single divergent
    /// rollback shows nothing at all, and then it accumulates for a few hundred frames and
    /// flips a bit long after anything that could be blamed for it. Invisible once and
    /// fatal later is the worst shape a bug can have, so peers rewind the same amount every
    /// tick. Documentation/DeterminismInvestigation.md section 8 records what would have to
    /// change for the horizon to become adaptive.</para>
    ///
    /// <para><b>What a tick looks like.</b> Every tick, unconditionally:</para>
    /// <list type="number">
    /// <item><description>restore the snapshot at the confirmed tick;</description></item>
    /// <item><description>step forward through the confirmed ticks that have become
    /// available, capturing each;</description></item>
    /// <item><description>step forward exactly <see cref="SimConfig.PredictionHorizon"/>
    /// further ticks using predicted inputs.</description></item>
    /// </list>
    /// <para>The same work happens whether or not a misprediction occurred, which costs a
    /// little throughput and buys the identical-sequence property. A peer whose inputs
    /// arrive later than the horizon stalls rather than predicting further ahead, because
    /// predicting further would make its sequence differ from everyone else's.</para>
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
        private readonly InputBuffer _inputs;
        private readonly SnapshotRing _snapshots;
        private readonly List<ISimStepHandler> _handlers = new List<ISimStepHandler>();
        private ISimStateProvider _stateProvider;

        private int _confirmedTick = -1;
        private int _currentTick = -1;
        private bool _stalled;

        // The earliest tick a misprediction has dirtied since the last Advance, or
        // int.MaxValue when nothing needs correcting. Only maintained under conditional
        // rollback; the fixed-horizon path re-runs the whole window and does not consult it.
        private int _pendingReplayFrom = int.MaxValue;

        /// <summary>The newest tick whose inputs are final and which will not be replayed.</summary>
        public int ConfirmedTick { get { return _confirmedTick; } }

        /// <summary>The newest tick simulated, confirmed or predicted.</summary>
        public int CurrentTick { get { return _currentTick; } }

        /// <summary>
        /// The tick a local input sampled right now should be stamped for.
        /// </summary>
        /// <remarks>
        /// <see cref="CurrentTick"/> plus <see cref="SimConfig.LocalInputDelay"/>. Build
        /// local input against this rather than against <see cref="CurrentTick"/>: a tick
        /// stamped further ahead reaches the other peers before they have to guess at it,
        /// which is the difference between a remote player who moves and one who moves and
        /// then snaps somewhere else.
        /// </remarks>
        public int LocalInputTick { get { return _currentTick + _config.LocalInputDelay; } }

        /// <summary>
        /// True when the peer is waiting for inputs rather than advancing.
        /// </summary>
        /// <remarks>
        /// Reached when confirmation falls further behind than the prediction horizon
        /// allows. Stalling is the intended behaviour: predicting further ahead would
        /// break the identical-sequence rule and silently desync this peer instead of
        /// visibly pausing it.
        /// </remarks>
        public bool IsStalled { get { return _stalled; } }

        /// <summary>
        /// How far ahead of the confirmed tick the peer is currently simulating.
        /// </summary>
        /// <remarks>
        /// Under the fixed horizon this is pinned at <see cref="SimConfig.PredictionHorizon"/>.
        /// Under the free-running clock (<see cref="SimConfig.FreeRunningClock"/>) it is
        /// emergent: it grows when confirmations lag and shrinks as they arrive, and a policy
        /// that wants Overwatch-style adaptation tunes the peer-local
        /// <see cref="SimConfig.LocalInputDelay"/> from watching it.
        /// </remarks>
        public int CurrentLead { get { return _currentTick - _confirmedTick; } }

        /// <summary>How many ticks were resimulated on the most recent advance.</summary>
        public int LastReplayLength { get; private set; }

        /// <summary>Total ticks resimulated since the session started, for profiling.</summary>
        public long TotalReplayedTicks { get; private set; }

        /// <summary>The snapshot history, for the netcode layer to read hashes from.</summary>
        public SnapshotRing Snapshots { get { return _snapshots; } }

        /// <summary>The input buffer this engine predicts from.</summary>
        public InputBuffer Inputs { get { return _inputs; } }

        /// <summary>
        /// Creates an engine over a world.
        /// </summary>
        /// <param name="world">The world to drive. Must already have its actors committed.</param>
        /// <param name="playerIds">Every player in the session.</param>
        public RollbackEngine(DeterministicWorld world, IList<uint> playerIds)
        {
            if (world == null)
            {
                throw new ArgumentNullException("world");
            }

            _world = world;
            _config = world.Config;
            _inputs = new InputBuffer(playerIds, _config.SnapshotHistory);
            _snapshots = new SnapshotRing(_config.SnapshotHistory, world.StateSize);
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

            _confirmedTick = 0;
            _currentTick = 0;

            SimLog.Info(string.Format(
                "Initialised at tick 0; physics {0} bytes hash 0x{1:X16}, combined hash 0x{2:X16}",
                size, hash, slot.CombinedHash));
        }

        /// <summary>
        /// Records a received input, which may make more ticks confirmable.
        /// </summary>
        /// <remarks>
        /// Takes the local peer's own input as well as everything that arrived over the
        /// network; the two are the same thing once stamped. Stamp local input with
        /// <see cref="LocalInputTick"/> rather than <see cref="CurrentTick"/> so it reaches
        /// the other peers before they guess at it.
        /// <para>
        /// Note what this deliberately does not do: it does not trigger a rollback. The
        /// engine rewinds on a fixed schedule in <see cref="Advance"/> whether or not a
        /// misprediction happened, because rewinding only when mispredicted would make
        /// the operation sequence depend on network timing and differ between peers.
        /// </para>
        /// </remarks>
        public void SubmitInput(SimInput input)
        {
            int mispredictedFrom = _inputs.Submit(input);

            // Under the fixed horizon the return is advisory and ignored: the engine rewinds
            // the whole window every tick regardless. Under conditional rollback it is the
            // whole point -- it says how far back the next Advance has to replay from.
            if (_config.ConditionalRollback && mispredictedFrom >= 0 && mispredictedFrom < _pendingReplayFrom)
            {
                _pendingReplayFrom = mispredictedFrom;
            }
        }

        /// <summary>
        /// Advances the simulation by one tick of wall time.
        /// </summary>
        /// <remarks>
        /// Called once per fixed update. Performs the full restore-and-replay cycle every
        /// time, so its cost is steady rather than spiking on the frames a misprediction
        /// happens to land.
        /// </remarks>
        /// <returns>False when the peer is stalled waiting for inputs.</returns>
        public bool Advance()
        {
            if (_config.FreeRunningClock)
            {
                return AdvanceFreeRunning();
            }

            // How far the confirmed timeline may advance this call. The fixed horizon caps
            // it at one tick; conditional rollback drains the whole backlog. See the two
            // branches below for why the cap is safe to lift only under PGS.
            //
            // Draining the whole confirmed backlog in one call looks harmless and is not,
            // in general. A confirmed tick is computed as restore(previous) then one step,
            // and the restore is exact for everything the snapshot holds -- but not for the
            // state the solver carries across it. The cold-step discipline below makes the
            // predecessor's contact cache irrelevant; it does not make the predecessor
            // irrelevant, because TGS substep state survives a restore regardless.
            //
            // Draining three confirmations at once puts a confirmed step directly after
            // another confirmed step; draining them one per frame puts it after a
            // prediction run instead. Same tick, same inputs, different predecessor,
            // different result under TGS. Since the number of confirmations arriving in a
            // frame is a property of the network rather than of the simulation, TGS peers
            // would disagree about confirmed state purely because their packets clumped
            // differently. The one-per-call cap keeps their per-frame sequence identical:
            // restore(c), step(c+1), capture, restore(c+1), then horizon prediction steps.
            //
            // Under PGS the predecessor is irrelevant, because restore-and-step was measured
            // bitwise transparent (§4): a confirmed tick is a pure function of the confirmed
            // snapshot before it, however many are drained in one frame. Conditional
            // rollback lifts the cap on that basis, and Validate refuses the flag under any
            // other solver.
            int newConfirmed = _config.ConditionalRollback
                ? _inputs.ConfirmedThrough
                : Math.Min(_inputs.ConfirmedThrough, _confirmedTick + 1);

            // Refuse to run further ahead than the horizon. A peer that predicted further
            // would outrun the state history a late input still needs; stalling instead
            // makes the problem visible and recoverable.
            int targetTick = _confirmedTick + _config.PredictionHorizon;
            if (newConfirmed <= _confirmedTick && _currentTick >= targetTick)
            {
                if (!_stalled)
                {
                    _stalled = true;
                    SimLog.Warning(string.Format(
                        "Stalled at tick {0}: confirmed only through {1}, and the prediction horizon of {2} " +
                        "ticks does not allow running further ahead. An input has {3} ticks of one-way flight " +
                        "time at this configuration; widen PredictionHorizon or LocalInputDelay to buy more.",
                        _currentTick, _confirmedTick, _config.PredictionHorizon,
                        _config.PredictionHorizon + _config.LocalInputDelay - 1));
                }
                return false;
            }

            if (_stalled)
            {
                _stalled = false;
                SimLog.Info(string.Format("Resumed at tick {0}", _currentTick));
            }

            AdvanceConfirmed(newConfirmed);
            if (_config.ConditionalRollback)
            {
                RunPredictionConditional(_confirmedTick + _config.PredictionHorizon);
            }
            else
            {
                RunPrediction();
            }
            return true;
        }

        /// <summary>
        /// Advances the free-running clock: one predicted tick of wall time, driven by the
        /// fixed update rather than pinned to the confirmed frontier. Phase 3.
        /// </summary>
        /// <remarks>
        /// The fixed horizon locks <c>CurrentTick = ConfirmedTick + PredictionHorizon</c>, so
        /// the clock moves only when confirmation does and a whole session runs in lockstep
        /// with its slowest link. Here the clock advances once per call regardless, and the
        /// lead over the confirmed tick is whatever the network happens to allow. The only
        /// hard stop is running further ahead than <see cref="SnapshotRing"/> can retain,
        /// which is a physical limit rather than a hashed constant: a lead of
        /// <c>SnapshotHistory - LocalInputDelay - 1</c> is the most that leaves room for both
        /// the retained window and local input stamped ahead.
        /// <para>
        /// Requires conditional rollback, and therefore PGS. A free-running clock means peers
        /// run different-length prediction windows every frame, which only lands on agreeing
        /// confirmed state because replay is transparent and the confirmed timeline is
        /// advanced by the same cold restore-and-step regardless of window width.
        /// <see cref="SimConfig.Validate"/> enforces the dependency.
        /// </para>
        /// </remarks>
        private bool AdvanceFreeRunning()
        {
            int newConfirmed = _inputs.ConfirmedThrough;

            // The most the clock may lead confirmation by. Mirrors the SnapshotHistory bound
            // Validate checks against PredictionHorizon: the ring must retain the whole live
            // window, from the confirmed tick out to the furthest tick local input is stamped.
            int maxLead = _config.SnapshotHistory - _config.LocalInputDelay - 1;
            if (maxLead < 1)
            {
                maxLead = 1;
            }

            int nextTick = _currentTick + 1;
            bool canAdvanceClock = (nextTick - newConfirmed) <= maxLead;

            // Nothing to do only when the clock is pinned by the history limit and no fresh
            // confirmation has arrived to relieve it. New confirmation is always worth
            // processing -- it shrinks the lead and lets the clock move again next frame.
            if (!canAdvanceClock && newConfirmed <= _confirmedTick)
            {
                if (!_stalled)
                {
                    _stalled = true;
                    SimLog.Warning(string.Format(
                        "Stalled at tick {0}: confirmed only through {1}, and the lead cannot exceed {2} ticks " +
                        "without outrunning the {3}-tick snapshot history. Widen SnapshotHistory to lead further, " +
                        "or the peer that is behind needs to catch up.",
                        _currentTick, _confirmedTick, maxLead, _config.SnapshotHistory));
                }
                return false;
            }

            if (_stalled)
            {
                _stalled = false;
                SimLog.Info(string.Format("Resumed at tick {0}", _currentTick));
            }

            AdvanceConfirmed(newConfirmed);

            int windowEnd = canAdvanceClock ? nextTick : _currentTick;
            RunPredictionConditional(windowEnd);
            return true;
        }

        /// <summary>
        /// Steps the confirmed timeline forward to <paramref name="newConfirmed"/>.
        /// </summary>
        /// <remarks>
        /// Each confirmed tick is computed by restoring the previous confirmed snapshot
        /// and taking exactly one step, which is the same operation on every peer, from
        /// the same bytes, with the same inputs. That is what makes the resulting hash
        /// comparable bit-for-bit. The fixed-horizon path passes at most one more tick per
        /// call so TGS peers, whose confirmed step depends on its predecessor, run an
        /// identical per-frame sequence; conditional rollback drains the whole backlog
        /// because under PGS a confirmed tick is a pure function of the snapshot before it.
        /// </remarks>
        private void AdvanceConfirmed(int newConfirmed)
        {
            while (_confirmedTick < newConfirmed)
            {
                int tick = _confirmedTick + 1;

                RestoreTo(_confirmedTick);
                StepOnce(tick, false);
                CaptureInto(tick, true);

                _confirmedTick = tick;
                if (_currentTick < tick)
                {
                    _currentTick = tick;
                }
            }
        }

        /// <summary>
        /// Resimulates the prediction window from the confirmed tick.
        /// </summary>
        /// <remarks>
        /// Always exactly <see cref="SimConfig.PredictionHorizon"/> ticks, replayed in
        /// full every time. Replaying unconditionally is what keeps every peer's
        /// operation sequence the same length, and it also removes the frame-time spike
        /// that a conditional rollback produces on the frames it fires.
        /// </remarks>
        private void RunPrediction()
        {
            RestoreTo(_confirmedTick);

            int replayed = 0;
            for (int i = 1; i <= _config.PredictionHorizon; ++i)
            {
                int tick = _confirmedTick + i;

                // Every step is preceded by exactly one restore, including steps that
                // were not rolled back to.
                //
                // PhysX warm-starts its solver from cached contact data, and moving an
                // actor throws that cache away. A step that follows a restore therefore
                // runs cold while a step that follows another step runs warm, and the
                // two do not produce the same answer. That asymmetry -- not any loss of
                // precision in the restore itself -- is what made a replayed tick differ
                // from the original.
                //
                // Restoring before every step removes the asymmetry by making every step
                // cold. The native suite measures the result directly: under PGS a
                // replayed run is then bitwise identical to a run that was never rolled
                // back, with persistent contact manifolds both enabled and disabled, and
                // without it replay diverges on the very first step.
                //
                // Under TGS, which is what SimConfig selects, transparency is not reached
                // -- substep state survives the restore and nothing exposed clears it. The
                // discipline is kept regardless: it costs nothing, it removes the largest
                // source of asymmetry, and peers agree because they run the same sequence,
                // not because replay is transparent.
                //
                // The first restore is the rewind above, so only subsequent iterations
                // restore again. Restoring twice would be as wrong as not at all:
                // quaternion normalisation is not idempotent, and a second restore
                // shifts the rotation by one unit in the last place.
                if (i > 1)
                {
                    RestoreTo(tick - 1);
                }

                StepOnce(tick, true);
                CaptureInto(tick, false);
                ++replayed;
            }

            LastReplayLength = replayed;
            TotalReplayedTicks += replayed;
            _currentTick = _confirmedTick + _config.PredictionHorizon;

            SimLog.Verbose(string.Format("Predicted {0} tick(s) from confirmed tick {1}", replayed, _confirmedTick));
        }

        /// <summary>
        /// Resimulates the prediction window up to <paramref name="windowEnd"/>, but only
        /// from the earliest tick a misprediction or a new confirmation actually disturbed.
        /// Phases 2 and 3.
        /// </summary>
        /// <remarks>
        /// The fixed horizon replays the whole window every tick to keep every peer's
        /// operation sequence the same length, which is the only safety a solver without
        /// transparent replay has. PGS has transparent replay -- restore-and-step is a pure
        /// function of the restored snapshot (§4) -- so a shorter, data-dependent rewind
        /// lands on exactly the state a full re-simulation would, and the redundant work is
        /// pure cost. This replays only the ticks whose input changed
        /// (<see cref="InputBuffer.Submit"/>'s return) or that were newly exposed above the
        /// last simulated tick, and reuses every valid snapshot below that.
        /// <para>
        /// The window end is a parameter so the same routine serves both a fixed horizon
        /// (<c>confirmed + PredictionHorizon</c>, Phase 2) and a free-running clock
        /// (<c>CurrentTick</c>, Phase 3). On entry <see cref="_currentTick"/> is the last
        /// tick that already has a snapshot; on exit it is <paramref name="windowEnd"/>.
        /// </para>
        /// <para>
        /// The confirmed timeline is advanced first, and its hashes are what peers compare,
        /// so a bug here can only smear this peer's own prediction between confirmations; it
        /// cannot desync the session. Even so this keeps the same cold-step discipline as the
        /// fixed path: one restore before every step, the first being the rewind to the tick
        /// before the replay start, and never two restores in a row.
        /// </para>
        /// </remarks>
        private void RunPredictionConditional(int windowEnd)
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
            _pendingReplayFrom = int.MaxValue;

            int replayed = 0;
            if (replayFrom <= windowEnd)
            {
                // The predecessor snapshot is valid by construction: replayFrom is the
                // earliest dirty tick, so replayFrom - 1 was not disturbed and its snapshot
                // -- confirmed when it equals _confirmedTick, otherwise a prediction from a
                // prior frame -- still holds.
                RestoreTo(replayFrom - 1);
                for (int tick = replayFrom; tick <= windowEnd; ++tick)
                {
                    // One restore before every step, exactly as RunPrediction argues: the
                    // first is the rewind above, subsequent steps re-restore so every step
                    // runs cold. Restoring twice would shift the rotation by one ULP.
                    if (tick > replayFrom)
                    {
                        RestoreTo(tick - 1);
                    }

                    StepOnce(tick, true);
                    CaptureInto(tick, false);
                    ++replayed;
                }

                _currentTick = windowEnd;
            }

            LastReplayLength = replayed;
            TotalReplayedTicks += replayed;

            SimLog.Verbose(string.Format(
                "Conditionally replayed {0} tick(s) from tick {1} to {2}", replayed, replayFrom, windowEnd));
        }

        private void RestoreTo(int tick)
        {
            Snapshot snapshot;
            if (!_snapshots.TryGet(tick, out snapshot))
            {
                // The tick fell out of history, which means the peer fell further behind
                // than SnapshotHistory allows. Nothing local can fix that; the session
                // has to resynchronise.
                throw new InvalidOperationException(string.Format(
                    "Cannot rewind to tick {0}; the oldest retained tick is {1}. This peer has fallen further " +
                    "behind than SimConfig.SnapshotHistory ({2} ticks) allows and needs a synchronised rebuild.",
                    tick, _snapshots.OldestTick, _config.SnapshotHistory));
            }

            _world.RestoreState(snapshot.Data, snapshot.Size);
            RestoreManagedFrom(snapshot);
        }

        private void StepOnce(int tick, bool isReplay)
        {
            SimLog.CurrentTick = tick;

            SimInputFrame frame = _inputs.GetOrPredict(tick);

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

        /// <summary>
        /// Discards the timeline in preparation for a synchronised rebuild.
        /// </summary>
        /// <remarks>
        /// A peer that joins mid-match has no history, and the native suite shows a world
        /// with no history cannot reproduce a running world's trace under any contact
        /// reset mode. What it can do is agree with another world that also started from
        /// the same snapshot: two worlds rebuilt that way were measured bit-identical for
        /// thirty-two ticks with their actors registered in opposite orders.
        /// <para>
        /// So the joiner does not chase the others. Every peer restores the same snapshot
        /// at the same tick, discards its history, and continues from there, which puts
        /// them all back on an identical footing. The same procedure is the recovery path
        /// when a desync is detected.
        /// </para>
        /// </remarks>
        /// <param name="resumeTick">The tick the session resumes from.</param>
        /// <param name="state">The agreed snapshot every peer restores.</param>
        /// <param name="size">How many bytes of <paramref name="state"/> are meaningful.</param>
        /// <param name="recreateWorld">
        /// When true (the default), the native world is destroyed and rebuilt from scratch
        /// before the snapshot is restored, so every peer — a mid-match joiner included —
        /// reaches the identical PhysX internal arrangement. Restoring into a world that has
        /// run a match instead is the known-incorrect path: its internal indices carry the
        /// shape of a history the joiner never saw. Pass false only for a peer that provably
        /// built its world in the same order the snapshot was captured under and has not
        /// churned its registry since, which in practice means never for a real rebuild.
        /// </param>
        public void PrepareForRebuild(int resumeTick, byte[] state, int size, bool recreateWorld = true)
        {
            if (state == null)
            {
                throw new ArgumentNullException("state");
            }
            if (resumeTick < 0)
            {
                throw new ArgumentOutOfRangeException("resumeTick", "Tick numbers start at zero.");
            }

            SimLog.Info(string.Format("Synchronised rebuild: resuming at tick {0} from a {1} byte snapshot{2}",
                resumeTick, size, recreateWorld ? ", rebuilding the native world" : ""));

            if (recreateWorld)
            {
                // Rebuild from nothing so the internal arrangement is the one every peer
                // reaches independently, then capture from the fresh world below.
                _world.RecreateNativeWorld();
            }
            else
            {
                _world.CommitPending();
            }
            _world.RestoreState(state, size);

            // Everything before the rebuild describes a timeline that no longer exists.
            _snapshots.Clear();
            _inputs.Reset(resumeTick);

            Snapshot slot = _snapshots.BeginWrite(resumeTick);
            ulong hash;
            byte[] buffer = slot.Data;
            int captured = _world.CaptureState(ref buffer, out hash);
            slot.Data = buffer;
            _snapshots.CompleteWrite(slot, captured, hash);
            // The managed channels are captured from whatever the provider currently holds,
            // so the game layer must have applied the agreed managed state to its objects
            // before calling this, exactly as it applies the agreed physics snapshot.
            CaptureManagedInto(slot);
            slot.IsConfirmed = true;

            _confirmedTick = resumeTick;
            _currentTick = resumeTick;
            _stalled = false;
            LastReplayLength = 0;
            _pendingReplayFrom = int.MaxValue;

            SimLog.Info(string.Format("Rebuild complete; state hash is 0x{0:X16}", hash));
        }
    }
}
