using NUnit.Framework;
using UNDPWR.Core;
using UNDPWR.Interop;
using UNDPWR.Rollback;

namespace UNDPWR.Tests
{
    /// <summary>
    /// Tests for the timing configuration and the input buffer: which fields a session has to
    /// agree on, and how the buffer behaves once local input is stamped ahead of the tick being
    /// simulated.
    /// </summary>
    public class SimTimingTests
    {
        // ------------------------------------------------------------- validation ----

        [Test]
        public void RecommendedConfigValidates()
        {
            string reason;
            Assert.IsTrue(SimConfig.Deterministic.Validate(out reason), reason);
        }

        [Test]
        public void NegativeLocalInputDelayIsRejected()
        {
            SimConfig config = SimConfig.Deterministic;
            config.LocalInputDelay = -1;

            string reason;
            Assert.IsFalse(config.Validate(out reason));
            StringAssert.Contains("LocalInputDelay", reason);
        }

        [Test]
        public void SnapshotHistoryMustCoverHorizonPlusDelay()
        {
            // The live window runs from the confirmed tick out to the furthest tick input has
            // been stamped for. A ring that does not span it overwrites a tick a rollback still
            // needs, so the horizon alone is not the bound to check against.
            SimConfig config = SimConfig.Deterministic;
            config.PredictionHorizon = 6;
            config.LocalInputDelay = 4;
            config.SnapshotHistory = 10;

            string reason;
            Assert.IsFalse(config.Validate(out reason), "10 does not exceed 6 + 4");
            StringAssert.Contains("SnapshotHistory", reason);

            config.SnapshotHistory = 11;
            Assert.IsTrue(config.Validate(out reason), reason);
        }

        [Test]
        public void ConditionalRollbackRequiresPgs()
        {
            // A data-dependent rewind depth only lands where a full re-simulation would when
            // replay is bitwise transparent, which §4 found for PGS alone. Under TGS the flag
            // is a silent desync, so it must be refused at validation rather than discovered
            // in play.
            SimConfig config = SimConfig.Deterministic;
            config.ConditionalRollback = true;
            config.Solver = SimSolverType.TemporalGaussSeidel;

            string reason;
            Assert.IsFalse(config.Validate(out reason));
            StringAssert.Contains("ConditionalRollback", reason);

            config.Solver = SimSolverType.ProjectedGaussSeidel;
            Assert.IsTrue(config.Validate(out reason), reason);
        }

        [Test]
        public void FreeRunningClockRequiresConditionalRollback()
        {
            // A free-running clock runs a different-length window every frame, which only
            // agrees on confirmed state because replay is transparent -- the same property
            // conditional rollback rests on. It cannot be enabled without it.
            SimConfig config = SimConfig.Deterministic;
            config.FreeRunningClock = true;
            config.ConditionalRollback = false;

            string reason;
            Assert.IsFalse(config.Validate(out reason));
            StringAssert.Contains("FreeRunningClock", reason);

            config.ConditionalRollback = true;
            Assert.IsTrue(config.Validate(out reason), reason);
        }

        // ------------------------------------------------------------------ hash ----

        [Test]
        public void PredictionHorizonIsHashed()
        {
            // The horizon is the length of every peer's per-frame operation sequence, so a
            // mismatch has to be caught at join rather than diagnosed as a desync later.
            SimConfig a = SimConfig.Deterministic;
            SimConfig b = SimConfig.Deterministic;
            b.PredictionHorizon = a.PredictionHorizon + 1;

            Assert.AreNotEqual(a.ComputeHash(), b.ComputeHash());
        }

        [Test]
        public void SolverTypeIsHashed()
        {
            // PGS and TGS do not produce the same simulation, and the difference is not
            // one a peer would notice from its own results, so a mixed session has to be
            // refused at join rather than discovered as a desync.
            SimConfig a = SimConfig.Deterministic;
            SimConfig b = SimConfig.Deterministic;
            b.Solver = SimSolverType.TemporalGaussSeidel;

            Assert.AreEqual(SimSolverType.ProjectedGaussSeidel, a.Solver,
                "PGS is the default after the Phase 1 solver decision");
            Assert.AreNotEqual(a.ComputeHash(), b.ComputeHash());
        }

