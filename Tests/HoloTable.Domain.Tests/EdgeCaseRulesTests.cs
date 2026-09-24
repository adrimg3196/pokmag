using System;
using System.Collections.Generic;
using System.Numerics;
using HoloTable.Domain;
using HoloTable.Domain.Combat;
using HoloTable.Domain.Mtg;
using HoloTable.Domain.Pokemon;
using HoloTable.Domain.Warhammer;
using Xunit;

namespace HoloTable.Domain.Tests
{
    // ---------------- MtgRulesTests.cs additions ----------------
    public sealed class MtgCombatEdgeCaseTests
    {
        private static readonly MtgCreature Bear = new MtgCreature("Grizzly Bears", 2, 2, MtgKeyword.None);

        [Fact]
        public void BlockerFirstStrike_KillsAttacker_BeforeItDealsDamage()
        {
            var attacker = new MtgCreature("Vampire Wurm", 6, 2, MtgKeyword.Trample | MtgKeyword.Lifelink);
            var blocker = new MtgCreature("Knight", 2, 2, MtgKeyword.FirstStrike);

            MtgCombatOutcome o = MtgCombatRules.Resolve(attacker, blocker);

            Assert.True(o.AttackerDies);
            Assert.False(o.BlockerDies);
            Assert.Equal(0, o.DamageToBlocker);
            Assert.Equal(0, o.DamageToDefendingPlayer);
            Assert.Equal(0, o.LifeGainedByAttacker);
        }

        [Fact]
        public void BlockerFirstStrikeDeathtouch_KillsBigAttackerUnharmed()
        {
            var attacker = new MtgCreature("Colossus", 10, 10, MtgKeyword.None);
            var blocker = new MtgCreature("Assassin", 1, 1, MtgKeyword.FirstStrike | MtgKeyword.Deathtouch);

            MtgCombatOutcome o = MtgCombatRules.Resolve(attacker, blocker);

            Assert.True(o.AttackerDies);
            Assert.False(o.BlockerDies);
            Assert.Equal(0, o.DamageToBlocker);
        }

        [Fact]
        public void AttackerFirstStrike_DoesNotSaveIt_WhenBlockerSurvivesFirstStrikeDamage()
        {
            var attacker = new MtgCreature("Squire", 1, 1, MtgKeyword.FirstStrike | MtgKeyword.Deathtouch);
            var blocker = new MtgCreature("Wall", 0, 8, MtgKeyword.Defender);
            var ogre = new MtgCreature("Ogre", 3, 3, MtgKeyword.None);
            var knight = new MtgCreature("Knight", 2, 2, MtgKeyword.FirstStrike);

            MtgCombatOutcome deathtouchFs = MtgCombatRules.Resolve(attacker, blocker);
            MtgCombatOutcome survives = MtgCombatRules.Resolve(knight, ogre);

            Assert.True(deathtouchFs.BlockerDies);
            Assert.False(deathtouchFs.AttackerDies);
            Assert.False(survives.BlockerDies);   // 2 first-strike damage on a 3/3
            Assert.True(survives.AttackerDies);   // ogre strikes back in the regular step
            Assert.Equal(3, survives.DamageToAttacker);
        }

        [Fact]
        public void BothFirstStrike_DealDamageSimultaneously()
        {
            var a = new MtgCreature("Knight A", 2, 2, MtgKeyword.FirstStrike);
            var b = new MtgCreature("Knight B", 2, 2, MtgKeyword.FirstStrike);

            MtgCombatOutcome o = MtgCombatRules.Resolve(a, b);

            Assert.True(o.AttackerDies);
            Assert.True(o.BlockerDies);
        }

        [Fact]
        public void Lifelink_WhenBlocked_GainsAllDamageDealtToBlocker_EvenBeyondLethal()
        {
            // CR 702.15b: lifelink gains life equal to damage dealt, not lethal damage.
            var vampire = new MtgCreature("Vampire", 5, 5, MtgKeyword.Lifelink);

            MtgCombatOutcome o = MtgCombatRules.Resolve(vampire, Bear);

            Assert.Equal(5, o.DamageToBlocker);
            Assert.Equal(5, o.LifeGainedByAttacker);
            Assert.Equal(0, o.DamageToDefendingPlayer);
        }

