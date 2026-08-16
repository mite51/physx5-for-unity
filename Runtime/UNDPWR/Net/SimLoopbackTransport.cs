using System;
using System.Collections.Generic;

namespace UNDPWR.Net
{
    /// <summary>
    /// An in-process authoritative network of <see cref="ISimTransport"/> endpoints.
    /// </summary>
    /// <remarks>
    /// A directed send is copied into its destination's inbox. It models the three things a
    /// real unreliable transport does to the
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
            public uint SenderId;
            public byte[] Data;
            public SimDelivery Delivery;
            public int DeliverAtStep;
            public ulong Sequence;
        }

        private readonly Dictionary<uint, Endpoint> _endpoints = new Dictionary<uint, Endpoint>();
        private readonly List<Pending> _inFlight = new List<Pending>();
        private readonly Random _random;
        private long _step;
        private ulong _sequence;

        /// <summary>Whole-<see cref="Step"/>s a message waits before it can be delivered.</summary>
        public int Latency { get; set; }

        /// <summary>Percent chance, 0..100, that an unreliable message is dropped.</summary>
        public int LossPercent { get; set; }

        /// <summary>When true, messages due on the same step are delivered in a shuffled order.</summary>
        public bool Reorder { get; set; }

        /// <summary>Creates a network with a fixed seed, so a test replays identically.</summary>
        public SimLoopbackNetwork(int seed = 12345)
        {
            _random = new Random(seed);
        }

        /// <summary>Creates an authenticated endpoint with a unique peer ID.</summary>
        public ISimTransport CreateEndpoint(uint peerId)
        {
            if (_endpoints.ContainsKey(peerId))
            {
                throw new InvalidOperationException("A loopback endpoint already uses peer ID " + peerId);
            }
            Endpoint endpoint = new Endpoint(this, peerId);
            _endpoints.Add(peerId, endpoint);
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

            due.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            if (Reorder)
            {
                // Shuffle only unreliable entries. Reliable entries remain in send order.
                List<int> unreliableSlots = new List<int>();
                List<Pending> unreliable = new List<Pending>();
                for (int i = 0; i < due.Count; ++i)
                {
                    if (due[i].Delivery == SimDelivery.Unreliable)
                    {
                        unreliableSlots.Add(i);
                        unreliable.Add(due[i]);
                    }
                }
                for (int i = unreliable.Count - 1; i > 0; --i)
                {
                    int j = _random.Next(i + 1);
                    Pending tmp = unreliable[i];
                    unreliable[i] = unreliable[j];
                    unreliable[j] = tmp;
                }
                for (int i = 0; i < unreliableSlots.Count; ++i)
                {
                    due[unreliableSlots[i]] = unreliable[i];
                }
            }

            for (int i = 0; i < due.Count; ++i)
            {
                due[i].Target.Enqueue(due[i].SenderId, due[i].Data, due[i].Delivery);
            }
        }

        private void Send(Endpoint sender, uint recipientId, byte[] data, int offset, int length,
            SimDelivery delivery)
        {
            Endpoint target;
            if (!_endpoints.TryGetValue(recipientId, out target))
            {
                throw new InvalidOperationException("No loopback endpoint uses peer ID " + recipientId);
            }
            if (target == sender)
            {
                throw new InvalidOperationException("A transport endpoint cannot send to itself.");
            }
            if (delivery == SimDelivery.Unreliable
                && LossPercent > 0 && _random.Next(100) < LossPercent)
            {
                return;
            }

            byte[] copy = new byte[length];
            Array.Copy(data, offset, copy, 0, length);

            Pending pending = new Pending();
            pending.Target = target;
            pending.SenderId = sender.LocalPeerId;
            pending.Data = copy;
            pending.Delivery = delivery;
            pending.DeliverAtStep = (int)_step + (Latency < 0 ? 0 : Latency);
            pending.Sequence = _sequence++;
            _inFlight.Add(pending);
        }

        private sealed class Endpoint : ISimTransport
        {
            private readonly SimLoopbackNetwork _network;
            private readonly Queue<SimTransportMessage> _inbox = new Queue<SimTransportMessage>();

            public Endpoint(SimLoopbackNetwork network, uint peerId)
            {
                _network = network;
                LocalPeerId = peerId;
            }

            public uint LocalPeerId { get; private set; }

            public void Send(uint recipientId, byte[] data, int offset, int length, SimDelivery delivery)
            {
                if (data == null)
                {
                    throw new ArgumentNullException("data");
                }
                if (offset < 0 || length < 0 || offset + length > data.Length)
                {
                    throw new ArgumentOutOfRangeException("length");
                }
                _network.Send(this, recipientId, data, offset, length, delivery);
            }

            public bool TryReceive(out SimTransportMessage message)
            {
                if (_inbox.Count == 0)
                {
                    message = default(SimTransportMessage);
                    return false;
                }
                message = _inbox.Dequeue();
                return true;
            }

            internal void Enqueue(uint senderId, byte[] data, SimDelivery delivery)
            {
                SimTransportMessage message = new SimTransportMessage();
                message.SenderId = senderId;
                message.Payload = new ArraySegment<byte>(data, 0, data.Length);
                message.Delivery = delivery;
                _inbox.Enqueue(message);
            }
        }
    }
}
