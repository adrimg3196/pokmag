using System.Collections.Generic;
using System.Numerics;

namespace HoloTable.Domain.Combat
{
    public readonly struct LookCandidate
    {
        public LookCandidate(int id, Vector3 position, PlayerSide side, bool isAlive)
        {
            Id = id;
            Position = position;
            Side = side;
            IsAlive = isAlive;
        }

        public int Id { get; }
        public Vector3 Position { get; }
        public PlayerSide Side { get; }
        public bool IsAlive { get; }
    }

    /// <summary>
    /// Chooses what a creature stares at: the closest living rival inside its awareness
    /// radius, otherwise nothing (the caller falls back to the player's head / camera).
    /// </summary>
    public static class LookTargetSelector
    {
        public const int NoTarget = -1;

        public static int SelectNearestRival(
            int selfId,
            Vector3 selfPosition,
            PlayerSide selfSide,
            IReadOnlyList<LookCandidate> candidates,
            float awarenessRadius)
        {
            int bestId = NoTarget;
            float bestSqr = awarenessRadius * awarenessRadius;

            for (int i = 0; i < candidates.Count; i++)
            {
                LookCandidate c = candidates[i];
                if (c.Id == selfId || !c.IsAlive || !selfSide.IsRivalOf(c.Side))
                {
                    continue;
                }

                float sqr = Vector3.DistanceSquared(selfPosition, c.Position);
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    bestId = c.Id;
                }
            }

            return bestId;
        }
    }
}
