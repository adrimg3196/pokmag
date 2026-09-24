#nullable enable
using System;
using System.Collections.Generic;

namespace HoloTable.Domain.Warhammer
{
    public enum MovementMode
    {
        Normal = 0,
        Advance = 1,
        FallBack = 2,
    }

    public enum Visibility
    {
        Clear = 0,
        PartialCover = 1,
        Blocked = 2,
    }

    /// <summary>Weapon profile. <see cref="ArmourPenetration"/> is stored positive (AP-2 → 2).</summary>
    public sealed record WeaponProfile(
        string Name,
        float RangeInches,
        int Attacks,
        int Skill,
        int Strength,
        int ArmourPenetration,
        int Damage,
        bool IsMelee);

    public sealed record UnitProfile(
        string Name,
        float MoveInches,
        int Toughness,
        int Save,
        int? InvulnerableSave,
        int Wounds,
        float BaseDiameterMm);

    public sealed record DicePhaseResult(IReadOnlyList<int> Rolls, int TargetNumber, int Successes)
    {
        public int Failures => Rolls.Count - Successes;
    }

    public sealed record AttackSummary(
        DicePhaseResult Hits,
        DicePhaseResult Wounds,
        DicePhaseResult Saves,
        int FailedSaves,
        int TotalDamage);

    public static class TableUnits
    {
        public const float MetersPerInch = 0.0254f;

        public static float InchesToMeters(float inches) => inches * MetersPerInch;

        public static float MetersToInches(float meters) => meters / MetersPerInch;

        public static float MmToMeters(float millimetres) => millimetres * 0.001f;
    }

    public static class MovementRules
    {
        public static float MaxMoveInches(UnitProfile unit, MovementMode mode, int advanceRoll = 0)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (mode == MovementMode.Advance && (advanceRoll < 1 || advanceRoll > 6))
            {
                throw new ArgumentOutOfRangeException(nameof(advanceRoll), advanceRoll, "Advance needs a D6 result.");
            }

            return mode == MovementMode.Advance ? unit.MoveInches + advanceRoll : unit.MoveInches;
        }

        /// <summary>
        /// Radius of the holographic ring measured from the model's centre. Movement is
        /// measured from the base edge, so the base radius is added.
        /// </summary>
        public static float RingRadiusMeters(UnitProfile unit, MovementMode mode, int advanceRoll = 0)
        {
            float move = TableUnits.InchesToMeters(MaxMoveInches(unit, mode, advanceRoll));
            return move + TableUnits.MmToMeters(unit.BaseDiameterMm) * 0.5f;
        }

