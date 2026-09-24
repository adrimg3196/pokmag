#nullable enable
using System;

namespace HoloTable.Domain
{
    /// <summary>
    /// Immutable hit-point state (HP in Pokémon, toughness in MTG, Wounds in Warhammer).
    /// Every operation returns a new instance, so HUDs and replays can diff old vs new.
    /// </summary>
    public sealed record Vitals
    {
        public int Max { get; }
        public int Current { get; }

        private Vitals(int max, int current)
        {
            Max = max;
            Current = current;
        }

        public static Vitals Full(int max)
        {
            if (max <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(max), max, "Max HP must be positive.");
            }

            return new Vitals(max, max);
        }

        public bool IsDefeated => Current <= 0;

        public int Missing => Max - Current;

        public float Fraction => (float)Current / Max;

        public Vitals WithDamage(int amount)
        {
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "Damage cannot be negative.");
            }

            return new Vitals(Max, Math.Max(0, Current - amount));
        }

        public Vitals WithHealing(int amount)
        {
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "Healing cannot be negative.");
            }

            return new Vitals(Max, Math.Min(Max, Current + amount));
        }

        /// <summary>
        /// Changes the maximum while keeping the damage already taken
        /// (Pokémon evolution keeps its damage counters).
        /// </summary>
        public Vitals WithMaxKeepingDamage(int newMax)
        {
            return Full(newMax).WithDamage(Math.Min(Missing, newMax));
        }
    }
}
