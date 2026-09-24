using System;
using System.Collections.Generic;
using System.Numerics;
using HoloTable.Domain;
using HoloTable.Domain.Combat;
using Xunit;

namespace HoloTable.Domain.Tests
{
    public sealed class VitalsTests
    {
        [Fact]
        public void WithDamage_ReturnsNewInstance_AndClampsAtZero()
        {
            Vitals full = Vitals.Full(60);

            Vitals hurt = full.WithDamage(80);

            Assert.Equal(60, full.Current);
            Assert.Equal(0, hurt.Current);
            Assert.True(hurt.IsDefeated);
        }

        [Fact]
        public void WithHealing_NeverExceedsMax()
        {
            Vitals v = Vitals.Full(10).WithDamage(3).WithHealing(50);

            Assert.Equal(10, v.Current);
        }

        [Fact]
        public void WithMaxKeepingDamage_CarriesDamageCounters()
        {
            Vitals charmeleon = Vitals.Full(90).WithDamage(40);

            Vitals charizard = charmeleon.WithMaxKeepingDamage(180);

            Assert.Equal(180, charizard.Max);
            Assert.Equal(140, charizard.Current);
        }

        [Fact]
        public void Full_RejectsNonPositiveMax()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Vitals.Full(0));
        }

        [Fact]
        public void WithDamage_RejectsNegative()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Vitals.Full(5).WithDamage(-1));
        }
    }

    public sealed class ScaleRulesTests
    {
        private const float CardShortSide = 0.063f;

        [Fact]
        public void SmallCreature_IsClampedInsideItsCard()
        {
            // Wide, flat model: height rule alone would make it 12 cm wide.
            ScaleProfile p = ScaleRules.Compute(nativeHeight: 1f, nativeFootprint: 2f, CardShortSide, SizeClass.Small);

            Assert.False(p.OverflowsFootprint);
            Assert.True(p.WorldFootprint <= CardShortSide);
        }

        [Fact]
        public void HugeDragon_TowersOverTheBoard()
        {
            ScaleProfile p = ScaleRules.Compute(nativeHeight: 2f, nativeFootprint: 3f, CardShortSide, SizeClass.Huge);

            Assert.True(p.OverflowsFootprint);
            Assert.Equal(ScaleRules.TargetHeightMeters(SizeClass.Huge), p.WorldHeight, 3);
        }

        [Fact]
        public void Multiplier_ScalesTargetHeight()
        {
            ScaleProfile normal = ScaleRules.Compute(1f, 0.1f, CardShortSide, SizeClass.Large);
            ScaleProfile doubled = ScaleRules.Compute(1f, 0.1f, CardShortSide, SizeClass.Large, multiplier: 2f);

            Assert.Equal(normal.UniformScale * 2f, doubled.UniformScale, 4);
        }

        [Theory]
        [InlineData(0f, 1f, 1f)]
        [InlineData(1f, 0f, 1f)]
        [InlineData(1f, 1f, 0f)]
        public void Compute_RejectsDegenerateInput(float h, float f, float card)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ScaleRules.Compute(h, f, card, SizeClass.Medium));
        }
    }

    public sealed class EngagementRulesTests
    {
        [Fact]
        public void OnlyRivalsWithinRange_AreEngaged()
        {
            var combatants = new List<Combatant>
            {
                new Combatant(1, PlayerSide.PlayerOne, new Vector2(0f, 0f), 0.2f),
                new Combatant(2, PlayerSide.PlayerTwo, new Vector2(0.1f, 0f), 0.2f),
                new Combatant(3, PlayerSide.PlayerOne, new Vector2(0.05f, 0f), 0.2f), // ally, ignored
                new Combatant(4, PlayerSide.PlayerTwo, new Vector2(2f, 0f), 0.2f),   // too far
            };
            var results = new List<Engagement>();

            EngagementRules.FindEngagements(combatants, results);

            Assert.Contains(new Engagement(1, 2, 0f), results);
            Assert.Contains(new Engagement(3, 2, 0f), results);
            Assert.Equal(2, results.Count);
        }

        [Fact]
        public void PairRange_UsesTheLargerRadius()
        {
            var combatants = new List<Combatant>
            {
                new Combatant(1, PlayerSide.PlayerOne, Vector2.Zero, 0.01f),
                new Combatant(2, PlayerSide.PlayerTwo, new Vector2(0.3f, 0f), 0.35f),
            };
            var results = new List<Engagement>();

            EngagementRules.FindEngagements(combatants, results);

            Assert.Single(results);
        }

        [Fact]
        public void NeutralEntities_NeverEngage()
        {
            var combatants = new List<Combatant>
            {
                new Combatant(1, PlayerSide.Neutral, Vector2.Zero, 1f),
                new Combatant(2, PlayerSide.PlayerTwo, Vector2.Zero, 1f),
            };
            var results = new List<Engagement>();

            EngagementRules.FindEngagements(combatants, results);

            Assert.Empty(results);
        }
    }

    public sealed class LookTargetSelectorTests
    {
        [Fact]
        public void PicksNearestLivingRival()
        {
            var candidates = new List<LookCandidate>
            {
                new LookCandidate(1, Vector3.Zero, PlayerSide.PlayerOne, true),
                new LookCandidate(2, new Vector3(0.3f, 0, 0), PlayerSide.PlayerTwo, true),
                new LookCandidate(3, new Vector3(0.1f, 0, 0), PlayerSide.PlayerTwo, false), // dead
                new LookCandidate(4, new Vector3(0.2f, 0, 0), PlayerSide.PlayerTwo, true),
                new LookCandidate(5, new Vector3(0.05f, 0, 0), PlayerSide.PlayerOne, true), // ally
            };

            int id = LookTargetSelector.SelectNearestRival(1, Vector3.Zero, PlayerSide.PlayerOne, candidates, 1f);

            Assert.Equal(4, id);
        }

        [Fact]
        public void ReturnsNoTarget_WhenRivalsOutsideAwareness()
        {
            var candidates = new List<LookCandidate>
            {
                new LookCandidate(2, new Vector3(5f, 0, 0), PlayerSide.PlayerTwo, true),
            };

            int id = LookTargetSelector.SelectNearestRival(1, Vector3.Zero, PlayerSide.PlayerOne, candidates, 0.5f);

            Assert.Equal(LookTargetSelector.NoTarget, id);
        }
    }

    public sealed class RandomSourceTests
    {
        [Fact]
        public void RollD6_StaysInRange_AndIsDeterministicWithSeed()
        {
            int[] a = new SystemRandomSource(42).RollD6(200);
            int[] b = new SystemRandomSource(42).RollD6(200);

            Assert.Equal(a, b);
            Assert.All(a, r => Assert.InRange(r, 1, 6));
        }
    }
}