        public static float RemainingInches(float allowedInches, float travelledMeters) =>
            allowedInches - TableUnits.MetersToInches(travelledMeters);
    }

    public static class WoundRules
    {
        /// <summary>Strength vs Toughness table (10th edition).</summary>
        public static int RequiredWoundRoll(int strength, int toughness)
        {
            if (strength <= 0) throw new ArgumentOutOfRangeException(nameof(strength));
            if (toughness <= 0) throw new ArgumentOutOfRangeException(nameof(toughness));

            if (strength >= toughness * 2) return 2;
            if (strength > toughness) return 3;
            if (strength == toughness) return 4;
            if (strength * 2 <= toughness) return 6;
            return 5;
        }

        /// <summary>Unmodified 1 always fails; unmodified 6 always succeeds.</summary>
        public static bool RollSucceeds(int roll, int targetNumber)
        {
            if (roll < 1 || roll > 6) throw new ArgumentOutOfRangeException(nameof(roll));
            if (roll == 1) return false;
            if (roll == 6) return true;
            return roll >= targetNumber;
        }

        /// <summary>
        /// Final save target. Cover gives +1 except to 3+ or better saves against AP0.
        /// Invulnerable saves ignore AP and cover. Returns 7 when no save is possible.
        /// </summary>
        public static int SaveTarget(int save, int armourPenetration, int? invulnerable, bool inCover)
        {
            int armour = save + armourPenetration;
            bool coverApplies = inCover && !(save <= 3 && armourPenetration == 0);
            if (coverApplies) armour -= 1;
            armour = Math.Max(2, armour);

            int best = invulnerable.HasValue ? Math.Min(armour, invulnerable.Value) : armour;
            return Math.Min(best, 7);
        }

        /// <summary>A save roll: unmodified 1 fails, otherwise must meet the target (7 = impossible).</summary>
        public static bool SaveSucceeds(int roll, int saveTarget)
        {
            if (roll < 1 || roll > 6) throw new ArgumentOutOfRangeException(nameof(roll));
            return roll != 1 && saveTarget <= 6 && roll >= saveTarget;
        }
    }

    public static class AttackResolver
    {
        public static DicePhaseResult ResolveHits(IReadOnlyList<int> rolls, int skill) =>
            Count(rolls, skill, WoundRules.RollSucceeds);

        public static DicePhaseResult ResolveWounds(IReadOnlyList<int> rolls, int strength, int toughness) =>
            Count(rolls, WoundRules.RequiredWoundRoll(strength, toughness), WoundRules.RollSucceeds);

        public static DicePhaseResult ResolveSaves(IReadOnlyList<int> rolls, int saveTarget) =>
            Count(rolls, saveTarget, WoundRules.SaveSucceeds);

        public static int Damage(int failedSaves, int damagePerWound) => Math.Max(0, failedSaves) * Math.Max(0, damagePerWound);

        /// <summary>Full automatic sequence (used when the player opts out of physical dice).</summary>
        public static AttackSummary ResolveAll(WeaponProfile weapon, UnitProfile target, bool inCover, IRandomSource random)
        {
            if (weapon == null) throw new ArgumentNullException(nameof(weapon));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (random == null) throw new ArgumentNullException(nameof(random));

            DicePhaseResult hits = ResolveHits(random.RollD6(weapon.Attacks), weapon.Skill);
            DicePhaseResult wounds = ResolveWounds(random.RollD6(hits.Successes), weapon.Strength, target.Toughness);
            // Benefit of Cover only applies against ranged attacks.
            int saveTarget = WoundRules.SaveTarget(target.Save, weapon.ArmourPenetration, target.InvulnerableSave, inCover && !weapon.IsMelee);
            DicePhaseResult saves = ResolveSaves(random.RollD6(wounds.Successes), saveTarget);

            return new AttackSummary(hits, wounds, saves, saves.Failures, Damage(saves.Failures, weapon.Damage));
        }

        private static DicePhaseResult Count(IReadOnlyList<int> rolls, int target, Func<int, int, bool> succeeds)
        {
            if (rolls == null) throw new ArgumentNullException(nameof(rolls));

            var copy = new int[rolls.Count];
            int successes = 0;
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = rolls[i];
                if (succeeds(copy[i], target)) successes++;
            }

            return new DicePhaseResult(Array.AsReadOnly(copy), target, successes);
        }
    }

    public static class CoverRules
    {
        /// <summary>
        /// Converts ray samples from the weapon to the target volume into visibility:
        /// all rays clear = Clear, some = PartialCover (benefit of cover), none = Blocked.
        /// </summary>
        public static Visibility Evaluate(int visibleSamples, int totalSamples)
        {
            if (totalSamples <= 0) throw new ArgumentOutOfRangeException(nameof(totalSamples));
            if (visibleSamples < 0 || visibleSamples > totalSamples) throw new ArgumentOutOfRangeException(nameof(visibleSamples));

            if (visibleSamples == 0) return Visibility.Blocked;
            return visibleSamples == totalSamples ? Visibility.Clear : Visibility.PartialCover;
        }

        public static bool InRange(float distanceMeters, WeaponProfile weapon)
        {
            if (weapon == null) throw new ArgumentNullException(nameof(weapon));
            return TableUnits.MetersToInches(distanceMeters) <= weapon.RangeInches + 1e-3f;
        }
    }
}
