using System.Collections.Generic;
using HoloTable.Domain;
using HoloTable.Domain.Warhammer;
using Xunit;

namespace HoloTable.Domain.Tests
{
    public sealed class WarhammerRulesTests
    {
        private static readonly UnitProfile Intercessor =
            new UnitProfile("Intercessor", 6f, 4, 3, null, 2, 32f);

        private static readonly WeaponProfile BoltRifle =
            new WeaponProfile("Bolt rifle", 24f, 2, 3, 4, 1, 1, false);

        [Theory]
        [InlineData(8, 4, 2)]
        [InlineData(5, 4, 3)]
        [InlineData(4, 4, 4)]
        [InlineData(3, 4, 5)]
        [InlineData(2, 4, 6)]
        public void WoundTable_MatchesTenthEdition(int s, int t, int expected)
        {
            Assert.Equal(expected, WoundRules.RequiredWoundRoll(s, t));
        }

        [Fact]
        public void NaturalOneFails_NaturalSixSucceeds()
        {
            Assert.False(WoundRules.RollSucceeds(1, 2));
            Assert.True(WoundRules.RollSucceeds(6, 7));
        }

        [Theory]
        [InlineData(3, 1, null, false, 4)]
        [InlineData(3, 0, null, true, 3)]   // 3+ vs AP0 ignores cover
        [InlineData(4, 0, null, true, 3)]   // cover +1
        [InlineData(3, 4, 4, false, 4)]     // invulnerable wins
        [InlineData(6, 3, null, false, 7)]  // no save possible
        [InlineData(2, 0, null, true, 2)]   // cannot go better than 2+
        public void SaveTarget_AppliesApCoverAndInvulnerable(int save, int ap, int? invul, bool cover, int expected)
        {
            Assert.Equal(expected, WoundRules.SaveTarget(save, ap, invul, cover));
        }

        [Fact]
        public void SaveOfSeven_AlwaysFails()
        {
            Assert.False(WoundRules.SaveSucceeds(6, 7));
        }

        [Fact]
        public void MovementRing_AddsBaseRadius()
        {
            float r = MovementRules.RingRadiusMeters(Intercessor, MovementMode.Normal);

            Assert.Equal(6f * 0.0254f + 0.016f, r, 4);
        }

        [Fact]
        public void Advance_AddsTheDieRoll()
        {
            Assert.Equal(10f, MovementRules.MaxMoveInches(Intercessor, MovementMode.Advance, 4));
        }

        [Fact]
        public void RemainingInches_TracksPhysicalDisplacement()
        {
            Assert.Equal(1f, MovementRules.RemainingInches(6f, 5f * 0.0254f), 3);
        }

        [Fact]
        public void ResolvePhases_CountSuccesses()
        {
            DicePhaseResult hits = AttackResolver.ResolveHits(new[] { 1, 2, 3, 6 }, 3);
            DicePhaseResult saves = AttackResolver.ResolveSaves(new[] { 1, 3, 4 }, 4);

            Assert.Equal(2, hits.Successes);
            Assert.Equal(1, saves.Successes);
            Assert.Equal(2, saves.Failures);
        }

        [Fact]
        public void ResolveAll_WithScriptedDice_ProducesExpectedDamage()
        {
            // hits: 4,5 → 2 hits (3+) ; wounds S4 vs T4 needs 4+: 4,2 → 1 ; save 3+ AP1 → 4+: roll 2 → fail
            var dice = new ScriptedRandom(4, 5, 4, 2, 2);

            AttackSummary s = AttackResolver.ResolveAll(BoltRifle, Intercessor, inCover: false, dice);

            Assert.Equal(2, s.Hits.Successes);
            Assert.Equal(1, s.Wounds.Successes);
            Assert.Equal(4, s.Saves.TargetNumber);
            Assert.Equal(1, s.TotalDamage);
        }

        [Theory]
        [InlineData(9, 9, Visibility.Clear)]
        [InlineData(3, 9, Visibility.PartialCover)]
        [InlineData(0, 9, Visibility.Blocked)]
        public void Cover_FromVisibleSamples(int visible, int total, Visibility expected)
        {
            Assert.Equal(expected, CoverRules.Evaluate(visible, total));
        }

        [Fact]
        public void InRange_UsesInches()
        {
            Assert.True(CoverRules.InRange(24f * 0.0254f, BoltRifle));
            Assert.False(CoverRules.InRange(25f * 0.0254f, BoltRifle));
        }

        private sealed class ScriptedRandom : IRandomSource
        {
            private readonly Queue<int> _values;

            public ScriptedRandom(params int[] values) => _values = new Queue<int>(values);

            public int Range(int minInclusive, int maxExclusive) => _values.Dequeue();
        }
    }
}