        [Fact]
        public void TrampleLifelink_GainsBlockerPlusPlayerDamage()
        {
            var wurm = new MtgCreature("Wurm", 6, 6, MtgKeyword.Trample | MtgKeyword.Lifelink);

            MtgCombatOutcome o = MtgCombatRules.Resolve(wurm, Bear);

            Assert.Equal(2, o.DamageToBlocker);
            Assert.Equal(4, o.DamageToDefendingPlayer);
            Assert.Equal(6, o.LifeGainedByAttacker);
        }

        [Fact]
        public void Trample_CountsDamageAlreadyMarkedOnBlocker()
        {
            // CR 702.19c: lethal damage accounts for damage already dealt this turn.
            var wurm = new MtgCreature("Wurm", 6, 6, MtgKeyword.Trample);
            var spider = new MtgCreature("Giant Spider", 2, 4, MtgKeyword.Reach);

            MtgCombatOutcome o = MtgCombatRules.Resolve(wurm, spider, blockerDamageTaken: 3);

            Assert.Equal(1, o.DamageToBlocker);
            Assert.Equal(5, o.DamageToDefendingPlayer);
            Assert.True(o.BlockerDies);
        }

        [Fact]
        public void ZeroPowerAttacker_WithDeathtouch_KillsNothing()
        {
            var attacker = new MtgCreature("Fangless Snake", 0, 1, MtgKeyword.Deathtouch | MtgKeyword.Lifelink | MtgKeyword.Trample);

            MtgCombatOutcome blocked = MtgCombatRules.Resolve(attacker, Bear);
            MtgCombatOutcome unblocked = MtgCombatRules.Resolve(attacker, null);

            Assert.False(blocked.BlockerDies);
            Assert.Equal(0, blocked.DamageToBlocker);
            Assert.Equal(0, blocked.DamageToDefendingPlayer);
            Assert.Equal(0, blocked.LifeGainedByAttacker);
            Assert.True(blocked.AttackerDies);
            Assert.Equal(0, unblocked.DamageToDefendingPlayer);
        }

        [Fact]
        public void ChooseBestBlocker_ReturnsMinusOne_WhenNoLegalBlocker()
        {
            var dragon = new MtgCreature("Shivan Dragon", 5, 5, MtgKeyword.Flying);

            Assert.Equal(-1, MtgCombatRules.ChooseBestBlocker(dragon, Array.Empty<MtgCreature>()));
            Assert.Equal(-1, MtgCombatRules.ChooseBestBlocker(dragon, new[] { Bear }));
        }

        [Theory]
        // Printed cards carry reminder text; it must not grant the keyword it mentions.
        [InlineData("Reach (This creature can block creatures with flying.)", MtgKeyword.Reach)]
        [InlineData("Alcance. (Esta criatura puede bloquear criaturas con la habilidad de volar.)", MtgKeyword.Reach)]
        [InlineData("Flying (This creature can't be blocked except by creatures with flying or reach.)", MtgKeyword.Flying)]
        public void Parse_IgnoresKeywordsMentionedInsideReminderText(string text, MtgKeyword expected)
        {
            Assert.Equal(expected, MtgKeywordParser.Parse(text));
        }
    }

    // ---------------- MtgRulesTests.cs (TapRulesTests) additions ----------------
    public sealed class TapRulesBoundaryTests
    {
        [Theory]
        [InlineData(0f, 65f, true)]     // exactly at tap threshold
        [InlineData(0f, 64.9f, false)]
        [InlineData(0f, -65f, true)]    // counter-clockwise tap
        [InlineData(0f, 115f, true)]    // folds to 65
        [InlineData(350f, 55f, true)]   // wraps: 65 degrees clockwise
        [InlineData(-30f, -300f, true)] // negative reference/current: delta 90
        public void Tap_ThresholdIsInclusive_AndSymmetric(float reference, float current, bool tapped)
        {
            (TapState next, _) = TapRules.Evaluate(new TapState(false, reference), current);

            Assert.Equal(tapped, next.IsTapped);
        }

        [Theory]
        [InlineData(25f, TapTransition.Untapped)]   // exactly at untap threshold
        [InlineData(25.1f, TapTransition.None)]
        [InlineData(155f, TapTransition.Untapped)]  // upside-down folds to 25
        [InlineData(-25f, TapTransition.Untapped)]
        public void Untap_ThresholdIsInclusive(float current, TapTransition expected)
        {
            Assert.Equal(expected, TapRules.Evaluate(new TapState(true, 0f), current).Transition);
        }

