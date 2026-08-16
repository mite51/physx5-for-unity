using NUnit.Framework;
using UnityEngine;
using UNDPWR.Core;
using UNDPWR.Gameplay;
using UNDPWR.Rollback;

namespace UNDPWR.Tests
{
    /// <summary>
    /// Determinism tests for the gameplay pieces that can be exercised without a native world:
    /// the input encoder's round trip, the action queue's channel serialization, and the phase
    /// machine.
    /// </summary>
    public class SimGameplayTests
    {
        [Test]
        public void QuantizeDequantizeIsStableUnderReencoding()
        {
            // The property that keeps local and remote peers in step: dequantizing then
            // requantizing must land on the same byte, so the value the sender simulates from
            // reproduces the byte the receiver decoded.
            for (int q = -127; q <= 127; ++q)
            {
                float value = SimInputEncoder.Dequantize((sbyte)q);
                sbyte requantized = SimInputEncoder.Quantize(value);
                Assert.AreEqual((sbyte)q, requantized, "byte {0} did not survive a round trip", q);
            }
        }

        [Test]
        public void EncodeMovementIsIdempotent()
        {
            // Encoding an already-encoded direction must not move it, or every rollback replay
            // would drift the value a little further.
            var frame = new SimWorldSpaceInputFrame();
            Vector2 once = SimInputEncoder.EncodeMovement(new Vector2(0.6f, 0.9f), frame);
            Vector2 twice = SimInputEncoder.EncodeMovement(once, frame);
            Assert.AreEqual(once.x, twice.x, 1e-6f);
            Assert.AreEqual(once.y, twice.y, 1e-6f);
        }

        [Test]
        public void EncodeMovementClampsDiagonalMagnitude()
        {
            var frame = new SimWorldSpaceInputFrame();
            Vector2 move = SimInputEncoder.EncodeMovement(new Vector2(1f, 1f), frame);
            float magnitude = new Vector2(move.x, move.y).magnitude;
            Assert.LessOrEqual(magnitude, 1.01f, "a diagonal must not exceed unit length");
        }

        [Test]
        // SimFixedInputFrame builds its rotation with Quaternion.Euler, which is a native
        // internal call. The standalone runner in Tests~/Managed cannot execute it and
        // skips this category; Unity's Test Runner runs it normally.
        [Category("RequiresUnityRuntime")]
        public void FixedFrameRotatesInput()
        {
            // A 90-degree fixed frame turns "forward" into world +X.
            var frame = new SimFixedInputFrame(90f);
            Vector2 move = SimInputEncoder.EncodeMovement(new Vector2(0f, 1f), frame);
            Assert.Greater(move.x, 0.9f);
            Assert.Less(Mathf.Abs(move.y), 0.1f);
        }

        [Test]
        public void ActionQueueRunsDueActionsInSubmissionOrder()
        {
            var queue = new SimActionQueue();
            var log = new System.Collections.Generic.List<int>();
            queue.Submit(new RecordAction(log, 1), 0);
            queue.Submit(new RecordAction(log, 2), 0);
            queue.Submit(new RecordAction(log, 3), 5); // future, not yet due

            queue.ExecuteDue(0, null);

            Assert.AreEqual(2, log.Count);
            Assert.AreEqual(1, log[0]);
            Assert.AreEqual(2, log[1]);
            Assert.AreEqual(1, queue.PendingCount, "the future action must remain queued");
        }

        [Test]
        public void ActionQueueCaptureRestoreRoundTripsFutureActions()
        {
            var queue = new SimActionQueue();
            queue.RegisterActionType<DespawnAction>(() => new DespawnAction());
            queue.Submit(new DespawnAction(4242u), 100);

            var writer = new SimStateWriter(null);
            queue.CaptureState(ref writer);
            ulong hashBefore = writer.Hash;

            var restored = new SimActionQueue();
            restored.RegisterActionType<DespawnAction>(() => new DespawnAction());
            var reader = new SimStateReader(writer.Buffer, writer.Position);
            restored.RestoreState(ref reader);

            Assert.AreEqual(1, restored.PendingCount);

            var recapture = new SimStateWriter(null);
            restored.CaptureState(ref recapture);
            Assert.AreEqual(hashBefore, recapture.Hash, "the action log drifted across a restore");
        }

        [Test]
        public void NetworkActionCodecUsesTheRegisteredTypeId()
        {
            SimActionQueue sender = new SimActionQueue();
            sender.RegisterActionType<DespawnAction>(() => new DespawnAction());
            ushort typeId;
            byte[] payload;
            sender.EncodeNetworkAction(new DespawnAction(99), out typeId, out payload);

            SimActionQueue receiver = new SimActionQueue();
            receiver.RegisterActionType<DespawnAction>(() => new DespawnAction());
            receiver.SubmitNetworkAction(typeId, payload, 20);

            SimStateWriter writer = new SimStateWriter(null);
            receiver.CaptureState(ref writer);
            Assert.AreEqual(1, receiver.PendingCount);
            Assert.Greater(writer.Position, 0);
        }

        [Test]
        public void PhaseMachineCaptureRestoreRoundTrips()
        {
            var machine = new SimPhaseMachine<Phase>(Phase.Warmup);
            machine.TransitionTo(Phase.Playing, 30);
            Assert.AreEqual(Phase.Playing, machine.Phase);
            Assert.AreEqual(10, machine.TicksInPhase(40));

            var writer = new SimStateWriter(null);
            machine.Capture(ref writer);

            var restored = new SimPhaseMachine<Phase>(Phase.Warmup);
            var reader = new SimStateReader(writer.Buffer, writer.Position);
            restored.Restore(ref reader);

            Assert.AreEqual(Phase.Playing, restored.Phase);
            Assert.AreEqual(30, restored.EnteredTick);
        }

        private enum Phase
        {
            Warmup = 0,
            Playing = 1,
            Scored = 2
        }

        private sealed class RecordAction : ISimAction
        {
            private readonly System.Collections.Generic.List<int> _log;
            private readonly int _value;

            public RecordAction(System.Collections.Generic.List<int> log, int value)
            {
                _log = log;
                _value = value;
            }

            public void Execute(SimContext context) { _log.Add(_value); }
            public void Serialize(ref SimStateWriter writer) { writer.WriteInt(_value); }
            public void Deserialize(ref SimStateReader reader) { }
        }
    }
}
