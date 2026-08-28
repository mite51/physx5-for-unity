namespace PhysX5ForUnity
{
    /// <summary>
    /// The collision group table that decides which pairs of shapes are allowed to touch.
    /// </summary>
    /// <remarks>
    /// <para>Scenes run PhysX's default simulation filter shader, which reads a shape's
    /// simulation filter data word0 as a group <i>index</i> in 0..<see cref="MaxGroups"/>-1
    /// rather than as a bitmask, and suppresses a pair when the table says those two groups do
    /// not collide. Every pair starts out colliding, which is why shapes that never set a group
    /// collide with everything.</para>
    ///
    /// <para><b>This table is process-global.</b> It lives in PhysX's extensions library, not in
    /// a scene, so it is shared by every world in the process and it outlives any one of them.
    /// Two consequences follow. In the editor it survives leaving Play mode, so call
    /// <see cref="ResetToDefault"/> on teardown rather than leaving a session's filtering behind
    /// for whatever runs next. And in a networked deterministic session every peer must
    /// configure it identically: differing tables make peers simulate differently while every
    /// actor, shape and geometry still hashes the same. The construction hash folds the table in
    /// for exactly that reason, so a disagreement surfaces at the hash exchange instead of as an
    /// unexplained desync later. Configure it from authored data during world construction, not
    /// from gameplay code that runs at different times on different machines.</para>
    ///
    /// <para>The main use is keeping a shape out of a collision that something else already
    /// resolves. Vehicle wheels are the motivating case: the suspension raycast and tire model
    /// fully handle the wheel-versus-road interaction, so a wheel that is also a simulation
    /// shape must not collide with the road, or the road is resolved twice and the handling
    /// model fights the contact solver.</para>
    /// </remarks>
    public static class PhysxCollisionGroups
    {
        /// <summary>Number of distinct groups the table addresses. Fixed by PhysX.</summary>
        public const int MaxGroups = 32;

        /// <summary>The group shapes use when nothing assigns one. Collides with everything.</summary>
        public const int DefaultGroup = 0;

        /// <summary>
        /// Sets whether shapes in <paramref name="group0"/> collide with shapes in
        /// <paramref name="group1"/>. Symmetric: the pair is one entry, in either order.
        /// </summary>
        public static void SetPairCollides(int group0, int group1, bool collides)
        {
            if (!IsValidGroup(group0) || !IsValidGroup(group1))
            {
                UnityEngine.Debug.LogError(
                    $"PhysxCollisionGroups: group index out of range ({group0}, {group1}); " +
                    $"valid groups are 0..{MaxGroups - 1}.");
                return;
            }
            Physx.SetGroupCollisionFlag((uint)group0, (uint)group1, collides);
        }

        /// <summary>
        /// Whether shapes in <paramref name="group0"/> currently collide with shapes in
        /// <paramref name="group1"/>.
        /// </summary>
        public static bool GetPairCollides(int group0, int group1)
        {
            if (!IsValidGroup(group0) || !IsValidGroup(group1)) return true;
            return Physx.GetGroupCollisionFlag((uint)group0, (uint)group1);
        }

        /// <summary>
        /// Restores the all-pairs-collide default. Call this when tearing a session down so the
        /// table does not leak into the next one running in the same process.
        /// </summary>
        public static void ResetToDefault()
        {
            Physx.ResetGroupCollisionFlags();
        }

        public static bool IsValidGroup(int group)
        {
            return group >= 0 && group < MaxGroups;
        }
    }
}
