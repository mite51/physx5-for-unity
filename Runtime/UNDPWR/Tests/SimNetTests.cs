using System;
using System.Collections.Generic;
using NUnit.Framework;
using PhysX5ForUnity;
using UNDPWR.Core;
using UNDPWR.Diagnostics;
using UNDPWR.Interop;
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

        [Test]
        public void DetachNativeSinkClearsPointerAfterManagedStateWasReset()
        {
            int nativeMessages = 0;
            IntPtr firstMaterial = IntPtr.Zero;
            IntPtr secondMaterial = IntPtr.Zero;
            Action<SimLog.Verbosity, string> listener = delegate(SimLog.Verbosity level, string message)
            {
                if (message.Contains("Create rigid material"))
                {
                    nativeMessages += 1;
                }
            };

            try
            {
                SimLog.Level = SimLog.Verbosity.Info;
                SimLog.MessageLogged += listener;
                SimLog.AttachNativeSink();

                firstMaterial = Physx.CreatePxMaterial(0.5f, 0.5f, 0.0f);
                Assert.Greater(nativeMessages, 0, "the native callback was not installed");

                // Emulate a Unity domain reload: managed statics reset while the native DLL
                // remains loaded and still owns the old function pointer.
                System.Reflection.FieldInfo callbackField = typeof(SimLog).GetField(
                    "_nativeCallback",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                Assert.IsNotNull(callbackField);
                callbackField.SetValue(null, null);

                nativeMessages = 0;
                SimLog.DetachNativeSink();
                secondMaterial = Physx.CreatePxMaterial(0.5f, 0.5f, 0.0f);
                Assert.AreEqual(0, nativeMessages,
                    "DetachNativeSink left the stale callback installed in the native DLL");
            }
            finally
            {
                SimLog.DetachNativeSink();
                SimLog.MessageLogged -= listener;
                if (firstMaterial != IntPtr.Zero)
                {
                    Physx.ReleasePxMaterial(firstMaterial);
                }
                if (secondMaterial != IntPtr.Zero)
                {
                    Physx.ReleasePxMaterial(secondMaterial);
                }
            }
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

        [Test]
        public void ByteBlockRoundTripsWithLengthPrefix()
        {
            byte[] payload = { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x7F };
            SimByteWriter writer = new SimByteWriter(4);
            writer.WriteUInt32(0x11223344u);
            writer.WriteBytes(payload, 0, payload.Length);
            writer.WriteBytes(payload, 2, 0); // an empty block must round-trip too
            byte[] bytes = writer.ToArray();

            SimByteReader reader = new SimByteReader(bytes, 0, bytes.Length);
            Assert.AreEqual(0x11223344u, reader.ReadUInt32());
            CollectionAssert.AreEqual(payload, reader.ReadBytes());
            Assert.AreEqual(0, reader.ReadBytes().Length);
            Assert.IsFalse(reader.HasMore);
        }

        // ------------------------------------------------------- rebuild codec ----

        [Test]
        public void RebuildStateRoundTripsThroughCodec()
        {
            SimRebuildState state = new SimRebuildState();
            state.ResumeTick = 1234;
            state.PlayerIds = new uint[] { 7u, 3u, 42u };
            state.PhysicsHash = 0x0123456789ABCDEFul;
            state.PhysicsData = new byte[] { 1, 2, 3, 4, 5 };
            state.PhysicsSize = 5;
            state.EntityData = new byte[] { 9, 8, 7 };
            state.EntitySize = 3;
            state.GameData = new byte[0];
            state.GameSize = 0;

            byte[] bytes = SimRebuildCodec.Encode(ref state);
            SimRebuildState restored = SimRebuildCodec.Decode(bytes, 0, bytes.Length);

            Assert.AreEqual(state.ResumeTick, restored.ResumeTick);
            CollectionAssert.AreEqual(state.PlayerIds, restored.PlayerIds);
            Assert.AreEqual(state.PhysicsHash, restored.PhysicsHash);
            Assert.AreEqual(state.PhysicsSize, restored.PhysicsSize);
            CollectionAssert.AreEqual(state.PhysicsData, restored.PhysicsData);
            Assert.AreEqual(state.EntitySize, restored.EntitySize);
            CollectionAssert.AreEqual(state.EntityData, restored.EntityData);
            Assert.AreEqual(0, restored.GameSize);
        }

        [Test]
        public void RebuildDecodeRejectsWrongMessageKind()
        {
            byte[] notRebuild = { (byte)SimMessageKind.Hash, 0, 0, 0, 0 };
            Assert.Throws<SimWireFormatException>(
                () => SimRebuildCodec.Decode(notRebuild, 0, notRebuild.Length));
        }

        [Test]
        public void CompactCopiesOnlyMeaningfulPrefix()
        {
            SimRebuildState state = new SimRebuildState();
            state.ResumeTick = 5;
            state.PlayerIds = new uint[] { 1u, 2u };
            state.PhysicsData = new byte[] { 10, 20, 30, 40 };
            state.PhysicsSize = 2; // only the first two bytes are meaningful
            state.EntityData = new byte[0];
            state.GameData = new byte[0];

            SimRebuildState compact = state.Compact();

            Assert.AreEqual(2, compact.PhysicsData.Length);
            Assert.AreEqual(10, compact.PhysicsData[0]);
            Assert.AreEqual(20, compact.PhysicsData[1]);
            Assert.AreNotSame(state.PlayerIds, compact.PlayerIds);
            CollectionAssert.AreEqual(state.PlayerIds, compact.PlayerIds);
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

            detector.RecordLocal(10, Hashes(0xABCDul));
            detector.RecordPeer(7u, 10, Hashes(0xABCDul));

            Assert.AreEqual(0, reports);
            Assert.AreEqual(0, detector.DesyncCount);
        }

        /// <summary>A hash triple, defaulting the channels a test does not care about.</summary>
        private static SimStateHashes Hashes(ulong physics, ulong entity = 0ul, ulong game = 0ul)
        {
            return new SimStateHashes(physics, entity, game);
        }

        private static SimEntryHash Body(uint stableId, ulong hash)
        {
            SimEntryHash entry = new SimEntryHash();
            entry.StableId = stableId;
            entry.Kind = 0;
            entry.Hash = hash;
            return entry;
        }

        [Test]
        public void EntityHashDiffSaysNothingWhenTablesAgree()
        {
            SimEntryHash[] a = { Body(1, 0xAAul), Body(2, 0xBBul) };
            SimEntryHash[] b = { Body(1, 0xAAul), Body(2, 0xBBul) };

            Assert.IsNull(SimEntityHashDiff.Describe(a, 2, b, 2, 7u, 100));
        }

        [Test]
        public void EntityHashDiffNamesOnlyTheBodyThatDiverged()
        {
            // The point of the whole exercise: out of a scene of bodies, say which one moved
            // differently, and confirm the rest agree so they can be ruled out.
            SimEntryHash[] a = { Body(1, 0xAAul), Body(2, 0xBBul), Body(3, 0xCCul) };
            SimEntryHash[] b = { Body(1, 0xAAul), Body(2, 0x99ul), Body(3, 0xCCul) };

            string difference = SimEntityHashDiff.Describe(a, 3, b, 3, 7u, 100);

            StringAssert.Contains("id 2", difference);
            Assert.IsFalse(difference.Contains("id 1 "), "a body that agrees must not be named");
            Assert.IsFalse(difference.Contains("id 3 "), "a body that agrees must not be named");
        }

        [Test]
        public void EntityHashDiffReportsADifferentBodyCount()
        {
            SimEntryHash[] a = { Body(1, 0xAAul), Body(2, 0xBBul) };
            SimEntryHash[] b = { Body(1, 0xAAul) };

            string difference = SimEntityHashDiff.Describe(a, 2, b, 1, 7u, 100);
            StringAssert.Contains("different numbers of bodies", difference);
        }

        [Test]
        public void EntityHashDiffReportsTablesOutOfOrder()
        {
            SimEntryHash[] a = { Body(1, 0xAAul), Body(2, 0xBBul) };
            SimEntryHash[] b = { Body(2, 0xBBul), Body(1, 0xAAul) };

            string difference = SimEntityHashDiff.Describe(a, 2, b, 2, 7u, 100);
            StringAssert.Contains("not in the same order", difference);
        }

        private static SimInternalIdEntry Entry(uint stableId, uint kind, uint actorIndex, ulong islandNode)
        {
            SimInternalIdEntry entry = new SimInternalIdEntry();
            entry.StableId = stableId;
            entry.Kind = kind;
            entry.InternalActorIndex = actorIndex;
            entry.IslandNodeIndex = islandNode;
            return entry;
        }

        [Test]
        public void RegistrationCheckAcceptsIdenticalTables()
        {
            SimInternalIdEntry[] a = { Entry(1, 0, 0, 10), Entry(2, 0, 1, 11) };
            SimInternalIdEntry[] b = { Entry(1, 0, 0, 10), Entry(2, 0, 1, 11) };

            string problem;
            Assert.IsTrue(SimRegistrationCheck.Compare(a, 2, b, 2, out problem));
            Assert.IsNull(problem);
        }

        [Test]
        public void RegistrationCheckIgnoresIslandNode()
        {
            // The island node index changes when a body sleeps or wakes, so two peers a few ticks
            // apart legitimately hold different values for the same body. Comparing it would report
            // a desync every time the ball came to rest, so the check must not look at it.
            SimInternalIdEntry[] a = { Entry(1, 0, 0, 10), Entry(2, 0, 1, 11) };
            SimInternalIdEntry[] b = { Entry(1, 0, 0, 999), Entry(2, 0, 1, 12345) };

            string problem;
            Assert.IsTrue(SimRegistrationCheck.Compare(a, 2, b, 2, out problem),
                "island node differences must not be reported: " + problem);
        }

        [Test]
        public void RegistrationCheckNamesABodyWithADifferentActorIndex()
        {
            // The classic bug: the same framework entity is a different body inside PhysX, so the
            // solver visits it in a different order and the peers drift after the first contact.
            SimInternalIdEntry[] a = { Entry(1, 0, 0, 10), Entry(2, 0, 1, 11) };
            SimInternalIdEntry[] b = { Entry(1, 0, 1, 10), Entry(2, 0, 0, 11) };

            string problem;
            Assert.IsFalse(SimRegistrationCheck.Compare(a, 2, b, 2, out problem));
            StringAssert.Contains("Stable ID 1", problem);
        }

        [Test]
        public void RegistrationCheckNamesADifferentBodyCount()
        {
            SimInternalIdEntry[] a = { Entry(1, 0, 0, 10), Entry(2, 0, 1, 11) };
            SimInternalIdEntry[] b = { Entry(1, 0, 0, 10) };

            string problem;
            Assert.IsFalse(SimRegistrationCheck.Compare(a, 2, b, 1, out problem));
            StringAssert.Contains("different numbers of bodies", problem);
        }

        [Test]
        public void RegistrationCheckNamesADifferentBuildOrder()
        {
            SimInternalIdEntry[] a = { Entry(1, 0, 0, 10), Entry(2, 0, 1, 11) };
            SimInternalIdEntry[] b = { Entry(2, 0, 0, 11), Entry(1, 0, 1, 10) };

            string problem;
            Assert.IsFalse(SimRegistrationCheck.Compare(a, 2, b, 2, out problem));
            StringAssert.Contains("Registration order differs", problem);
        }

        [Test]
        public void DesyncNamesTheChannelThatDiffers()
        {
            // The point of putting all three channel hashes on the wire rather than the fold.
            // "The worlds diverged" is a week of bisecting; "the game channel diverged, physics
            // agrees" says the bodies are in the same places and the bug is in game logic.
            SimDesyncDetector detector = new SimDesyncDetector();
            SimDesyncReport captured = new SimDesyncReport();
            detector.DesyncDetected += r => captured = r;

            detector.RecordLocal(10, new SimStateHashes(0x1111ul, 0x2222ul, 0x3333ul));
            detector.RecordPeer(7u, 10, new SimStateHashes(0x1111ul, 0x2222ul, 0x9999ul));

            Assert.AreEqual(SimStateChannel.Game, captured.Channels);
            StringAssert.Contains("Game", captured.Describe());
        }

        [Test]
        public void DesyncReportsEveryChannelThatDiffers()
        {
            SimDesyncDetector detector = new SimDesyncDetector();
            SimDesyncReport captured = new SimDesyncReport();
            detector.DesyncDetected += r => captured = r;

            detector.RecordLocal(10, new SimStateHashes(0x1111ul, 0x2222ul, 0x3333ul));
            detector.RecordPeer(7u, 10, new SimStateHashes(0xAAAAul, 0x2222ul, 0xCCCCul));

            Assert.AreEqual(SimStateChannel.Physics | SimStateChannel.Game, captured.Channels);
        }

        [Test]
        public void ChannelsThatAgreeAreNotReportedAsDiverged()
        {
            // Two triples that fold to different combined hashes must still attribute precisely;
            // a channel that matches is evidence, and calling it diverged would waste the search.
            SimStateHashes local = new SimStateHashes(0x1111ul, 0x2222ul, 0x3333ul);
            SimStateHashes peer = new SimStateHashes(0x1111ul, 0x8888ul, 0x3333ul);

            Assert.AreNotEqual(local.Combined, peer.Combined, "the fold must notice the difference");
            Assert.AreEqual(SimStateChannel.Entity, local.Differences(peer));
        }

        [Test]
        public void MismatchIsReportedWhenPeerArrivesAfterLocal()
        {
            SimDesyncDetector detector = new SimDesyncDetector();
            SimDesyncReport captured = new SimDesyncReport();
            detector.DesyncDetected += r => captured = r;

            detector.RecordLocal(10, Hashes(0x1111ul));
            detector.RecordPeer(7u, 10, Hashes(0x2222ul));

            Assert.AreEqual(1, detector.DesyncCount);
            Assert.AreEqual(10, captured.Tick);
            Assert.AreEqual(Hashes(0x1111ul).Combined, captured.LocalHash);
            Assert.AreEqual(Hashes(0x2222ul).Combined, captured.PeerHash);
            Assert.AreEqual(7u, captured.PeerId);
        }

        [Test]
        public void MismatchIsReportedWhenPeerArrivesBeforeLocal()
        {
            SimDesyncDetector detector = new SimDesyncDetector();
            int reports = 0;
            detector.DesyncDetected += _ => reports++;

            // Peer hash first, then our own; the check must still fire.
            detector.RecordPeer(3u, 20, Hashes(0xAAAAul));
            detector.RecordLocal(20, Hashes(0xBBBBul));

            Assert.AreEqual(1, reports);
        }

        [Test]
        public void FatalDetectorThrowsOnMismatch()
        {
            SimDesyncDetector detector = new SimDesyncDetector();
            detector.Fatal = true;

            detector.RecordLocal(5, Hashes(0x1ul));
            Assert.Throws<SimDesyncException>(() => detector.RecordPeer(1u, 5, Hashes(0x2ul)));
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
                detector.RecordLocal(tick, Hashes((ulong)tick));
            }

            // A late peer hash for a tick that has already fallen out of the window is ignored
            // rather than compared, even though it disagrees.
            detector.RecordPeer(1u, 0, Hashes(0xFFFFul));
            Assert.AreEqual(0, reports);
        }

        // ----------------------------------------------------------------- helper ----

        private static unsafe uint SimFloatBitsOf(float value)
        {
            return *(uint*)&value;
        }
    }
}
