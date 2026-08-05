using System;
using System.Collections.Generic;

namespace UNDPWR.Net
{
    /// <summary>
    /// An in-process network of <see cref="ISimTransport"/> endpoints, for tests and for
    /// running several peers in one process.
    /// </summary>
    /// <remarks>
    /// A <see cref="Broadcast"/> from one endpoint is copied into every other endpoint's
    /// inbox. It is not a toy: it models the three things a real transport does to the
    /// framework — <see cref="Latency"/> holds messages for a number of <see cref="Step"/>s
    /// before delivery, <see cref="LossPercent"/> drops them, and <see cref="Reorder"/>
    /// shuffles what is due — so a multi-peer harness can prove the rollback layer survives
    /// loss and lateness without a socket. Delivery advances only when <see cref="Step"/> is
    /// called, which keeps a test deterministic and in control of timing.
    /// </remarks>
    public sealed class SimLoopbackNetwork
    {
        private sealed class Pending
        {
            public Endpoint Target;
            public byte[] Data;
            public int DeliverAtStep;
            public ulong Sequence;
        }

        private readonly List<Endpoint> _endpoints = new List<Endpoint>();
        private readonly List<Pending> _inFlight = new List<Pending>();
        private readonly Random _random;
        private long _step;
        private ulong _sequence;

        /// <summary>Whole-<see cref="Step"/>s a message waits before it can be delivered.</summary>
        public int Latency { get; set; }

        /// <summary>Percent chance, 0..100, that a broadcast copy is dropped.</summary>
        public int LossPercent { get; set; }

        /// <summary>When true, messages due on the same step are delivered in a shuffled order.</summary>
        public bool Reorder { get; set; }

        /// <summary>Creates a network with a fixed seed, so a test replays identically.</summary>
        public SimLoopbackNetwork(int seed = 12345)
        {
            _random = new Random(seed);
        }

        /// <summary>Creates a new endpoint joined to this network.</summary>
        public ISimTransport CreateEndpoint()
        {
            Endpoint endpoint = new Endpoint(this);
            _endpoints.Add(endpoint);
            return endpoint;
        }

        /// <summary>
        /// Advances delivery by one step: everything whose latency has elapsed is moved into
        /// its target's inbox.
        /// </summary>
        public void Step()
        {
            _step++;

            List<Pending> due = null;
            for (int i = _inFlight.Count - 1; i >= 0; --i)
            {
                if (_inFlight[i].DeliverAtStep <= _step)
                {
                    if (due == null)
                    {
                        due = new List<Pending>();
                    }
                    due.Add(_inFlight[i]);
                    _inFlight.RemoveAt(i);
                }
            }

            if (due == null)
            {
                return;
            }

            if (Reorder)
            {
                for (int i = due.Count - 1; i > 0; --i)
                {
                    int j = _random.Next(i + 1);
                    Pending tmp = due[i];
                    due[i] = due[j];
                    due[j] = tmp;
                }
            }
            else
            {
                due.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            }

            for (int i = 0; i < due.Count; ++i)
            {
                due[i].Target.Enqueue(due[i].Data);
            }
        }

        private void Broadcast(Endpoint sender, byte[] data, int offset, int length)
        {
            for (int i = 0; i < _endpoints.Count; ++i)
            {
                Endpoint target = _endpoints[i];
                if (target == sender)
                {
                    continue;
                }
                if (LossPercent > 0 && _random.Next(100) < LossPercent)
                {
                    continue;
                }

                byte[] copy = new byte[length];
                Array.Copy(data, offset, copy, 0, length);

                Pending pending = new Pending();
                pending.Target = target;
                pending.Data = copy;
                pending.DeliverAtStep = (int)_step + (Latency < 0 ? 0 : Latency);
                pending.Sequence = _sequence++;
                _inFlight.Add(pending);
            }
        }

        private sealed class Endpoint : ISimTransport
        {
            private readonly SimLoopbackNetwork _network;
            private readonly Queue<byte[]> _inbox = new Queue<byte[]>();

            public Endpoint(SimLoopbackNetwork network)
            {
                _network = network;
            }

            public void Broadcast(byte[] data, int offset, int length)
            {
                if (data == null)
                {
                    throw new ArgumentNullException("data");
                }
                if (offset < 0 || length < 0 || offset + length > data.Length)
                {
                    throw new ArgumentOutOfRangeException("length");
                }
                _network.Broadcast(this, data, offset, length);
            }

            public bool TryReceive(out ArraySegment<byte> message)
            {
                if (_inbox.Count == 0)
                {
                    message = default(ArraySegment<byte>);
                    return false;
                }
                byte[] data = _inbox.Dequeue();
                message = new ArraySegment<byte>(data, 0, data.Length);
                return true;
            }

            internal void Enqueue(byte[] data)
            {
                _inbox.Enqueue(data);
            }
        }
    }
}
