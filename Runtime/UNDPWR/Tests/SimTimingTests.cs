using NUnit.Framework;
using UNDPWR.Core;
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
            b.Solver = SimSolverType.ProjectedGaussSeidel;

            Assert.AreEqual(SimSolverType.TemporalGaussSeidel, a.Solver, "TGS is still the default");
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
    }
}
