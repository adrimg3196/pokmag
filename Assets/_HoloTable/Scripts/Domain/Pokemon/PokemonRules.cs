#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;

namespace HoloTable.Domain.Pokemon
{
    public enum EvolutionStage
    {
        Basic = 0,
        Stage1 = 1,
        Stage2 = 2,
    }

    /// <summary>Rules-relevant snapshot of a Pokémon card.</summary>
    public sealed record PokemonSpecies(
        string Name,
        EvolutionStage Stage,
        string? EvolvesFrom,
        ElementType Type,
        ElementType Weakness,
        ElementType Resistance,
        int Hp);

    public sealed record PokemonDamageResult(int Amount, bool AppliedWeakness, bool AppliedResistance);

    public static class EvolutionRules
    {
        /// <summary>A Stage N card evolves a Stage N-1 Pokémon whose name matches "Evolves from".</summary>
        public static bool CanEvolve(PokemonSpecies current, PokemonSpecies candidate)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));

            return !string.IsNullOrWhiteSpace(candidate.EvolvesFrom)
                && string.Equals(candidate.EvolvesFrom, current.Name, StringComparison.OrdinalIgnoreCase)
                && (int)candidate.Stage == (int)current.Stage + 1;
        }

        /// <summary>
        /// The new card was physically placed on top of the old one: the centres
        /// (projected on the table) are closer than a fraction of the card's short side.
        /// </summary>
        public static bool IsStackedOver(Vector2 existingCenter, Vector2 newCenter, float cardShortSide, float tolerance = 0.6f)
        {
            return Vector2.Distance(existingCenter, newCenter) <= cardShortSide * tolerance;
        }

        /// <summary>
        /// The old card was removed and the evolution put in (roughly) the same place
        /// within the replacement window.
        /// </summary>
        public static bool IsReplacement(float secondsSinceOldLost, float replaceWindow, float distance, float maxDistance)
        {
            return secondsSinceOldLost >= 0f
                && secondsSinceOldLost <= replaceWindow
                && distance <= maxDistance;
        }

        /// <summary>Damage counters stay on the Pokémon when it evolves.</summary>
        public static Vitals CarryDamage(Vitals previous, int newMaxHp) => previous.WithMaxKeepingDamage(newMaxHp);
    }

    public static class PokemonDamageCalculator
    {
        public const int WeaknessMultiplier = 2;
        public const int ResistanceReduction = 30;
        public const int DamagePerCounter = 10;

        public static PokemonDamageResult Calculate(int baseDamage, ElementType attackerType, PokemonSpecies defender)
        {
            if (baseDamage < 0) throw new ArgumentOutOfRangeException(nameof(baseDamage));
            if (defender == null) throw new ArgumentNullException(nameof(defender));

            bool weak = IsMatch(attackerType, defender.Weakness);
            bool resist = IsMatch(attackerType, defender.Resistance);

            int amount = baseDamage;
            if (weak) amount *= WeaknessMultiplier;
            if (resist) amount -= ResistanceReduction;

            return new PokemonDamageResult(Math.Max(0, amount), weak, resist);
        }

        public static int ToDamageCounters(int damage) => Math.Max(0, damage) / DamagePerCounter;

        private static bool IsMatch(ElementType attacker, ElementType defenderAttribute) =>
            defenderAttribute != ElementType.None
            && defenderAttribute != ElementType.Colorless
            && attacker == defenderAttribute;
    }

    public static class EnergyRules
    {
        /// <summary>
        /// Checks an attack cost against attached energy. Typed symbols need a matching
        /// energy; Colorless symbols can be paid with any leftover energy.
        /// </summary>
        public static bool CanPayCost(IReadOnlyList<ElementType> attached, IReadOnlyList<ElementType> cost)
        {
            if (attached == null) throw new ArgumentNullException(nameof(attached));
            if (cost == null) throw new ArgumentNullException(nameof(cost));

            var pool = new Dictionary<ElementType, int>();
            foreach (ElementType e in attached)
            {
                pool.TryGetValue(e, out int n);
                pool[e] = n + 1;
            }

            int colorlessNeeded = 0;
            foreach (ElementType symbol in cost)
            {
                if (symbol == ElementType.Colorless || symbol == ElementType.None)
                {
                    colorlessNeeded++;
                    continue;
                }

                if (!pool.TryGetValue(symbol, out int available) || available == 0)
                {
                    return false;
                }

                pool[symbol] = available - 1;
            }

            int leftover = 0;
            foreach (int n in pool.Values) leftover += n;
            return leftover >= colorlessNeeded;
        }
    }
}
