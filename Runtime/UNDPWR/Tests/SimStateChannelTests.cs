using NUnit.Framework;
using UnityEngine;
using UNDPWR.Core;
using UNDPWR.Rollback;

namespace UNDPWR.Tests
{
    /// <summary>
    /// Determinism tests for the managed state channels, with no native dependency.
    /// </summary>
    /// <remarks>
    /// These cover the property everything else in the gameplay layer is built on: a
    /// capture followed by a restore reproduces exactly the state that was captured, so a
    /// hash taken before a rollback matches the hash taken after the resimulation. The
    /// full engine cycle needs the native PhysX world; these exercise the pure-managed
    /// serialization contract that a desync in gameplay state would violate first.
    /// </remarks>
    public class SimStateChannelTests
    {
        [Test]
        public void WriterReaderRoundTripsEveryType()
        {
            SimStateWriter writer = new SimStateWriter(null);
            writer.WriteInt(-42);
            writer.WriteUInt(0xDEADBEEFu);
            writer.WriteFloat(3.14159f);
            writer.WriteBool(true);
            writer.WriteBool(false);
            writer.WriteVector3(new Vector3(1.5f, -2.5f, 3.5f));
            writer.WriteQuaternion(new Quaternion(0.1f, 0.2f, 0.3f, 0.9f));

            SimStateReader reader = new SimStateReader(writer.Buffer, writer.Position);
            Assert.AreEqual(-42, reader.ReadInt());
            Assert.AreEqual(0xDEADBEEFu, reader.ReadUInt());
            Assert.AreEqual(3.14159f, reader.ReadFloat());
            Assert.IsTrue(reader.ReadBool());
            Assert.IsFalse(reader.ReadBool());
            Assert.AreEqual(new Vector3(1.5f, -2.5f, 3.5f), reader.ReadVector3());
            Assert.AreEqual(new Quaternion(0.1f, 0.2f, 0.3f, 0.9f), reader.ReadQuaternion());
            Assert.AreEqual(0, reader.Remaining);
        }

        [Test]
        public void WriterArrayRoundTrips()
        {
            int[] source = { 5, 6, 7, 8 };
            SimStateWriter writer = new SimStateWriter(null);
            writer.WriteArray(source, source.Length);

            int[] destination = new int[16];
            SimStateReader reader = new SimStateReader(writer.Buffer, writer.Position);
            int count = reader.ReadArray(destination);

            Assert.AreEqual(4, count);
            for (int i = 0; i < count; ++i)
            {
                Assert.AreEqual(source[i], destination[i]);
            }
        }

        [Test]
        public void IdenticalStateHashesIdentically()
        {
            ulong first = HashOf(1.0f, 2.0f, 3);
            ulong second = HashOf(1.0f, 2.0f, 3);
            Assert.AreEqual(first, second);
        }

        [Test]
        public void OneBitDifferenceChangesHash()
        {
            ulong baseline = HashOf(1.0f, 2.0f, 3);
            ulong changed = HashOf(1.0f, 2.0000001f, 3);
            Assert.AreNotEqual(baseline, changed);
        }

        [Test]
        public void GrownBufferMatchesUngrownHash()
        {
            // A tiny starting buffer forces a resize; a large one does not. The hash must
            // not depend on how the buffer happened to be sized, only on the bytes written.
            SimStateWriter grown = new SimStateWriter(new byte[1]);
            SimStateWriter roomy = new SimStateWriter(new byte[4096]);
            for (int i = 0; i < 200; ++i)
            {
                grown.WriteFloat(i * 0.5f);
                roomy.WriteFloat(i * 0.5f);
            }
            Assert.AreEqual(grown.Position, roomy.Position);
            Assert.AreEqual(grown.Hash, roomy.Hash);
        }

        [Test]
        public void ProviderCaptureRestoreRecaptureIsStable()
        {
            // Mirrors what the engine does across a rollback: capture a provider's state,
            // restore it into another provider, and recapture. If the channel serialization
            // is deterministic the two captures hash identically.
            FakeProvider original = new FakeProvider();
            original.Load(health: 27.5f, timer: 14, scores: new[] { 3, 1, 4 });

            byte[] entityBuffer = new byte[0];
            SimStateWriter entityWriter = new SimStateWriter(entityBuffer);
            original.CaptureEntityState(ref entityWriter);
            ulong entityHashBefore = entityWriter.Hash;

            byte[] gameBuffer = new byte[0];
            SimStateWriter gameWriter = new SimStateWriter(gameBuffer);
            original.CaptureGameState(ref gameWriter);
            ulong gameHashBefore = gameWriter.Hash;

            FakeProvider restored = new FakeProvider();
            SimStateReader entityReader = new SimStateReader(entityWriter.Buffer, entityWriter.Position);
            restored.RestoreEntityState(ref entityReader);
            SimStateReader gameReader = new SimStateReader(gameWriter.Buffer, gameWriter.Position);
            restored.RestoreGameState(ref gameReader);

            SimStateWriter entityRecapture = new SimStateWriter(null);
            restored.CaptureEntityState(ref entityRecapture);
            SimStateWriter gameRecapture = new SimStateWriter(null);
            restored.CaptureGameState(ref gameRecapture);

            Assert.AreEqual(entityHashBefore, entityRecapture.Hash, "entity channel drifted across a restore");
            Assert.AreEqual(gameHashBefore, gameRecapture.Hash, "game channel drifted across a restore");
        }

        private static ulong HashOf(float a, float b, int c)
        {
            SimStateWriter writer = new SimStateWriter(null);
            writer.WriteFloat(a);
            writer.WriteFloat(b);
            writer.WriteInt(c);
            return writer.Hash;
        }

        private sealed class FakeProvider : ISimStateProvider
        {
            private float _health;
            private int _timer;
            private readonly int[] _scores = new int[8];
            private int _scoreCount;

            public void Load(float health, int timer, int[] scores)
            {
                _health = health;
                _timer = timer;
                _scoreCount = scores.Length;
                for (int i = 0; i < _scoreCount; ++i)
                {
                    _scores[i] = scores[i];
                }
            }

            public void CaptureEntityState(ref SimStateWriter writer)
            {
                writer.WriteFloat(_health);
                writer.WriteInt(_timer);
            }

            public void RestoreEntityState(ref SimStateReader reader)
            {
                _health = reader.ReadFloat();
                _timer = reader.ReadInt();
            }

            public void CaptureGameState(ref SimStateWriter writer)
            {
                writer.WriteArray(_scores, _scoreCount);
            }

            public void RestoreGameState(ref SimStateReader reader)
            {
                _scoreCount = reader.ReadArray(_scores);
            }
        }
    }
}