        [Test]
        public void SolverTypeReachesTheSceneDescriptor()
        {
            // The value PhysX receives is PxSolverType, so the enum's numbering is part of
            // the interop contract rather than an internal detail.
            SimConfig config = SimConfig.Deterministic;

            config.Solver = SimSolverType.ProjectedGaussSeidel;
            Assert.AreEqual(0, config.ToSceneDesc().SolverType);

            config.Solver = SimSolverType.TemporalGaussSeidel;
            Assert.AreEqual(1, config.ToSceneDesc().SolverType);
        }

        [Test]
        public void SceneFlagsMatchTheNativeHeader()
        {
            // Hand-transcribed from pxw::PxwSceneFlag in DataInterop.h. Every one of these is a
            // raw bit the native side switches on, so a member declared in the wrong position
            // builds a scene nobody asked for, in silence: it compiles, and both peers compute
            // the same wrong number so no config hash check catches it either. This shipped once
            // with EnhancedDeterminism at bit 0, which meant no session ever actually enabled
            // enhanced determinism and every session enabled CCD instead.
            Assert.AreEqual(1u << 0, (uint)SimSceneFlags.EnablePcm);
            Assert.AreEqual(1u << 1, (uint)SimSceneFlags.EnableCcd);
            Assert.AreEqual(1u << 2, (uint)SimSceneFlags.EnableStabilization);
            Assert.AreEqual(1u << 3, (uint)SimSceneFlags.EnableActiveActors);
            Assert.AreEqual(1u << 4, (uint)SimSceneFlags.EnhancedDeterminism);
            Assert.AreEqual(1u << 5, (uint)SimSceneFlags.EnableDirectGpuApi);
            Assert.AreEqual(1u << 6, (uint)SimSceneFlags.DisablePvd);
            Assert.AreEqual(1u << 7, (uint)SimSceneFlags.EnableContactEvents);
        }

        [Test]
        public void DeterministicPresetAsksForEnhancedDeterminismAndNotCcd()
        {
            // Enhanced determinism is what makes a result independent of the order PhysX
            // happens to visit actors and islands in, which is the whole basis of two peers
            // agreeing. Asserted on the descriptor rather than the enum so that a future
            // reshuffle of either side is caught by the flags a session actually sends.
            uint flags = SimConfig.Deterministic.ToSceneDesc().Flags;

            Assert.AreNotEqual(0u, flags & (uint)SimSceneFlags.EnhancedDeterminism,
                "every UNDPWR preset must enable enhanced determinism");
            Assert.AreNotEqual(0u, flags & (uint)SimSceneFlags.EnablePcm);
            Assert.AreNotEqual(0u, flags & (uint)SimSceneFlags.DisablePvd);
            Assert.AreEqual(0u, flags & (uint)SimSceneFlags.EnableCcd,
                "CCD varies contact generation with velocity history, which a restore cannot carry");
            Assert.AreEqual(0u, flags & (uint)SimSceneFlags.EnableStabilization);
        }

        [Test]
        public void PeerLocalTimingFieldsAreNotHashed()
        {
            // An input carries the tick it applies to and is applied at that tick whenever it
            // arrives, so the delay changes when a peer produces input, never what the
            // simulation does with it. Retaining more history changes nothing at all. Hashing
            // either would reject a session over a difference that cannot desync it.
            SimConfig a = SimConfig.Deterministic;
            SimConfig b = SimConfig.Deterministic;
            b.LocalInputDelay = a.LocalInputDelay + 3;
            b.SnapshotHistory = a.SnapshotHistory * 2;

            Assert.AreEqual(a.ComputeHash(), b.ComputeHash());
        }

        [Test]
        public void FreeRunningClockIsHashed()
        {
            // It decides whether PredictionHorizon is hashed, so two peers have to agree on
            // it or their hashes would rest on different field sets. A mixed session is a
            // clean rejection at join.
            SimConfig a = SimConfig.Deterministic;
            SimConfig b = SimConfig.Deterministic;
            b.ConditionalRollback = true;
            b.FreeRunningClock = true;

            Assert.AreNotEqual(a.ComputeHash(), b.ComputeHash());
        }

        [Test]
        public void PredictionHorizonLeavesTheHashWhenFreeRunning()
        {
            // Once the clock is free the horizon is only a peer-local target lead, so two
            // free-running peers that chose different leads must still agree at join.
            SimConfig a = SimConfig.Deterministic;
            a.ConditionalRollback = true;
            a.FreeRunningClock = true;

            SimConfig b = a.Clone();
            b.PredictionHorizon = a.PredictionHorizon + 3;

            Assert.AreEqual(a.ComputeHash(), b.ComputeHash());
        }

