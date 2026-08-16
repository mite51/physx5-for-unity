using System;
using System.Collections.Generic;

namespace UNDPWR.Rollback
{
    /// <summary>A server-assigned deterministic gameplay event.</summary>
    public struct SimAuthoritativeEvent
    {
        public uint PlayerId;
        public uint Sequence;
        public int Tick;
        public ushort TypeId;
        public byte[] Payload;
    }

    /// <summary>Consumes authoritative events at their assigned simulation tick.</summary>
    public interface ISimAuthoritativeEventHandler
    {
        void OnAuthoritativeEvent(SimAuthoritativeEvent command, bool isReplay);
    }

    /// <summary>Rollback-external immutable event timeline retained alongside input history.</summary>
    internal sealed class SimAuthoritativeEventBuffer
    {
        private readonly int _capacity;
        private readonly Dictionary<int, List<SimAuthoritativeEvent>> _byTick =
            new Dictionary<int, List<SimAuthoritativeEvent>>();
        private readonly Dictionary<ulong, int> _known = new Dictionary<ulong, int>();
        private int _newestTick = -1;

        public SimAuthoritativeEventBuffer(int capacity)
        {
            _capacity = capacity;
        }

        public bool Contains(uint playerId, uint sequence)
        {
            return _known.ContainsKey(((ulong)playerId << 32) | sequence);
        }

        /// <returns>False for a redundant event; true when newly inserted.</returns>
        public bool Submit(SimAuthoritativeEvent command)
        {
            ulong key = ((ulong)command.PlayerId << 32) | command.Sequence;
            if (_known.ContainsKey(key))
            {
                return false;
            }
            List<SimAuthoritativeEvent> events;
            if (!_byTick.TryGetValue(command.Tick, out events))
            {
                events = new List<SimAuthoritativeEvent>();
                _byTick.Add(command.Tick, events);
            }
            events.Add(command);
            events.Sort(Compare);
            _known.Add(key, command.Tick);
            if (command.Tick > _newestTick)
            {
                _newestTick = command.Tick;
                Prune();
            }
            return true;
        }

        public IList<SimAuthoritativeEvent> Get(int tick)
        {
            List<SimAuthoritativeEvent> events;
            return _byTick.TryGetValue(tick, out events) ? events : Empty;
        }

        public SimAuthoritativeEvent[] CopyAt(int tick)
        {
            IList<SimAuthoritativeEvent> events = Get(tick);
            SimAuthoritativeEvent[] copy = new SimAuthoritativeEvent[events.Count];
            for (int i = 0; i < events.Count; ++i)
            {
                copy[i] = Copy(events[i]);
            }
            return copy;
        }

        public SimAuthoritativeEvent[] CopyAfter(int confirmedTick)
        {
            List<SimAuthoritativeEvent> copy = new List<SimAuthoritativeEvent>();
            foreach (KeyValuePair<int, List<SimAuthoritativeEvent>> pair in _byTick)
            {
                if (pair.Key <= confirmedTick)
                {
                    continue;
                }
                for (int i = 0; i < pair.Value.Count; ++i)
                {
                    copy.Add(Copy(pair.Value[i]));
                }
            }
            copy.Sort(CompareByTick);
            return copy.ToArray();
        }

        public void ResetAfterConfirmed(int confirmedTick, SimAuthoritativeEvent[] pending)
        {
            _byTick.Clear();
            _known.Clear();
            _newestTick = confirmedTick;
            if (pending == null)
            {
                return;
            }
            for (int i = 0; i < pending.Length; ++i)
            {
                if (pending[i].Tick > confirmedTick)
                {
                    Submit(pending[i]);
                }
            }
        }

        public void DiscardThrough(int confirmedTick)
        {
            List<int> ticks = new List<int>();
            foreach (int tick in _byTick.Keys)
            {
                if (tick <= confirmedTick) ticks.Add(tick);
            }
            for (int i = 0; i < ticks.Count; ++i)
            {
                RemoveTick(ticks[i]);
            }
        }

        private void Prune()
        {
            int oldest = _newestTick - _capacity + 1;
            List<int> ticks = new List<int>();
            foreach (int tick in _byTick.Keys)
            {
                if (tick < oldest) ticks.Add(tick);
            }
            for (int i = 0; i < ticks.Count; ++i)
            {
                RemoveTick(ticks[i]);
            }
        }

        private void RemoveTick(int tick)
        {
            List<SimAuthoritativeEvent> events;
            if (!_byTick.TryGetValue(tick, out events))
            {
                return;
            }
            for (int i = 0; i < events.Count; ++i)
            {
                _known.Remove(((ulong)events[i].PlayerId << 32) | events[i].Sequence);
            }
            _byTick.Remove(tick);
        }

        private static int Compare(SimAuthoritativeEvent a, SimAuthoritativeEvent b)
        {
            int player = a.PlayerId.CompareTo(b.PlayerId);
            return player != 0 ? player : a.Sequence.CompareTo(b.Sequence);
        }

        private static int CompareByTick(SimAuthoritativeEvent a, SimAuthoritativeEvent b)
        {
            int tick = a.Tick.CompareTo(b.Tick);
            return tick != 0 ? tick : Compare(a, b);
        }

        private static SimAuthoritativeEvent Copy(SimAuthoritativeEvent value)
        {
            byte[] payload = value.Payload ?? new byte[0];
            value.Payload = new byte[payload.Length];
            Array.Copy(payload, value.Payload, payload.Length);
            return value;
        }

        private static readonly IList<SimAuthoritativeEvent> Empty =
            new SimAuthoritativeEvent[0];
    }
}
