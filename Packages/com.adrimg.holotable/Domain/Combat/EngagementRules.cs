#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;

namespace HoloTable.Domain.Combat
{
    /// <summary>A combatant projected onto the 2D table plane (metres).</summary>
    public readonly struct Combatant
    {
        public Combatant(int id, PlayerSide side, Vector2 tablePosition, float engagementRadius)
        {
            Id = id;
            Side = side;
            TablePosition = tablePosition;
            EngagementRadius = engagementRadius;
        }

        public int Id { get; }
        public PlayerSide Side { get; }
        public Vector2 TablePosition { get; }

        /// <summary>How close a rival must be to engage this unit (game specific).</summary>
        public float EngagementRadius { get; }
    }

    /// <summary>Unordered pair of engaged rivals. <see cref="FirstId"/> is always the lower id.</summary>
    public readonly struct Engagement : IEquatable<Engagement>
    {
        public Engagement(int a, int b, float distance)
        {
            FirstId = Math.Min(a, b);
            SecondId = Math.Max(a, b);
            Distance = distance;
        }

        public int FirstId { get; }
        public int SecondId { get; }
        public float Distance { get; }

        public bool Involves(int id) => FirstId == id || SecondId == id;

        public bool Equals(Engagement other) => FirstId == other.FirstId && SecondId == other.SecondId;

        public override bool Equals(object? obj) => obj is Engagement other && Equals(other);

        public override int GetHashCode() => (FirstId * 397) ^ SecondId;
    }

    public static class EngagementRules
    {
        /// <summary>
        /// Finds every rival pair whose table distance is within the larger of both
        /// engagement radii. O(n²) is fine: a table rarely holds more than ~40 entities.
        /// </summary>
        public static void FindEngagements(IReadOnlyList<Combatant> combatants, List<Engagement> results)
        {
            if (combatants == null) throw new ArgumentNullException(nameof(combatants));
            if (results == null) throw new ArgumentNullException(nameof(results));

            results.Clear();
            for (int i = 0; i < combatants.Count; i++)
            {
                Combatant a = combatants[i];
                for (int j = i + 1; j < combatants.Count; j++)
                {
                    Combatant b = combatants[j];
                    if (!a.Side.IsRivalOf(b.Side))
                    {
                        continue;
                    }

                    float range = Math.Max(a.EngagementRadius, b.EngagementRadius);
                    float distance = Vector2.Distance(a.TablePosition, b.TablePosition);
                    if (distance <= range)
                    {
                        results.Add(new Engagement(a.Id, b.Id, distance));
                    }
                }
            }
        }
    }
}