        [Fact]
        public void Evaluate_RejectsInvalidThresholdsAndNullState()
        {
            Assert.Throws<ArgumentException>(() => TapRules.Evaluate(new TapState(false, 0f), 90f, 40f, 40f));
            Assert.Throws<ArgumentException>(() => TapRules.Evaluate(new TapState(false, 0f), 90f, 30f, 60f));
            Assert.Throws<ArgumentNullException>(() => TapRules.Evaluate(null!, 90f));
        }
    }

    // ---------------- WarhammerRulesTests.cs additions ----------------
    public sealed class WarhammerEdgeCaseTests
    {
        private static readonly UnitProfile Intercessor = new UnitProfile("Intercessor", 6f, 4, 3, null, 2, 32f);
        private static readonly UnitProfile Guardsman = new UnitProfile("Guardsman", 6f, 3, 5, null, 1, 25f);

        [Theory]
        [InlineData(3, 6, 6)]  // exactly half → 6+
        [InlineData(3, 7, 6)]
        [InlineData(4, 7, 5)]  // more than half → 5+
        [InlineData(7, 4, 3)]
        [InlineData(1, 1, 4)]
        [InlineData(12, 6, 2)] // exactly double → 2+
        [InlineData(11, 6, 3)]
        public void WoundTable_Boundaries(int s, int t, int expected)
        {
            Assert.Equal(expected, WoundRules.RequiredWoundRoll(s, t));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(7)]
        [InlineData(-1)]
        public void Rolls_OutsideD6_AreRejected(int roll)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => WoundRules.RollSucceeds(roll, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => WoundRules.SaveSucceeds(roll, 4));
        }

        [Theory]
        [InlineData(3, 2, 5, true, 4)]  // cover improves armour (3+2-1=4) which beats the 5++
        [InlineData(3, 3, 4, true, 4)]  // armour 5+ in cover; 4++ is NOT improved by cover
        [InlineData(2, 0, 4, true, 2)]  // 2+ vs AP0: no cover, armour wins
        [InlineData(3, 1, null, true, 3)] // 3+ save DOES get cover when AP is not 0
        public void SaveTarget_CoverOnlyImprovesArmour(int save, int ap, int? invul, bool cover, int expected)
        {
            Assert.Equal(expected, WoundRules.SaveTarget(save, ap, invul, cover));
        }

        [Fact]
        public void ResolveAll_ZeroHits_RollsNoFurtherDice()
        {
            // ScriptedRandom throws (empty queue) if any wound/save dice are requested.
            var bolter = new WeaponProfile("Bolt rifle", 24f, 2, 3, 4, 1, 1, false);
            var dice = new ScriptedRandom(1, 2);

            AttackSummary s = AttackResolver.ResolveAll(bolter, Intercessor, inCover: false, dice);

            Assert.Equal(0, s.Hits.Successes);
            Assert.Empty(s.Wounds.Rolls);
            Assert.Empty(s.Saves.Rolls);
            Assert.Equal(0, s.TotalDamage);
        }

