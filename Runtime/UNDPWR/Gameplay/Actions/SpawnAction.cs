using UnityEngine;
using UNDPWR.Core;

namespace UNDPWR.Gameplay
{
    /// <summary>
    /// Spawns a pooled entity at a pose, optionally recording who spawned it.
    /// </summary>
    /// <remarks>
    /// A thin action over <see cref="SimEntityPool.Spawn(string, Vector3, Quaternion, uint)"/>.
    /// It reverses itself for free: a rollback restores the entity channel, which puts the
    /// spawned instance's active flag back to false, and the forward replay runs this action
    /// again to set it true. Nothing here has to remember the instance it produced.
    /// </remarks>
    public sealed class SpawnAction : ISimAction
    {
        /// <summary>The pool group to draw from.</summary>
        public string PoolKey;

        /// <summary>Where to place the spawned entity.</summary>
        public Vector3 Position;

        /// <summary>The spawned entity's orientation.</summary>
        public Quaternion Rotation;

        /// <summary>The spawning entity or player's stable ID, or <see cref="SimGameEntity.NoOwner"/>.</summary>
        public uint Owner;

        /// <summary>Creates an empty action, for deserialization.</summary>
        public SpawnAction()
        {
            Rotation = Quaternion.identity;
            Owner = SimGameEntity.NoOwner;
        }

        /// <summary>Creates a spawn action.</summary>
        public SpawnAction(string poolKey, Vector3 position, Quaternion rotation, uint owner)
        {
            PoolKey = poolKey;
            Position = position;
            Rotation = rotation;
            Owner = owner;
        }

        /// <inheritdoc/>
        public void Execute(SimContext context)
        {
            context.Pool.Spawn(PoolKey, Position, Rotation, Owner);
        }

        /// <inheritdoc/>
        public void Serialize(ref SimStateWriter writer)
        {
            // Only reached for a spawn scheduled ahead of time. The pool key goes in verbatim;
            // a game with many delayed spawns can subclass to write a small group index
            // instead, but the common case has no future spawns and serializes nothing at all.
            WriteString(ref writer, PoolKey);
            writer.WriteVector3(Position);
            writer.WriteQuaternion(Rotation);
            writer.WriteUInt(Owner);
        }

        /// <inheritdoc/>
        public void Deserialize(ref SimStateReader reader)
        {
            PoolKey = ReadString(ref reader);
            Position = reader.ReadVector3();
            Rotation = reader.ReadQuaternion();
            Owner = reader.ReadUInt();
        }

        internal static void WriteString(ref SimStateWriter writer, string value)
        {
            if (value == null)
            {
                writer.WriteInt(-1);
                return;
            }
            writer.WriteInt(value.Length);
            for (int i = 0; i < value.Length; ++i)
            {
                writer.Write(value[i]);
            }
        }

        internal static string ReadString(ref SimStateReader reader)
        {
            int length = reader.ReadInt();
            if (length < 0)
            {
                return null;
            }
            char[] chars = new char[length];
            for (int i = 0; i < length; ++i)
            {
                chars[i] = reader.Read<char>();
            }
            return new string(chars);
        }
    }
}
