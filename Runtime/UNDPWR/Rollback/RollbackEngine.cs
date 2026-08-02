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
    /// <para>The measurements behind that rule, from the native suite: a peer that
    /// rewinds and resimulates does not reproduce its own earlier trace, because PhysX
    /// warm-starts its solver from contact impulses held in persistent manifolds that no
    /// public API can read or write. The error is small, around 2e-06 m over thirty
    /// ticks, but it is not zero and it compounds. No contact reset mode fixes it:
    /// replaying sixteen ticks into a world with no history reproduces zero of them with
    /// the caches left alone, zero with <c>resetFiltering</c>, and one with a full
    /// reinsert. So a peer that rewound three ticks and a peer that rewound five are
    /// simply different, and the only way to keep them identical is to have them rewind
    /// the same amount.</para>
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

        private int _confirmedTick = -1;
        private int _currentTick = -1;
        private bool _stalled;

        /// <summary>The newest tick whose inputs are final and which will not be replayed.</summary>
        public int ConfirmedTick { get { return _confirmedTick; } }

        /// <summary>The newest tick simulated, confirmed or predicted.</summary>
        public int CurrentTick { get { return _currentTick; } }

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
            _snapshots.MarkConfirmedThrough(0);

            _confirmedTick = 0;
            _currentTick = 0;

            SimLog.Info(string.Format("Initialised at tick 0; state is {0} bytes, hash 0x{1:X16}", size, hash));
        }

        /// <summary>
        /// Records a received input, which may make more ticks confirmable.
        /// </summary>
        /// <remarks>
        /// Note what this deliberately does not do: it does not trigger a rollback. The
        /// engine rewinds on a fixed schedule in <see cref="Advance"/> whether or not a
        /// misprediction happened, because rewinding only when mispredicted would make
        /// the operation sequence depend on network timing and differ between peers.
        /// </remarks>
        public void SubmitInput(SimInput input)
        {
            _inputs.Submit(input);
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
            // At most one confirmed tick per call, however many confirmations arrived.
            //
            // Draining the whole confirmed backlog in one call looks harmless and is not.
            // A confirmed tick is computed as restore(previous) then one step, and while
            // the restore is exact for everything a snapshot holds, PhysX's warm-start
            // contact impulses are not in the snapshot and carry over from whichever step
            // ran immediately before. Draining three confirmations at once puts a confirmed
            // step directly after another confirmed step; draining them one per frame puts
            // it after a prediction run instead. Same tick, same inputs, different
            // predecessor, different result. Since the number of confirmations arriving in
            // a frame is a property of the network rather than of the simulation, peers
            // would disagree about confirmed state purely because their packets clumped
            // differently.
            //
            // One per call keeps the per-frame sequence identical on every peer:
            // restore(c), step(c+1), capture, restore(c+1), then horizon prediction steps.
            // It is also sustainable, because inputs are produced at the tick rate and
            // Advance runs at the tick rate, so the steady state is exactly one.
            int newConfirmed = Math.Min(_inputs.ConfirmedThrough, _confirmedTick + 1);

            // Refuse to run further ahead than the horizon. A peer that predicted further
            // would be running a different operation sequence to everyone else, which
            // desyncs it silently; stalling instead makes the problem visible and
            // recoverable.
            int targetTick = _confirmedTick + _config.PredictionHorizon;
            if (newConfirmed <= _confirmedTick && _currentTick >= targetTick)
            {
                if (!_stalled)
                {
                    _stalled = true;
                    SimLog.Warning(string.Format(
                        "Stalled at tick {0}: confirmed only through {1}, and the fixed prediction horizon of {2} " +
                        "ticks does not allow running further ahead.",
                        _currentTick, _confirmedTick, _config.PredictionHorizon));
                }
                return false;
            }

            if (_stalled)
            {
                _stalled = false;
                SimLog.Info(string.Format("Resumed at tick {0}", _currentTick));
            }

            AdvanceConfirmed(newConfirmed);
            RunPrediction();
            return true;
        }

        /// <summary>
        /// Steps the confirmed timeline forward, by at most one tick per call.
        /// </summary>
        /// <remarks>
        /// Each confirmed tick is computed by restoring the previous confirmed snapshot
        /// and taking exactly one step, which is the same operation on every peer, from
        /// the same bytes, with the same inputs. That is what makes the resulting hash
        /// comparable bit-for-bit, and why <see cref="Advance"/> caps the caller at one
        /// tick rather than letting a backlog drain in a single frame.
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
            slot.IsConfirmed = confirmed;
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
        public void PrepareForRebuild(int resumeTick, byte[] state, int size)
        {
            if (state == null)
            {
                throw new ArgumentNullException("state");
            }
            if (resumeTick < 0)
            {
                throw new ArgumentOutOfRangeException("resumeTick", "Tick numbers start at zero.");
            }

            SimLog.Info(string.Format("Synchronised rebuild: resuming at tick {0} from a {1} byte snapshot",
                resumeTick, size));

            _world.CommitPending();
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
            slot.IsConfirmed = true;

            _confirmedTick = resumeTick;
            _currentTick = resumeTick;
            _stalled = false;
            LastReplayLength = 0;

            SimLog.Info(string.Format("Rebuild complete; state hash is 0x{0:X16}", hash));
        }
    }
}
