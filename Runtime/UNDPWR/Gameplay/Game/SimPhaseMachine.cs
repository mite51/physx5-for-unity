using System;
using UNDPWR.Core;

namespace UNDPWR.Gameplay
{
    /// <summary>
    /// A deterministic, rollback-safe state machine for a match's phases: warmup, countdown,
    /// playing, scored, and so on.
    /// </summary>
    /// <remarks>
    /// A game mode almost always has a phase and a timer counting down within it, and both
    /// are game state that has to roll back. This packages the pattern: a phase enum value
    /// and the tick the current phase entered, captured and restored as two integers. It
    /// deliberately holds no callbacks or transition tables — those live in the game mode,
    /// which reads <see cref="Phase"/> and <see cref="TicksInPhase"/> each tick and calls
    /// <see cref="TransitionTo"/> when its own rules say to. Keeping the machine this thin is
    /// what makes it trivially deterministic.
    /// <para>
    /// The phase is stored as an <c>int</c> so any enum works; a game passes its enum values
    /// in and casts them back out. Timing is in ticks, never seconds, because ticks are the
    /// only clock the simulation shares across peers.
    /// </para>
    /// </remarks>
    /// <typeparam name="TPhase">The game's phase enum, backed by <c>int</c>.</typeparam>
    public sealed class SimPhaseMachine<TPhase> where TPhase : struct, IConvertible
    {
        private int _phase;
        private int _enteredTick;

        /// <summary>Creates a machine in a starting phase.</summary>
        public SimPhaseMachine(TPhase initial)
        {
            _phase = ToInt(initial);
            _enteredTick = 0;
        }

        /// <summary>The current phase.</summary>
        public TPhase Phase { get { return FromInt(_phase); } }

        /// <summary>The tick the current phase was entered on.</summary>
        public int EnteredTick { get { return _enteredTick; } }

        /// <summary>How many ticks the machine has been in the current phase.</summary>
        public int TicksInPhase(int currentTick)
        {
            return currentTick - _enteredTick;
        }

        /// <summary>Moves to a new phase, recording the tick it was entered on.</summary>
        /// <remarks>
        /// Re-entering the same phase still resets the entered tick, so a game can restart a
        /// phase timer by transitioning to the phase it is already in.
        /// </remarks>
        public void TransitionTo(TPhase phase, int currentTick)
        {
            _phase = ToInt(phase);
            _enteredTick = currentTick;
        }

        /// <summary>Writes the phase and its entry tick into a state channel.</summary>
        public void Capture(ref SimStateWriter writer)
        {
            writer.WriteInt(_phase);
            writer.WriteInt(_enteredTick);
        }

        /// <summary>Reads the phase and its entry tick back.</summary>
        public void Restore(ref SimStateReader reader)
        {
            _phase = reader.ReadInt();
            _enteredTick = reader.ReadInt();
        }

        private static int ToInt(TPhase value)
        {
            return value.ToInt32(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static TPhase FromInt(int value)
        {
            return (TPhase)Enum.ToObject(typeof(TPhase), value);
        }
    }
}
