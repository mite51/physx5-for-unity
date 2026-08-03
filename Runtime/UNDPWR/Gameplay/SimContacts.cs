using System;
using UNDPWR.Core;
using UNDPWR.Interop;

namespace UNDPWR.Gameplay
{
    /// <summary>
    /// Drains the contact and trigger events a step produced, in a deterministic order.
    /// </summary>
    /// <remarks>
    /// PhysX reports contacts in an order that follows internal pair bookkeeping — exactly
    /// the kind of state a snapshot cannot carry — so the raw order differs between peers
    /// and between an original pass and its replay. The native layer normalises each pair
    /// to ascending stable-ID order and sorts the whole buffer before it crosses the
    /// boundary, so gameplay sees the same events in the same order everywhere.
    /// <para>
    /// Drain once per step, from <see cref="Rollback.ISimStepHandler.OnAfterStep"/>. The
    /// important discipline: a contact only exists for the tick it happened on, so anything
    /// a later tick needs to know about it must be written into the entity or game channel
    /// during the same handler. A contact remembered in a plain field is forgotten by the
    /// next restore and reappears differently on the replay. This is the deferred-contact
    /// pattern the original system used, and the reason it deferred them by a tick.
    /// </para>
    /// </remarks>
    public sealed class SimContacts
    {
        private readonly DeterministicWorld _world;

        /// <summary>Creates a contact interface over a world.</summary>
        public SimContacts(DeterministicWorld world)
        {
            if (world == null)
            {
                throw new ArgumentNullException("world");
            }
            _world = world;
        }

        /// <summary>
        /// Drains this step's contact events into a caller-owned buffer.
        /// </summary>
        /// <param name="destination">Receives the events, up to its length.</param>
        /// <returns>How many events were written.</returns>
        public unsafe int Drain(SimContactEvent[] destination)
        {
            if (destination == null || destination.Length == 0)
            {
                throw new ArgumentException("destination array must be non-empty", "destination");
            }
            fixed (SimContactEvent* dst = destination)
            {
                return (int)NativeMethods.PxwWorldDrainContacts(_world.Handle, dst, (uint)destination.Length);
            }
        }

        /// <summary>
        /// Drains this step's trigger-overlap changes into a caller-owned buffer.
        /// </summary>
        /// <param name="destination">Receives the events, up to its length.</param>
        /// <returns>How many events were written.</returns>
        public unsafe int DrainTriggers(SimTriggerEvent[] destination)
        {
            if (destination == null || destination.Length == 0)
            {
                throw new ArgumentException("destination array must be non-empty", "destination");
            }
            fixed (SimTriggerEvent* dst = destination)
            {
                return (int)NativeMethods.PxwWorldDrainTriggers(_world.Handle, dst, (uint)destination.Length);
            }
        }
    }
}
