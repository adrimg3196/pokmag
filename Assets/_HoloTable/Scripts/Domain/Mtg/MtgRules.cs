#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace HoloTable.Domain.Mtg
{
    [Flags]
    public enum MtgKeyword
    {
        None = 0,
        Flying = 1 << 0,
        Reach = 1 << 1,
        Vigilance = 1 << 2,
        Trample = 1 << 3,
        Deathtouch = 1 << 4,
        Lifelink = 1 << 5,
        FirstStrike = 1 << 6,
        Haste = 1 << 7,
        Defender = 1 << 8,
    }

    public enum MtgCardKind
    {
        Creature = 0,
        Instant = 1,
        Sorcery = 2,
        Land = 3,
        Artifact = 4,
        Enchantment = 5,
        Planeswalker = 6,
    }

    public sealed record MtgCreature(string Name, int Power, int Toughness, MtgKeyword Keywords)
    {
        public bool Has(MtgKeyword keyword) => (Keywords & keyword) == keyword;
    }

    public sealed record MtgCombatOutcome(
        bool Blocked,
        int DamageToBlocker,
        int DamageToAttacker,
        int DamageToDefendingPlayer,
        int LifeGainedByAttacker,
        bool BlockerDies,
        bool AttackerDies);

    /// <summary>
    /// Extracts evergreen keywords from Oracle text in English and Spanish
    /// ("Flying" / "Vuela"), so both card printings drive the same behaviour.
    /// </summary>
    public static class MtgKeywordParser
    {
        private static readonly (MtgKeyword Keyword, Regex Pattern)[] Patterns =
        {
            (MtgKeyword.Flying, Build("flying|vuela|volar")),
            (MtgKeyword.Reach, Build("reach|alcance")),
            (MtgKeyword.Vigilance, Build("vigilance|vigilancia")),
            (MtgKeyword.Trample, Build("trample|arrolla|arrollar")),
            (MtgKeyword.Deathtouch, Build("deathtouch|toque\\s+mortal|toque\\s+letal")),
            (MtgKeyword.Lifelink, Build("lifelink|v[ií]nculo\\s+vital")),
            (MtgKeyword.FirstStrike, Build("first\\s+strike|da[nñ]a\\s+primero")),
            (MtgKeyword.Haste, Build("haste|prisa")),
            (MtgKeyword.Defender, Build("defender|defensor")),
        };

        public static MtgKeyword Parse(string? rulesText)
        {
            if (string.IsNullOrWhiteSpace(rulesText))
            {
                return MtgKeyword.None;
            }

            MtgKeyword result = MtgKeyword.None;
            foreach ((MtgKeyword keyword, Regex pattern) in Patterns)
            {
                if (pattern.IsMatch(rulesText))
                {
                    result |= keyword;
                }
            }

            return result;
        }

        private static Regex Build(string alternatives) =>
            new Regex($"(?<![\\p{{L}}])({alternatives})(?![\\p{{L}}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    public static class MtgCombatRules
    {
        public static bool CanAttack(MtgCreature creature, bool summoningSick) =>
            !creature.Has(MtgKeyword.Defender) && (!summoningSick || creature.Has(MtgKeyword.Haste));

        /// <summary>A flier can only be blocked by creatures with flying or reach.</summary>
        public static bool CanBlock(MtgCreature attacker, MtgCreature blocker) =>
            !attacker.Has(MtgKeyword.Flying)
            || blocker.Has(MtgKeyword.Flying)
            || blocker.Has(MtgKeyword.Reach);

        /// <summary>
        /// Picks the blocker that survives and deals the most damage; ties broken by list order.
        /// Used by the "auto-block" helper, never mandatory.
        /// </summary>
        public static int ChooseBestBlocker(MtgCreature attacker, IReadOnlyList<MtgCreature> candidates, int attackerDamageTaken = 0)
        {
            int best = -1;
            int bestScore = int.MinValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                MtgCreature blocker = candidates[i];
                if (!CanBlock(attacker, blocker)) continue;

                MtgCombatOutcome o = Resolve(attacker, blocker, attackerDamageTaken);
                int score = (o.BlockerDies ? 0 : 100) + (o.AttackerDies ? 50 : 0) + blocker.Power;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return best;
        }

        /// <param name="attacker">Attacking creature.</param>
        /// <param name="blocker">Blocking creature, or null when unblocked.</param>
        /// <param name="attackerDamageTaken">Damage already marked on the attacker this turn.</param>
        /// <param name="blockerDamageTaken">Damage already marked on the blocker this turn.</param>
        public static MtgCombatOutcome Resolve(
            MtgCreature attacker,
            MtgCreature? blocker,
            int attackerDamageTaken = 0,
            int blockerDamageTaken = 0)
        {
            if (attacker == null) throw new ArgumentNullException(nameof(attacker));

            int power = Math.Max(0, attacker.Power);
            int lifelinkGain = attacker.Has(MtgKeyword.Lifelink) ? power : 0;

            if (blocker == null)
            {
                return new MtgCombatOutcome(false, 0, 0, power, lifelinkGain, false, false);
            }

            int blockerRemaining = Math.Max(0, blocker.Toughness - blockerDamageTaken);
            int attackerRemaining = Math.Max(0, attacker.Toughness - attackerDamageTaken);

            int lethalToBlocker = attacker.Has(MtgKeyword.Deathtouch) ? Math.Min(1, blockerRemaining) : blockerRemaining;
            int toBlocker = power;
            int trampleOver = 0;
            if (attacker.Has(MtgKeyword.Trample) && power > lethalToBlocker)
            {
                toBlocker = lethalToBlocker;
                trampleOver = power - lethalToBlocker;
            }

            bool blockerDies = toBlocker > 0 && (toBlocker >= blockerRemaining || attacker.Has(MtgKeyword.Deathtouch));

            bool attackerStrikesFirst = attacker.Has(MtgKeyword.FirstStrike) && !blocker.Has(MtgKeyword.FirstStrike);
            bool blockerStrikesFirst = blocker.Has(MtgKeyword.FirstStrike) && !attacker.Has(MtgKeyword.FirstStrike);

            int toAttacker = Math.Max(0, blocker.Power);
            if (attackerStrikesFirst && blockerDies)
            {
                toAttacker = 0;
            }

            bool attackerDies = toAttacker > 0 && (toAttacker >= attackerRemaining || blocker.Has(MtgKeyword.Deathtouch));

            if (blockerStrikesFirst && attackerDies)
            {
                toBlocker = 0;
                trampleOver = 0;
                blockerDies = false;
                lifelinkGain = 0;
            }

            return new MtgCombatOutcome(true, toBlocker, toAttacker, trampleOver, lifelinkGain, blockerDies, attackerDies);
        }
    }
}
