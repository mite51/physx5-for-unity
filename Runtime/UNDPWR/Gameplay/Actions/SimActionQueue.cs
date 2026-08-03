using System;
using System.Collections.Generic;
using UNDPWR.Core;
using UNDPWR.Diagnostics;

namespace UNDPWR.Gameplay
{
    /// <summary>
    /// Collects gameplay commands and runs them at the tick they are due, in a deterministic
    /// order, persisting any future-scheduled ones through the game channel.
    /// </summary>
    /// <remarks>
    /// Most actions are submitted and executed within the same tick: gameplay decides to fire
    /// a projectile during its update, the queue runs the spawn before the physics step, and
    /// the action is gone by the tick boundary. Those never touch the game channel, so the
    /// common case captures nothing.
    /// <para>
    /// An action scheduled for a later tick is different: it has to survive until then, and it
    /// has to survive a rollback in between, so it rides in the game channel. That is the only
    /// reason <see cref="ISimAction"/> is serializable. Because serialization is polymorphic,
    /// the game registers its action types once at setup, in an order every peer matches, and
    /// each type gets an index that stands in for it on the wire and in the snapshot.
    /// </para>
    /// <para>
    /// Execution order within a tick is submission order, which is deterministic because every
    /// peer runs the same gameplay in the same order to produce the same submissions. Actions
    /// submitted while executing this tick's actions — a spawn that immediately schedules a
    /// despawn — run in the same pass, after the ones already queued.
    /// </para>
    /// </remarks>
    public sealed class SimActionQueue
    {
        private struct Entry
        {
            public int Tick;
            public ISimAction Action;
        }

        private readonly List<Entry> _pending = new List<Entry>();
        private readonly Dictionary<Type, int> _typeIndex = new Dictionary<Type, int>();
        private readonly List<Func<ISimAction>> _factories = new List<Func<ISimAction>>();

        private SimContext _context;

        /// <summary>Wires the queue to its context. Called by the host at setup.</summary>
        internal void Attach(SimContext context)
        {
            _context = context;
        }

        /// <summary>How many actions are waiting for a future tick.</summary>
        public int PendingCount { get { return _pending.Count; } }

        /// <summary>
        /// Registers an action type so future-scheduled instances can be serialized. Call
        /// once per type at setup, in the same order on every peer.
        /// </summary>
        /// <typeparam name="T">The action type.</typeparam>
        /// <param name="factory">Creates a blank instance for deserialization.</param>
        public void RegisterActionType<T>(Func<T> factory) where T : ISimAction
        {
            if (factory == null)
            {
                throw new ArgumentNullException("factory");
            }
            if (_typeIndex.ContainsKey(typeof(T)))
            {
                throw new InvalidOperationException(string.Format("Action type {0} is already registered", typeof(T).Name));
            }
            _typeIndex.Add(typeof(T), _factories.Count);
            _factories.Add(() => factory());
        }

        /// <summary>Schedules an action for the current tick.</summary>
        public void Submit(ISimAction action)
        {
            int tick = _context != null ? _context.CurrentTick : 0;
            Submit(action, tick);
        }

        /// <summary>Schedules an action for a specific tick, now or in the future.</summary>
        public void Submit(ISimAction action, int scheduledTick)
        {
            if (action == null)
            {
                throw new ArgumentNullException("action");
            }
            _pending.Add(new Entry { Tick = scheduledTick, Action = action });
        }

        /// <summary>
        /// Runs every action due on a tick, in submission order, including ones submitted by
        /// those actions.
        /// </summary>
        public void ExecuteDue(int tick, SimContext context)
        {
            // Scan in order. Executing an action may append more entries; because appended
            // entries go to the end, the same forward scan reaches them, so a spawn that
            // schedules a same-tick despawn is handled in this pass, after everything already
            // queued. Not incrementing after a removal re-checks the shifted-down element.
            int i = 0;
            while (i < _pending.Count)
            {
                if (_pending[i].Tick == tick)
                {
                    ISimAction action = _pending[i].Action;
                    _pending.RemoveAt(i);
                    action.Execute(context);
                }
                else
                {
                    ++i;
                }
            }
        }

        /// <summary>Discards everything, for a synchronised rebuild.</summary>
        public void Clear()
        {
            _pending.Clear();
        }

        /// <summary>Writes the pending future actions into the game channel.</summary>
        public void CaptureState(ref SimStateWriter writer)
        {
            writer.WriteInt(_pending.Count);
            for (int i = 0; i < _pending.Count; ++i)
            {
                Entry entry = _pending[i];
                int index;
                if (!_typeIndex.TryGetValue(entry.Action.GetType(), out index))
                {
                    throw new InvalidOperationException(string.Format(
                        "Action type {0} is scheduled for a future tick but was never registered with " +
                        "RegisterActionType, so it cannot be captured for rollback.", entry.Action.GetType().Name));
                }
                writer.WriteInt(entry.Tick);
                writer.WriteInt(index);
                entry.Action.Serialize(ref writer);
            }
        }

        /// <summary>Reads the pending future actions back, replacing the current set.</summary>
        public void RestoreState(ref SimStateReader reader)
        {
            int count = reader.ReadInt();
            _pending.Clear();
            for (int i = 0; i < count; ++i)
            {
                int tick = reader.ReadInt();
                int index = reader.ReadInt();
                if (index < 0 || index >= _factories.Count)
                {
                    throw new InvalidOperationException(string.Format(
                        "Game channel names action type index {0}, but only {1} types are registered. The peers " +
                        "registered action types in different orders.", index, _factories.Count));
                }
                ISimAction action = _factories[index]();
                action.Deserialize(ref reader);
                _pending.Add(new Entry { Tick = tick, Action = action });
            }
        }
    }
}
