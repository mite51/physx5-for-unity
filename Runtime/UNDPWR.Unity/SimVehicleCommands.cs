using UnityEngine;
using PhysX5ForUnity;
using UNDPWR.Rollback;

namespace UNDPWR.Unity
{
    /// <summary>
    /// One tick's worth of vehicle control: brake, handbrake, throttle, steer, and an
    /// optional gear change.
    /// </summary>
    /// <remarks>
    /// A vehicle's commands are <i>input</i>, not simulation state, so they must never ride
    /// the snapshot — the native snapshot deliberately excludes them
    /// (<c>PxVehicleCommandState</c> is not captured). They ride the input frame instead, the
    /// same discipline as every force: decode the tick's <see cref="SimInput"/> into this
    /// struct and apply it from the sim callback that runs on both the live tick and every
    /// replay, so a replayed tick re-applies exactly the command the original tick did.
    /// </remarks>
    public struct SimVehicleCommand
    {
        /// <summary>Primary brake, 0..1.</summary>
        public float Brake;

        /// <summary>Handbrake, 0..1, routed to the wheels flagged as handbrake wheels.</summary>
        public float Handbrake;

        /// <summary>Throttle, 0..1.</summary>
        public float Throttle;

        /// <summary>Steer, -1..1.</summary>
        public float Steer;

        /// <summary>
        /// Whether <see cref="TargetGear"/> and <see cref="Clutch"/> should be pushed. Left
        /// false for the common case where the autobox picks gears and no manual shift is
        /// requested this tick.
        /// </summary>
        public bool HasTransmission;

        /// <summary>Target gear for a manual shift, when <see cref="HasTransmission"/> is set.</summary>
        public int TargetGear;

        /// <summary>Clutch command for a manual shift, when <see cref="HasTransmission"/> is set.</summary>
        public float Clutch;
    }

    /// <summary>
    /// Turns a <see cref="SimInput"/> into vehicle commands and applies them to a
    /// <see cref="PhysxVehicle"/>, keeping vehicle control on the deterministic input path.
    /// </summary>
    /// <remarks>
    /// Call <see cref="Apply(PhysxVehicle, SimInput)"/> (or the decoded overload) from a sim
    /// callback that runs once per tick, before the world steps, on both live and replayed
    /// ticks. Calling it from an ordinary <c>Update</c> or an input callback is the same
    /// self-desync a force applied outside the step causes: the command lands on the live tick
    /// but not on the replay.
    /// <para>
    /// The default decode follows the axis convention in <see cref="SimInput"/>: AxisW is
    /// throttle when positive and brake when negative, AxisZ is steer, and
    /// <see cref="HandbrakeButton"/> is the handbrake. A game with a different mapping builds
    /// its own <see cref="SimVehicleCommand"/> and calls the decoded overload.
    /// </para>
    /// </remarks>
    public static class SimVehicleCommands
    {
        /// <summary>
        /// Button bit the default decode reads as the handbrake. Games that want a different
        /// bit build the command themselves.
        /// </summary>
        public const uint HandbrakeButton = 1u << 0;

        /// <summary>
        /// Decodes a tick's input into a vehicle command using the default axis convention.
        /// </summary>
        public static SimVehicleCommand Decode(SimInput input)
        {
            SimVehicleCommand command = new SimVehicleCommand();
            command.Throttle = Mathf.Clamp01(input.AxisW);
            command.Brake = Mathf.Clamp01(-input.AxisW);
            command.Steer = Mathf.Clamp(input.AxisZ, -1.0f, 1.0f);
            command.Handbrake = (input.Buttons & HandbrakeButton) != 0 ? 1.0f : 0.0f;
            command.HasTransmission = false;
            return command;
        }

        /// <summary>Applies a decoded command to a finalized vehicle.</summary>
        public static void Apply(PhysxVehicle vehicle, SimVehicleCommand command)
        {
            if (vehicle == null || !vehicle.IsFinalized)
            {
                return;
            }
            vehicle.SetCommands(command.Brake, command.Handbrake, command.Throttle, command.Steer);
            if (command.HasTransmission)
            {
                vehicle.SetTransmissionCommand(command.TargetGear, command.Clutch);
            }
        }

        /// <summary>Decodes a tick's input and applies it to a finalized vehicle.</summary>
        public static void Apply(PhysxVehicle vehicle, SimInput input)
        {
            Apply(vehicle, Decode(input));
        }
    }
}
