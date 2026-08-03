using UnityEngine;
using UNDPWR.Rollback;

namespace UNDPWR.Gameplay
{
    /// <summary>
    /// Turns a peer's raw local movement into the quantized, camera-resolved world direction
    /// that the simulation runs on — and, critically, makes the local peer run on exactly the
    /// value the remote peers will.
    /// </summary>
    /// <remarks>
    /// The subtle bug this exists to prevent: a peer reads a smooth analogue stick, resolves
    /// it against its camera into a float direction, and simulates from that float — while
    /// sending a compressed, quantized version over the wire. Every remote peer then simulates
    /// from the dequantized value, which is close to but not the same as the sender's float,
    /// and the sender has desynced against everyone else from the very first tick.
    /// <para>
    /// The fix is to quantize <i>and dequantize</i> before the value is ever used locally, so
    /// the sender simulates from the same dequantized value the receivers will. That round
    /// trip is what <see cref="EncodeMovement"/> does, and why <see cref="BuildInput"/> stores
    /// the dequantized result. The transport carries the one-byte-per-axis quantized form;
    /// re-quantizing the stored float reproduces those exact bytes, so the two paths agree.
    /// </para>
    /// <para>
    /// Movement lands in <see cref="SimInput.AxisX"/> (world X) and <see cref="SimInput.AxisY"/>
    /// (world Z). Per the aim model, entities aim along their own facing, so
    /// <see cref="SimInput.AxisZ"/> and <see cref="SimInput.AxisW"/> are left free for a game
    /// to use later without disturbing the wire format.
    /// </para>
    /// </remarks>
    public static class SimInputEncoder
    {
        private const float Scale = 127f;

        /// <summary>Compresses a value in [-1, 1] to a signed byte.</summary>
        public static sbyte Quantize(float value)
        {
            float clamped = Mathf.Clamp(value, -1f, 1f);
            return (sbyte)Mathf.RoundToInt(clamped * Scale);
        }

        /// <summary>Expands a signed byte back to [-1, 1].</summary>
        public static float Dequantize(sbyte quantized)
        {
            return quantized / Scale;
        }

        /// <summary>
        /// Resolves raw stick or WASD movement against a reference frame and returns the
        /// quantized-then-dequantized world-space XZ direction to simulate from.
        /// </summary>
        /// <param name="raw">Raw movement, x = strafe (right), y = forward.</param>
        /// <param name="frame">The reference frame, or null for the world frame.</param>
        /// <returns>The world-space direction, x in world X and y in world Z, safe to network.</returns>
        public static Vector2 EncodeMovement(Vector2 raw, ISimInputFrameProvider frame)
        {
            Vector3 forward;
            Vector3 right;
            if (frame == null || !frame.TryGetReferenceFrame(out forward, out right))
            {
                forward = Vector3.forward;
                right = Vector3.right;
            }

            Vector3 world = right * raw.x + forward * raw.y;
            float x = world.x;
            float z = world.z;

            // Clamp the magnitude, not the components, so a diagonal is not longer than a
            // cardinal — the same reason input is normalised before it is trusted.
            float magnitude = Mathf.Sqrt(x * x + z * z);
            if (magnitude > 1f)
            {
                x /= magnitude;
                z /= magnitude;
            }

            // The round trip: quantize as the wire will, then dequantize as the receivers
            // will, so this peer simulates from the receivers' value.
            return new Vector2(Dequantize(Quantize(x)), Dequantize(Quantize(z)));
        }

        /// <summary>
        /// Builds a complete input for a tick from raw movement, buttons and a reference frame.
        /// </summary>
        public static SimInput BuildInput(uint playerId, int tick, uint buttons, Vector2 rawMove, ISimInputFrameProvider frame)
        {
            Vector2 move = EncodeMovement(rawMove, frame);
            SimInput input = SimInput.Neutral(playerId, tick);
            input.Buttons = buttons;
            input.AxisX = move.x;
            input.AxisY = move.y;
            return input;
        }

        /// <summary>The world-space movement direction carried by an input, on the XZ plane.</summary>
        /// <remarks>
        /// The read side of the encoding: an entity's update calls this to get the direction
        /// to push itself, rather than reading the axes and reconstructing the vector every
        /// time.
        /// </remarks>
        public static Vector3 MovementDirection(SimInput input)
        {
            return new Vector3(input.AxisX, 0f, input.AxisY);
        }
    }
}
