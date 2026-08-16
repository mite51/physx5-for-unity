using NUnit.Framework;
using UNDPWR.Core;
using UNDPWR.Net;
using UNDPWR.Rollback;

namespace UNDPWR.Tests
{
    public class SimTimingTests
    {
        private static SimInput Command(uint playerId, int tick, uint buttons)
        {
            SimInput input = SimInput.Neutral(playerId, tick);
            input.Buttons = buttons;
            return input;
        }

        [Test]
        public void AuthoritativeDefaultsValidate()
        {
            SimConfig simulation = SimConfig.Deterministic;
            SimNetConfig network = SimNetConfig.Authoritative;
            string reason;
            Assert.IsTrue(simulation.Validate(out reason), reason);
            Assert.IsTrue(network.Validate(simulation, out reason), reason);
            Assert.AreEqual(64, simulation.SnapshotHistory);
            Assert.AreEqual(3, network.InitialInputLead);
            Assert.AreEqual(8, network.MaxSimulationStepsPerFrame);
        }

        [Test]
        public void AdaptiveLeadMayNotRestoreZeroDelayLegacyMode()
        {
            SimConfig simulation = SimConfig.Deterministic;
            SimNetConfig network = SimNetConfig.Authoritative;
            network.MinimumInputLead = 0;
            string reason;
            Assert.IsFalse(network.Validate(simulation, out reason));
            StringAssert.Contains("minimum", reason.ToLowerInvariant());
        }

        [Test]
        public void SnapshotHistoryMustCoverLeadAndHardResyncWindow()
        {
            SimConfig simulation = SimConfig.Deterministic;
            SimNetConfig network = SimNetConfig.Authoritative;
            simulation.SnapshotHistory =
                network.HardResyncTicks + network.MaximumInputLead + 1;
            string reason;
            Assert.IsFalse(network.Validate(simulation, out reason));
            StringAssert.Contains("SnapshotHistory", reason);
        }

        [Test]
        public void GpuRemainsAvailableButAuthoritativeNetworkingRejectsIt()
        {
            SimConfig simulation = SimConfig.Deterministic;
            simulation.Backend = SimBackendMode.GpuExperimental;
            string reason;
            Assert.IsTrue(simulation.Validate(out reason), reason);
            Assert.IsFalse(SimNetConfig.Authoritative.Validate(simulation, out reason));
            StringAssert.Contains("CPU", reason);
        }

        [Test]
        public void SpeculativeInputDoesNotAdvanceAuthority()
        {
            InputBuffer buffer = new InputBuffer(new uint[] { 1, 2 }, 64);
            buffer.SubmitSpeculative(Command(1, 1, 7), 1);
            Assert.AreEqual(-1, buffer.ConfirmedThrough);

            SimInputFrame frame = buffer.GetOrPredict(1);
            Assert.AreEqual(SimInputProvenance.Speculative, frame[buffer.SlotOf(1)].Provenance);
            Assert.AreEqual(SimInputProvenance.Predicted, frame[buffer.SlotOf(2)].Provenance);
        }

        [Test]
        public void CanonicalFrameAdvancesOnlyAnUnbrokenAuthoritativeFrontier()
        {
            InputBuffer buffer = new InputBuffer(new uint[] { 1, 2 }, 64);
            SimInput[] tickZero =
            {
                Command(1, 0, 1),
                Command(2, 0, 2)
            };
            buffer.SubmitAuthoritativeFrame(0, tickZero, new uint[] { 1, 1 });
            Assert.AreEqual(0, buffer.ConfirmedThrough);

            SimInput[] tickTwo =
            {
                Command(1, 2, 3),
                Command(2, 2, 4)
            };
            buffer.SubmitAuthoritativeFrame(2, tickTwo, new uint[] { 2, 2 });
            Assert.AreEqual(0, buffer.ConfirmedThrough, "tick one is still absent");
        }

        [Test]
        public void CanonicalTimelineContinuesAfterAnAlreadyConfirmedBaseline()
        {
            InputBuffer buffer = new InputBuffer(new uint[] { 1 }, 64);
            buffer.ResetAfterConfirmed(10);
            SimInput[] inputs = { Command(1, 11, 3) };
            buffer.SubmitAuthoritativeFrame(11, inputs, new uint[] { 1 });
            Assert.AreEqual(11, buffer.ConfirmedThrough);
        }

        [Test]
        public void MatchingCanonicalInputPromotesSpeculationWithoutMispredict()
        {
            InputBuffer buffer = new InputBuffer(new uint[] { 1 }, 64);
            SimInput input = Command(1, 3, 0xFF);
            buffer.SubmitSpeculative(input, 9);
            buffer.GetOrPredict(3);
            Assert.AreEqual(-1, buffer.SubmitAuthoritative(input, 9));
            Assert.AreEqual(SimInputProvenance.Authoritative, buffer.GetOrPredict(3)[0].Provenance);
        }

