using System;
using System.Collections.Generic;
using UNDPWR.Core;
using UNDPWR.Diagnostics;

namespace UNDPWR.Rollback
{
    /// <summary>
    /// One player's input for one tick.
    /// </summary>
    /// <remarks>
    /// Inputs are the only thing that crosses the network during a match. Everything
    /// else, every pose, velocity and contact, is recomputed identically by every peer
    /// from the same inputs, which is what keeps bandwidth flat as the physics scene
    /// grows.
    /// <para>
    /// Deliberately a fixed-size struct rather than an interface. A per-tick allocation
    /// in a loop that replays the prediction window every frame is the easiest way
    /// to make a rollback engine stutter, and a fixed payload also keeps the wire format
    /// trivially serialisable. Games with richer input pack it into
    /// <see cref="Buttons"/> and the axis fields, or extend the struct and update the
    /// wire format to match.
    /// </para>
    /// </remarks>
    [Serializable]
    public struct SimInput : IEquatable<SimInput>
    {
        /// <summary>Which player this input belongs to.</summary>
        public uint PlayerId;

        /// <summary>The tick it applies to.</summary>
        public int Tick;

        /// <summary>Bit field of pressed buttons, game-defined.</summary>
        public uint Buttons;

        /// <summary>First analogue axis, conventionally movement X.</summary>
        public float AxisX;

        /// <summary>Second analogue axis, conventionally movement Y.</summary>
        public float AxisY;

        /// <summary>Third analogue axis, conventionally aim yaw or steering.</summary>
        public float AxisZ;

        /// <summary>Fourth analogue axis, conventionally aim pitch or throttle.</summary>
        public float AxisW;

        /// <summary>
        /// True when this input was predicted rather than received.
        /// </summary>
        /// <remarks>
        /// Not part of equality or the hash, because whether a value was guessed does not
        /// change what the simulation does with it. It exists so a predicted value can be
        /// overwritten when the real input arrives, and so diagnostics can tell the two
        /// apart. It does not gate rollback: the engine rewinds on a fixed schedule
        /// whether or not a prediction turned out correct, so the operation sequence does
        /// not depend on network timing. See <see cref="RollbackEngine.SubmitInput"/>.
        /// </remarks>
        [NonSerialized]
        public bool IsPredicted;

        /// <summary>An input with everything neutral, used as the prediction seed.</summary>
        public static SimInput Neutral(uint playerId, int tick)
        {
            SimInput input = new SimInput();
            input.PlayerId = playerId;
            input.Tick = tick;
            return input;
        }

        /// <summary>
        /// Compares the simulation-affecting fields, ignoring the tick, the player and
        /// whether the value was predicted.
        /// </summary>
        /// <remarks>
        /// This is the comparison that decides whether a late input forces a rollback.
        /// Most inputs are held rather than changed, so a player walking in a straight
        /// line produces predictions that match exactly and cost nothing.
        /// </remarks>
        public bool SameCommandAs(SimInput other)
        {
            return Buttons == other.Buttons
                && AxisX == other.AxisX
                && AxisY == other.AxisY
                && AxisZ == other.AxisZ
                && AxisW == other.AxisW;
        }

        /// <inheritdoc/>
        public bool Equals(SimInput other)
        {
            return PlayerId == other.PlayerId && Tick == other.Tick && SameCommandAs(other);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is SimInput && Equals((SimInput)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return (int)(ComputeHash() ^ (ComputeHash() >> 32));
        }

        /// <summary>Folds the simulation-affecting fields into a hash.</summary>
        public ulong ComputeHash()
        {
            ulong hash = SimHash.OffsetBasis;
            hash = SimHash.Combine(hash, PlayerId);
            hash = SimHash.Combine(hash, Tick);
            hash = SimHash.Combine(hash, Buttons);
            hash = SimHash.Combine(hash, AxisX);
            hash = SimHash.Combine(hash, AxisY);
            hash = SimHash.Combine(hash, AxisZ);
            hash = SimHash.Combine(hash, AxisW);
            return hash;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format("Input(p{0}, t{1}, buttons 0x{2:X8}, axes {3:F3} {4:F3} {5:F3} {6:F3}{7})",
                PlayerId, Tick, Buttons, AxisX, AxisY, AxisZ, AxisW, IsPredicted ? ", predicted" : "");
        }
    }

    /// <summary>
    /// Every player's input for one tick, in a fixed player order.
    /// </summary>
    /// <remarks>
    /// Order matters and is by player ID, not by arrival. Gameplay code that iterates a
    /// frame's inputs must see the same order on every peer, or two peers apply the same
    /// forces in a different order and get different floating point results.
    /// </remarks>
    public sealed class SimInputFrame
    {
        private readonly SimInput[] _inputs;

        /// <summary>The tick these inputs apply to.</summary>
        public int Tick { get; internal set; }

        /// <summary>How many players this frame covers.</summary>
        public int PlayerCount { get { return _inputs.Length; } }

        /// <summary>
        /// True when every input in the frame was actually received rather than guessed.
        /// </summary>
        public bool IsComplete
        {
            get
            {
                for (int i = 0; i < _inputs.Length; ++i)
                {
                    if (_inputs[i].IsPredicted) return false;
                }
                return true;
            }
        }

        internal SimInputFrame(int playerCount)
        {
            _inputs = new SimInput[playerCount];
            Tick = -1;
        }

        /// <summary>
        /// One player's input, indexed by slot. Slots are assigned in ascending player-ID
        /// order at session start and never change, so the index is stable across peers.
        /// </summary>
        public SimInput this[int slot]
        {
            get { return _inputs[slot]; }
            internal set { _inputs[slot] = value; }
        }

        /// <summary>Folds every input in the frame into a hash, in slot order.</summary>
        public ulong ComputeHash()
        {
            ulong hash = SimHash.OffsetBasis;
            for (int i = 0; i < _inputs.Length; ++i)
            {
                hash = SimHash.Combine(hash, _inputs[i].ComputeHash());
            }
            return hash;
        }

        internal void Reset(int tick)
        {
            Tick = tick;
            for (int i = 0; i < _inputs.Length; ++i)
            {
                _inputs[i] = SimInput.Neutral(_inputs[i].PlayerId, tick);
                _inputs[i].IsPredicted = true;
            }
        }
    }
}
