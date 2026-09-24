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
    /// <summary>Regression tests for findings raised by the ECC csharp-reviewer / pr-test-analyzer passes.</summary>
    public sealed class ReviewFindingsTests
    {
        private static readonly MtgCreature Bear = new MtgCreature("Grizzly Bears", 2, 2, MtgKeyword.None);

        [Fact]
        public void BlockerLifelink_GainsLifeForTheDefendingPlayer()
        {
            // CR 702.15b: any damage dealt by a lifelink source gains its controller that much life.
            var blocker = new MtgCreature("Vampire", 3, 3, MtgKeyword.Lifelink);

            MtgCombatOutcome o = MtgCombatRules.Resolve(Bear, blocker);

            Assert.Equal(3, o.LifeGainedByDefender);
            Assert.Equal(0, o.LifeGainedByAttacker);
        }

        [Fact]
        public void BlockerLifelink_KilledByFirstStrike_GainsNothing()
        {
            var knight = new MtgCreature("Knight", 3, 3, MtgKeyword.FirstStrike);
            var blocker = new MtgCreature("Vampire", 2, 2, MtgKeyword.Lifelink);

            MtgCombatOutcome o = MtgCombatRules.Resolve(knight, blocker);

            Assert.Equal(0, o.LifeGainedByDefender);
        }

        [Fact]
        public void Has_None_IsFalse()
        {
            Assert.False(Bear.Has(MtgKeyword.None));
        }

        [Fact]
        public void SpanishVolar_IsNotAKeyword()
        {
            Assert.Equal(MtgKeyword.None, MtgKeywordParser.Parse("Puede bloquear criaturas con la habilidad de volar"));
            Assert.Equal(MtgKeyword.Flying, MtgKeywordParser.Parse("Vuela."));
        }

        [Fact]
        public void PublicDomainApis_RejectNullArguments()
        {
            Assert.Throws<ArgumentNullException>(() => LookTargetSelector.SelectNearestRival(1, Vector3.Zero, PlayerSide.PlayerOne, null!, 1f));
            Assert.Throws<ArgumentNullException>(() => MtgCombatRules.CanBlock(null!, Bear));
            Assert.Throws<ArgumentNullException>(() => MtgCombatRules.CanBlock(Bear, null!));
            Assert.Throws<ArgumentNullException>(() => MtgCombatRules.CanAttack(null!, false));
            Assert.Throws<ArgumentNullException>(() => MtgCombatRules.ChooseBestBlocker(Bear, null!));
            Assert.Throws<ArgumentNullException>(() => EvolutionRules.CarryDamage(null!, 10));
            Assert.Throws<ArgumentNullException>(() => CoverRules.InRange(1f, null!));
            Assert.Throws<ArgumentNullException>(() => RandomSourceExtensions.RollD6(null!));
        }

        [Fact]
        public void LookTargetSelector_RejectsNegativeRadius()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                LookTargetSelector.SelectNearestRival(1, Vector3.Zero, PlayerSide.PlayerOne, new List<LookCandidate>(), -1f));
        }

        [Fact]
        public void DicePhaseResult_DoesNotAliasTheCallersArray()
        {
            int[] rolls = { 6, 6, 1 };

            DicePhaseResult hits = AttackResolver.ResolveHits(rolls, 3);
            rolls[0] = 1;

            Assert.Equal(6, hits.Rolls[0]);
            Assert.Equal(2, hits.Successes);
        }

        [Fact]
        public void Resistance_IsNotReportedForZeroDamage()
        {
            var squirtle = new PokemonSpecies("Squirtle", EvolutionStage.Basic, null, ElementType.Water, ElementType.Lightning, ElementType.Fire, 60);

            Assert.False(PokemonDamageCalculator.Calculate(0, ElementType.Fire, squirtle).AppliedResistance);
        }
    }
}
