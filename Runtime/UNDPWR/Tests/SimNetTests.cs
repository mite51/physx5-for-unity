using System;
using System.Collections.Generic;
using NUnit.Framework;
using UNDPWR.Diagnostics;
using UNDPWR.Net;
using UNDPWR.Rollback;

namespace UNDPWR.Tests
{
    /// <summary>
    /// Tests for the transport layer's pure-managed pieces: the wire codec, the loopback
    /// network, and the desync detector. The full <see cref="SimSession"/> loop needs a native
    /// world and is exercised by the multi-peer harness that can reach it.
    /// </summary>
    public class SimNetTests
    {
        private SimLog.Verbosity _savedLevel;

        [SetUp]
        public void Silence()
        {
            // The detector logs a desync through SimLog, which routes to UnityEngine.Debug and
            // cannot run outside the editor. Silencing it lets the detector's logic be checked
            // here; the log line itself is exercised from Unity's Test Runner.
            _savedLevel = SimLog.Level;
            SimLog.Level = SimLog.Verbosity.Silent;
        }

        [TearDown]
        public void Restore()
        {
            SimLog.Level = _savedLevel;
        }

        // ------------------------------------------------------------ wire codec ----

        [Test]
        public void InputRoundTripsBitExact()
        {
            SimInput input = new SimInput();
            input.PlayerId = 0x10000007u;
            input.Tick = 4242;
            input.Buttons = 0xDEADBEEFu;
            input.AxisX = 0.1234567f;
            input.AxisY = -0.9876543f;
            input.AxisZ = float.MaxValue;
            input.AxisW = -3.4e-30f;

            SimByteWriter writer = new SimByteWriter(SimInputCodec.Size);
            SimInputCodec.Write(ref writer, input);
            byte[] bytes = writer.ToArray();
            Assert.AreEqual(SimInputCodec.Size, bytes.Length);

            SimByteReader reader = new SimByteReader(bytes, 0, bytes.Length);
            SimInput restored = SimInputCodec.Read(ref reader);

            Assert.AreEqual(input.PlayerId, restored.PlayerId);
            Assert.AreEqual(input.Tick, restored.Tick);
            Assert.AreEqual(input.Buttons, restored.Buttons);
            // Bit-exact, not approximate: the sender simulates the value it puts on the wire.
            Assert.AreEqual(SimFloatBitsOf(input.AxisX), SimFloatBitsOf(restored.AxisX));
            Assert.AreEqual(SimFloatBitsOf(input.AxisY), SimFloatBitsOf(restored.AxisY));
            Assert.AreEqual(SimFloatBitsOf(input.AxisZ), SimFloatBitsOf(restored.AxisZ));
            Assert.AreEqual(SimFloatBitsOf(input.AxisW), SimFloatBitsOf(restored.AxisW));
        }

        [Test]
        public void PrimitivesRoundTripLittleEndian()
        {
            SimByteWriter writer = new SimByteWriter(4);
            writer.WriteByte(0xAB);
            writer.WriteUInt16(0x1234);
            writer.WriteUInt32(0x89ABCDEFu);
            writer.WriteInt32(-123456);
            writer.WriteUInt64(0x0123456789ABCDEFul);
            byte[] bytes = writer.ToArray();

            // Little-endian regardless of host: the low byte of the u16 comes first.
            Assert.AreEqual(0xAB, bytes[0]);
            Assert.AreEqual(0x34, bytes[1]);
            Assert.AreEqual(0x12, bytes[2]);

            SimByteReader reader = new SimByteReader(bytes, 0, bytes.Length);
            Assert.AreEqual(0xAB, reader.ReadByte());
            Assert.AreEqual(0x1234, reader.ReadUInt16());
            Assert.AreEqual(0x89ABCDEFu, reader.ReadUInt32());
            Assert.AreEqual(-123456, reader.ReadInt32());
            Assert.AreEqual(0x0123456789ABCDEFul, reader.ReadUInt64());
        }

        [Test]
        public void ReadingPastEndThrows()
        {
            byte[] bytes = new byte[2];
            SimByteReader reader = new SimByteReader(bytes, 0, bytes.Length);
            reader.ReadUInt16();
            Assert.Throws<SimWireFormatException>(() => reader.ReadByte());
        }

        // ------------------------------------------------------- loopback network ----