        [Test]
        public void ConditionalRollbackIsNotHashed()
        {
            // Because the confirmed timeline is advanced by a cold restore-and-step per tick
            // whatever this flag is, and PGS makes that a pure function of the predecessor
            // snapshot, a peer running conditional rollback and one running the fixed horizon
            // agree on every confirmed hash. It only changes how much prediction each redoes,
            // so hashing it would reject a session over a difference that cannot desync it.
            SimConfig a = SimConfig.Deterministic;
            SimConfig b = SimConfig.Deterministic;
            b.ConditionalRollback = !a.ConditionalRollback;

            Assert.AreEqual(a.ComputeHash(), b.ComputeHash());
        }

        // ---------------------------------------------------------- input buffer ----

        private static SimInput Command(uint playerId, int tick, uint buttons)
        {
            SimInput input = SimInput.Neutral(playerId, tick);
            input.Buttons = buttons;
            return input;
        }

        [Test]
        public void FrontierAdvancesOnlyWhenEveryPlayerHasSubmitted()
        {
            var buffer = new InputBuffer(new uint[] { 7, 3 }, 32);

            buffer.Submit(Command(3, 0, 1));
            Assert.AreEqual(-1, buffer.ConfirmedThrough, "one player is not everyone");

            buffer.Submit(Command(7, 0, 1));
            Assert.AreEqual(0, buffer.ConfirmedThrough);
        }

        [Test]
        public void InputStampedAheadDoesNotAdvanceTheFrontierPastTheGap()
        {
            // What local input delay actually does to the buffer: inputs land further ahead of
            // the confirmed frontier than they used to, leaving a run of ticks that are known
            // at the far end and empty in the middle. The frontier must stop at the gap.
            var buffer = new InputBuffer(new uint[] { 1, 2 }, 32);

            for (int tick = 0; tick <= 5; ++tick)
            {
                buffer.Submit(Command(1, tick, 0));
                buffer.Submit(Command(2, tick, 0));
            }
            Assert.AreEqual(5, buffer.ConfirmedThrough);

            buffer.Submit(Command(1, 8, 0));
            buffer.Submit(Command(2, 8, 0));
            Assert.AreEqual(5, buffer.ConfirmedThrough, "ticks 6 and 7 are still missing");

            buffer.Submit(Command(1, 6, 0));
            buffer.Submit(Command(2, 6, 0));
            buffer.Submit(Command(1, 7, 0));
            buffer.Submit(Command(2, 7, 0));
            Assert.AreEqual(8, buffer.ConfirmedThrough, "the gap closed, so the frontier reaches 8");
        }

        [Test]
        public void InputArrivingBeforeItsTickIsNeverMispredicted()
        {
            // The whole point of the delay. An input stamped far enough ahead is already in the
            // buffer when its tick comes up, so Submit reports no misprediction and the engine
            // has nothing to correct.
            var buffer = new InputBuffer(new uint[] { 1, 2 }, 32);

            buffer.Submit(Command(1, 0, 0));
            buffer.Submit(Command(2, 0, 0));

            // Player 2 changes command at tick 4 and it arrives while the frontier is at 0.
            Assert.AreEqual(-1, buffer.Submit(Command(2, 4, 0xFF)), "arrived before anyone guessed");

            SimInputFrame frame = buffer.GetOrPredict(4);
            Assert.AreEqual(0xFFu, frame[buffer.SlotOf(2)].Buttons);
            Assert.IsFalse(frame[buffer.SlotOf(2)].IsPredicted);
        }

        [Test]
        public void InputArrivingAfterItsTickWasGuessedReportsTheMisprediction()
        {
            // The case the delay is meant to make rare, kept as the contrast: once a tick has
            // been predicted, a command that differs from the guess has to be reported.
            var buffer = new InputBuffer(new uint[] { 1, 2 }, 32);

            buffer.Submit(Command(1, 0, 0));
            buffer.Submit(Command(2, 0, 0));

            SimInputFrame predicted = buffer.GetOrPredict(4);
            Assert.IsTrue(predicted[buffer.SlotOf(2)].IsPredicted);
            Assert.AreEqual(0u, predicted[buffer.SlotOf(2)].Buttons, "the guess repeats the last command");

            Assert.AreEqual(4, buffer.Submit(Command(2, 4, 0xFF)), "the guess was wrong at tick 4");
        }