        [Test]
        public void RetimedSpeculationDirtiesTheRequestedTick()
        {
            InputBuffer buffer = new InputBuffer(new uint[] { 1 }, 64);
            SimInput input = Command(1, 3, 0xFF);
            buffer.SubmitSpeculative(input, 9);
            buffer.GetOrPredict(3);

            Assert.AreEqual(3, buffer.RetimeSpeculative(1, 9, 3, 5));
            Assert.AreEqual(0u, buffer.GetOrPredict(3)[0].Buttons);
            Assert.AreEqual(0xFFu, buffer.GetOrPredict(5)[0].Buttons);
            Assert.AreEqual(SimInputProvenance.Speculative, buffer.GetOrPredict(5)[0].Provenance);
        }

        [Test]
        public void SchedulerRetimesPastInputAndNeverRewritesFinalizedTick()
        {
            SimInputScheduler scheduler = new SimInputScheduler(new uint[] { 1 }, 10, 12);
            SimInputProposal proposal = new SimInputProposal();
            proposal.Sequence = 1;
            proposal.RequestedTick = 9;
            proposal.Input = Command(1, 9, 7);

            SimInputDecision decision = scheduler.Submit(1, ref proposal);
            Assert.AreEqual(SimAdmissionDisposition.Retimed, decision.Disposition);
            Assert.AreEqual(11, decision.AssignedTick);

            SimCanonicalFrame frame = scheduler.FinalizeNextFrame(1);
            Assert.AreEqual(11, frame.Tick);
            Assert.AreEqual(7u, frame.Inputs[0].Input.Buttons);
        }

        [Test]
        public void SchedulerMakesRedundantProposalIdempotent()
        {
            SimInputScheduler scheduler = new SimInputScheduler(new uint[] { 1 }, 0, 12);
            SimInputProposal proposal = new SimInputProposal();
            proposal.Sequence = 1;
            proposal.RequestedTick = 3;
            proposal.Input = Command(1, 3, 7);

            SimInputDecision first = scheduler.Submit(1, ref proposal);
            SimInputDecision duplicate = scheduler.Submit(1, ref proposal);
            Assert.AreEqual(first.Disposition, duplicate.Disposition);
            Assert.AreEqual(first.AssignedTick, duplicate.AssignedTick);
        }

        [Test]
        public void SchedulerNeverReadmitsARejectedSequence()
        {
            SimInputScheduler scheduler = new SimInputScheduler(new uint[] { 1 }, 0, 2);
            SimInputProposal proposal = new SimInputProposal
            {
                Sequence = 1,
                RequestedTick = 5,
                Input = Command(1, 5, 7)
            };

            SimInputDecision rejected = scheduler.Submit(1, ref proposal);
            Assert.AreEqual(SimAdmissionDisposition.Rejected, rejected.Disposition);
            Assert.AreEqual(SimAdmissionRejection.TooFarInFuture, rejected.Rejection);

            scheduler.FinalizeNextFrame(1);
            scheduler.FinalizeNextFrame(1);
            scheduler.FinalizeNextFrame(1);
            SimInputDecision duplicate = scheduler.Submit(1, ref proposal);
            Assert.AreEqual(SimAdmissionDisposition.Rejected, duplicate.Disposition);
            Assert.AreEqual(SimAdmissionRejection.TooFarInFuture, duplicate.Rejection);
            Assert.AreEqual(-1, duplicate.AssignedTick);
        }

        [Test]
        public void SchedulerAcceptsRecentProposalsThatArriveOutOfOrder()
        {
            SimInputScheduler scheduler = new SimInputScheduler(new uint[] { 1 }, 0, 12);
            SimInputProposal second = new SimInputProposal
            {
                Sequence = 2, RequestedTick = 4, Input = Command(1, 4, 2)
            };
            SimInputProposal first = new SimInputProposal
            {
                Sequence = 1, RequestedTick = 3, Input = Command(1, 3, 1)
            };

            Assert.AreEqual(
                SimAdmissionDisposition.Accepted, scheduler.Submit(1, ref second).Disposition);
            Assert.AreEqual(
                SimAdmissionDisposition.Accepted, scheduler.Submit(1, ref first).Disposition);
        }

        [Test]
        public void AuthoritativeEventsAreOrderedAndSurviveRebuildBeforeTheirTick()
        {
            SimAuthoritativeEventBuffer buffer = new SimAuthoritativeEventBuffer(64);
            SimAuthoritativeEvent second = new SimAuthoritativeEvent
            {
                PlayerId = 2, Sequence = 1, Tick = 20, TypeId = 0, Payload = new byte[0]
            };
            SimAuthoritativeEvent first = new SimAuthoritativeEvent
            {
                PlayerId = 1, Sequence = 3, Tick = 20, TypeId = 0, Payload = new byte[0]
            };
            Assert.IsTrue(buffer.Submit(second));
            Assert.IsTrue(buffer.Submit(first));
            Assert.IsFalse(buffer.Submit(first), "reliable duplicates must be idempotent");

            buffer.DiscardThrough(10);
            Assert.AreEqual(2, buffer.Get(20).Count);
            Assert.AreEqual(1u, buffer.Get(20)[0].PlayerId);

            SimAuthoritativeEvent[] pending = buffer.CopyAfter(10);
            SimAuthoritativeEventBuffer restored = new SimAuthoritativeEventBuffer(64);
            restored.ResetAfterConfirmed(10, pending);
            Assert.AreEqual(2, restored.Get(20).Count);
        }
    }
}
