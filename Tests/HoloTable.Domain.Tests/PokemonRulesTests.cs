using System.Collections.Generic;
using System.Numerics;
using HoloTable.Domain;
using HoloTable.Domain.Pokemon;
using Xunit;

namespace HoloTable.Domain.Tests
{
    public sealed class PokemonRulesTests
    {
        private static readonly PokemonSpecies Charmander =
            new PokemonSpecies("Charmander", EvolutionStage.Basic, null, ElementType.Fire, ElementType.Water, ElementType.None, 70);

        private static readonly PokemonSpecies Charmeleon =
            new PokemonSpecies("Charmeleon", EvolutionStage.Stage1, "Charmander", ElementType.Fire, ElementType.Water, ElementType.None, 90);

        private static readonly PokemonSpecies Charizard =
            new PokemonSpecies("Charizard", EvolutionStage.Stage2, "Charmeleon", ElementType.Fire, ElementType.Water, ElementType.None, 180);

        private static readonly PokemonSpecies Squirtle =
            new PokemonSpecies("Squirtle", EvolutionStage.Basic, null, ElementType.Water, ElementType.Lightning, ElementType.Fire, 60);

        [Fact]
        public void Charizard_EvolvesFromCharmeleon()
        {
            Assert.True(EvolutionRules.CanEvolve(Charmeleon, Charizard));
        }

        [Fact]
        public void Charizard_CannotSkipAStage()
        {
            Assert.False(EvolutionRules.CanEvolve(Charmander, Charizard));
        }

        [Fact]
        public void EvolutionNameMatch_IsCaseInsensitive()
        {
            PokemonSpecies lower = Charmeleon with { EvolvesFrom = "charmander" };

            Assert.True(EvolutionRules.CanEvolve(Charmander, lower));
        }

        [Fact]
        public void StackedOver_WhenCentresNearlyCoincide()
        {
            Assert.True(EvolutionRules.IsStackedOver(Vector2.Zero, new Vector2(0.01f, 0.02f), 0.063f));
            Assert.False(EvolutionRules.IsStackedOver(Vector2.Zero, new Vector2(0.1f, 0f), 0.063f));
        }

        [Theory]
        [InlineData(1.0f, 2.5f, 0.02f, 0.05f, true)]
        [InlineData(3.0f, 2.5f, 0.02f, 0.05f, false)]
        [InlineData(1.0f, 2.5f, 0.20f, 0.05f, false)]
        public void Replacement_RequiresTimeAndDistanceWindow(float since, float window, float dist, float maxDist, bool expected)
        {
            Assert.Equal(expected, EvolutionRules.IsReplacement(since, window, dist, maxDist));
        }

        [Fact]
        public void Evolution_KeepsDamageCounters()
        {
            Vitals damaged = Vitals.Full(Charmeleon.Hp).WithDamage(50);

            Vitals evolved = EvolutionRules.CarryDamage(damaged, Charizard.Hp);

            Assert.Equal(130, evolved.Current);
        }

        [Fact]
        public void Weakness_DoublesDamage()
        {
            PokemonDamageResult r = PokemonDamageCalculator.Calculate(60, ElementType.Water, Charizard);

            Assert.Equal(120, r.Amount);
            Assert.True(r.AppliedWeakness);
        }

        [Fact]
        public void Resistance_SubtractsThirty_AndNeverGoesNegative()
        {
            PokemonDamageResult r = PokemonDamageCalculator.Calculate(20, ElementType.Fire, Squirtle);

            Assert.Equal(0, r.Amount);
            Assert.True(r.AppliedResistance);
        }

        [Fact]
        public void ColorlessWeakness_IsNeverApplied()
        {
            PokemonSpecies odd = Squirtle with { Weakness = ElementType.Colorless };

            Assert.Equal(30, PokemonDamageCalculator.Calculate(30, ElementType.Colorless, odd).Amount);
        }

        [Fact]
        public void DamageCounters_AreTensOfDamage()
        {
            Assert.Equal(12, PokemonDamageCalculator.ToDamageCounters(120));
        }

        [Fact]
        public void EnergyCost_TypedSymbolsNeedMatchingEnergy()
        {
            var attached = new List<ElementType> { ElementType.Fire, ElementType.Water };

            Assert.True(EnergyRules.CanPayCost(attached, new[] { ElementType.Fire, ElementType.Colorless }));
            Assert.False(EnergyRules.CanPayCost(attached, new[] { ElementType.Fire, ElementType.Fire }));
        }

        [Fact]
        public void EnergyCost_ColorlessNeedsEnoughLeftovers()
        {
            var attached = new List<ElementType> { ElementType.Fire };

            Assert.False(EnergyRules.CanPayCost(attached, new[] { ElementType.Fire, ElementType.Colorless }));
            Assert.True(EnergyRules.CanPayCost(attached, new[] { ElementType.Colorless }));
        }
    }
}