        [Fact]
        public void ResolveAll_MeleeAttack_IgnoresBenefitOfCover()
        {
            // 10th ed core rules: Benefit of Cover only applies against ranged attacks.
            // FAILS TODAY: ResolveAll forwards inCover to SaveTarget regardless of weapon.IsMelee.
            var chainsword = new WeaponProfile("Chainsword", 0f, 1, 3, 4, 0, 1, true);
            var dice = new ScriptedRandom(6, 6, 4); // hit, wound, save roll of 4

            AttackSummary s = AttackResolver.ResolveAll(chainsword, Guardsman, inCover: true, dice);

            Assert.Equal(5, s.Saves.TargetNumber);
            Assert.Equal(1, s.TotalDamage);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(7)]
        public void Advance_RejectsNonD6Roll(int roll)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MovementRules.MaxMoveInches(Intercessor, MovementMode.Advance, roll));
        }

        [Fact]
        public void NormalAndFallBack_IgnoreAdvanceRoll()
        {
            Assert.Equal(6f, MovementRules.MaxMoveInches(Intercessor, MovementMode.Normal, 5));
            Assert.Equal(6f, MovementRules.MaxMoveInches(Intercessor, MovementMode.FallBack));
        }

        private sealed class ScriptedRandom : IRandomSource
        {
            private readonly Queue<int> _values;

            public ScriptedRandom(params int[] values) => _values = new Queue<int>(values);

            public int Range(int minInclusive, int maxExclusive) => _values.Dequeue();
        }
    }

    // ---------------- PokemonRulesTests.cs additions ----------------
    public sealed class PokemonEdgeCaseTests
    {
        private static readonly PokemonSpecies Charizard =
            new PokemonSpecies("Charizard", EvolutionStage.Stage2, "Charmeleon", ElementType.Fire, ElementType.Water, ElementType.None, 180);

        [Fact]
        public void EnergyCost_TypedSymbolsArePaidFirst_RegardlessOfOrder()
        {
            var attached = new List<ElementType> { ElementType.Fire, ElementType.Water };

            Assert.True(EnergyRules.CanPayCost(attached, new[] { ElementType.Colorless, ElementType.Fire }));
            Assert.False(EnergyRules.CanPayCost(attached, new[] { ElementType.Colorless, ElementType.Colorless, ElementType.Fire }));
        }

        [Fact]
        public void EnergyCost_EmptyCostIsFree_AndNoneSymbolActsAsColorless()
        {
            Assert.True(EnergyRules.CanPayCost(Array.Empty<ElementType>(), Array.Empty<ElementType>()));
            Assert.False(EnergyRules.CanPayCost(Array.Empty<ElementType>(), new[] { ElementType.None }));
            Assert.True(EnergyRules.CanPayCost(new[] { ElementType.Grass }, new[] { ElementType.None }));
        }

        [Fact]
        public void ZeroBaseDamage_DoesNotApplyWeakness()
        {
            // Rulebook: an attack that does no damage does not apply Weakness/Resistance.
            // FAILS TODAY: AppliedWeakness is true, so the HUD shows "¡Es súper eficaz!" for 0 damage.
            PokemonDamageResult r = PokemonDamageCalculator.Calculate(0, ElementType.Water, Charizard);

            Assert.Equal(0, r.Amount);
            Assert.False(r.AppliedWeakness);
        }
    }

    // ---------------- CommonRulesTests.cs additions ----------------
    public sealed class CommonEdgeCaseTests
    {
        [Fact]
        public void WithMaxKeepingDamage_KnocksOut_WhenDamageMeetsNewMax()
        {
            Vitals damaged = Vitals.Full(120).WithDamage(90);

            Vitals devolved = damaged.WithMaxKeepingDamage(70);

            Assert.Equal(70, devolved.Max);
            Assert.Equal(0, devolved.Current);
            Assert.True(devolved.IsDefeated);
            Assert.Throws<ArgumentOutOfRangeException>(() => damaged.WithMaxKeepingDamage(0));
        }

        [Fact]
        public void WithHealing_RejectsNegative()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Vitals.Full(5).WithHealing(-1));
        }

        [Fact]
        public void Colossal_IsNeverClampedToItsCard()
        {
            ScaleProfile p = ScaleRules.Compute(nativeHeight: 1f, nativeFootprint: 2f, 0.063f, SizeClass.Colossal);

            Assert.False(ScaleRules.MustFitFootprint(SizeClass.Colossal));
            Assert.Equal(0.40f, p.WorldHeight, 4);
            Assert.True(p.OverflowsFootprint);
            Assert.Throws<ArgumentOutOfRangeException>(() => ScaleRules.Compute(1f, 1f, 1f, SizeClass.Small, multiplier: 0f));
        }

        [Fact]
        public void FindEngagements_RejectsNullArgs_ClearsResults_AndIsInclusiveAtRange()
        {
            var combatants = new List<Combatant>
            {
                new Combatant(1, PlayerSide.PlayerOne, Vector2.Zero, 0.5f),
                new Combatant(2, PlayerSide.PlayerTwo, new Vector2(0.5f, 0f), 0.5f),
            };
            var results = new List<Engagement> { new Engagement(8, 9, 0f) };

            EngagementRules.FindEngagements(combatants, results);

            Assert.Equal(new[] { new Engagement(1, 2, 0f) }, results);
            Assert.Throws<ArgumentNullException>(() => EngagementRules.FindEngagements(null!, results));
            Assert.Throws<ArgumentNullException>(() => EngagementRules.FindEngagements(combatants, null!));
        }
    }
}