        [Test]
        public void BroadcastReachesEveryPeerButTheSender()
        {
            SimLoopbackNetwork network = new SimLoopbackNetwork();
            ISimTransport a = network.CreateEndpoint();
            ISimTransport b = network.CreateEndpoint();
            ISimTransport c = network.CreateEndpoint();

            byte[] payload = { 1, 2, 3 };
            a.Broadcast(payload, 0, payload.Length);
            network.Step();

            ArraySegment<byte> message;
            Assert.IsFalse(a.TryReceive(out message), "sender must not receive its own broadcast");
            Assert.IsTrue(b.TryReceive(out message));
            Assert.AreEqual(3, message.Count);
            Assert.IsTrue(c.TryReceive(out message));
        }

        [Test]
        public void LatencyHoldsMessagesUntilDue()
        {
            SimLoopbackNetwork network = new SimLoopbackNetwork();
            network.Latency = 3;
            ISimTransport a = network.CreateEndpoint();
            ISimTransport b = network.CreateEndpoint();

            byte[] payload = { 9 };
            a.Broadcast(payload, 0, payload.Length);

            ArraySegment<byte> message;
            network.Step();
            Assert.IsFalse(b.TryReceive(out message), "1 of 3 steps");
            network.Step();
            Assert.IsFalse(b.TryReceive(out message), "2 of 3 steps");
            network.Step();
            Assert.IsTrue(b.TryReceive(out message), "delivered on the third step");
            Assert.AreEqual(9, message.Array[message.Offset]);
        }

        // -------------------------------------------------------- desync detector ----

        [Test]
        public void MatchingHashesRaiseNothing()
        {
            SimDesyncDetector detector = new SimDesyncDetector();
            int reports = 0;
            detector.DesyncDetected += _ => reports++;

            detector.RecordLocal(10, 0xABCDul);
            detector.RecordPeer(7u, 10, 0xABCDul);

            Assert.AreEqual(0, reports);
            Assert.AreEqual(0, detector.DesyncCount);
        }

        [Test]
        public void MismatchIsReportedWhenPeerArrivesAfterLocal()
        {
            SimDesyncDetector detector = new SimDesyncDetector();
            SimDesyncReport captured = new SimDesyncReport();
            detector.DesyncDetected += r => captured = r;

            detector.RecordLocal(10, 0x1111ul);
            detector.RecordPeer(7u, 10, 0x2222ul);

            Assert.AreEqual(1, detector.DesyncCount);
            Assert.AreEqual(10, captured.Tick);
            Assert.AreEqual(0x1111ul, captured.LocalHash);
            Assert.AreEqual(0x2222ul, captured.PeerHash);
            Assert.AreEqual(7u, captured.PeerId);
        }

        [Test]
        public void MismatchIsReportedWhenPeerArrivesBeforeLocal()
        {
            SimDesyncDetector detector = new SimDesyncDetector();
            int reports = 0;
            detector.DesyncDetected += _ => reports++;

            // Peer hash first, then our own; the check must still fire.
            detector.RecordPeer(3u, 20, 0xAAAAul);
            detector.RecordLocal(20, 0xBBBBul);

            Assert.AreEqual(1, reports);
        }

        [Test]
        public void FatalDetectorThrowsOnMismatch()
        {
            SimDesyncDetector detector = new SimDesyncDetector();
            detector.Fatal = true;

            detector.RecordLocal(5, 0x1ul);
            Assert.Throws<SimDesyncException>(() => detector.RecordPeer(1u, 5, 0x2ul));
        }

        [Test]
        public void PeerHashesOlderThanTheWindowAreDropped()
        {
            SimDesyncDetector detector = new SimDesyncDetector(4);
            int reports = 0;
            detector.DesyncDetected += _ => reports++;

            // Advance the local frontier well past tick 0.
            for (int tick = 0; tick <= 20; ++tick)
            {
                detector.RecordLocal(tick, (ulong)tick);
            }

            // A late peer hash for a tick that has already fallen out of the window is ignored
            // rather than compared, even though it disagrees.
            detector.RecordPeer(1u, 0, 0xFFFFul);
            Assert.AreEqual(0, reports);
        }

        // ----------------------------------------------------------------- helper ----

        private static unsafe uint SimFloatBitsOf(float value)
        {
            return *(uint*)&value;
        }
    }
}