        // ------------------------------------------------- local stream contiguity ----
        //
        // The buffer's own rule -- the frontier stops at the first tick that is not complete --
        // means a hole in one player's stream is not a hiccup but a permanent stall. These pin
        // the two ways stamping against RollbackEngine.LocalInputTick opens one, and the rule
        // SimSession.SubmitLocalInput follows to close them. They model the stamping pattern
        // rather than driving an engine, so they stay native-free like the rest of the suite.

        /// <summary>Submits one tick for the remote player, so only the local stream is at issue.</summary>
        private static void SubmitRemote(InputBuffer buffer, int tick)
        {
            buffer.Submit(Command(2, tick, 0));
        }

        [Test]
        public void FrontierNeverLeavesTheStartWhenTheDelayHoleIsNotFilled()
        {
            // Trap one: local input stamped LocalInputDelay ahead means nothing ever covers the
            // delay ticks the session starts at. Every later tick arriving changes nothing,
            // which is what makes this so hard to read in a log -- input is clearly flowing and
            // the frontier has still never moved.
            const int delay = 2;
            var buffer = new InputBuffer(new uint[] { 1, 2 }, 32);

            for (int clock = 0; clock <= 20; ++clock)
            {
                buffer.Submit(Command(1, clock + delay, 0));
                SubmitRemote(buffer, clock);
            }

            Assert.AreEqual(-1, buffer.ConfirmedThrough,
                "ticks 0 and 1 were never stamped by player 1, so nothing can ever confirm");
        }

        [Test]
        public void FrontierStopsWhereTheClockJumpedAndNothingFilledTheGap()
        {
            // Trap two: the clock does not always move one tick per frame. A fixed-horizon
            // engine's first Advance moves it from the confirmed tick to the end of the
            // prediction window in one go, so the tick to stamp jumps with it. Here the front
            // hole is primed correctly and the stream still breaks, at the jump instead.
            const int delay = 2;
            const int horizon = 6;
            var buffer = new InputBuffer(new uint[] { 1, 2 }, 32);

            for (int tick = 0; tick < delay; ++tick)
            {
                buffer.Submit(Command(1, tick, 0));
                SubmitRemote(buffer, tick);
            }

            // Frame one stamps for the clock at rest, then the clock jumps a whole horizon.
            buffer.Submit(Command(1, 0 + delay, 0));
            SubmitRemote(buffer, delay);
            Assert.AreEqual(delay, buffer.ConfirmedThrough, "contiguous so far");

            for (int clock = horizon; clock <= horizon + 10; ++clock)
            {
                buffer.Submit(Command(1, clock + delay, 0));
                SubmitRemote(buffer, clock);
            }

            Assert.AreEqual(delay, buffer.ConfirmedThrough,
                "ticks 3 to 7 were skipped by the jump, and the frontier cannot cross them");
        }

        [Test]
        public void FillingEveryTickUpToTheStampKeepsTheFrontierMoving()
        {
            // The rule SimSession.SubmitLocalInput applies: copy the current sample across every
            // tick from the last one submitted through the one being stamped. It closes both
            // traps above with the same line of code, because both are the same thing -- a tick
            // the local player never spoke for.
            const int delay = 2;
            const int horizon = 6;
            var buffer = new InputBuffer(new uint[] { 1, 2 }, 32);

            int nextLocal = 0;
            int highestClock = -1;

            // The same clock the test above uses: at rest, then a horizon-wide jump, then one
            // tick per frame.
            int[] clocks = { 0, horizon, horizon + 1, horizon + 2, horizon + 3, horizon + 4 };
            foreach (int clock in clocks)
            {
                for (; nextLocal <= clock + delay; ++nextLocal)
                {
                    buffer.Submit(Command(1, nextLocal, 0));
                }
                for (; highestClock < clock; ++highestClock)
                {
                    SubmitRemote(buffer, highestClock + 1);
                }
            }

            Assert.AreEqual(horizon + 4, buffer.ConfirmedThrough,
                "every tick through the newest one both players covered is confirmable");
        }
    }
}
