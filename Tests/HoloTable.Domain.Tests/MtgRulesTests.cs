using HoloTable.Domain.Mtg;
using Xunit;

namespace HoloTable.Domain.Tests
{
    public sealed class TapRulesTests
    {
        [Theory]
        [InlineData(0f, 90f, true)]
        [InlineData(0f, -88f, true)]
        [InlineData(350f, 80f, true)]   // wraps around 360
        [InlineData(0f, 40f, false)]    // inside the hysteresis gap
        [InlineData(0f, 180f, false)]   // upside down is still "untapped"
        public void Tap_DetectedAtNinetyDegrees(float reference, float current, bool tapped)
        {
            var state = new TapState(false, reference);

            (TapState next, TapTransition t) = TapRules.Evaluate(state, current);

            Assert.Equal(tapped, next.IsTapped);
            Assert.Equal(tapped ? TapTransition.Tapped : TapTransition.None, t);
        }

        [Fact]
        public void Untap_RequiresReturningCloseToReference()
        {
            var tapped = new TapState(true, 0f);

            Assert.Equal(TapTransition.None, TapRules.Evaluate(tapped, 45f).Transition);
            Assert.Equal(TapTransition.Untapped, TapRules.Evaluate(tapped, 10f).Transition);
        }

        [Fact]
        public void Evaluate_DoesNotMutateInput()
        {
            var state = new TapState(false, 0f);

            TapRules.Evaluate(state, 90f);

            Assert.False(state.IsTapped);
        }

        [Fact]
        public void SignedDelta_IsNormalised()
        {
            Assert.Equal(-20f, TapRules.SignedDelta(10f, 350f), 3);
            Assert.Equal(180f, TapRules.SignedDelta(0f, 180f), 3);
        }
    }

    public sealed class MtgKeywordParserTests
    {
        [Theory]
        [InlineData("Flying, haste", MtgKeyword.Flying | MtgKeyword.Haste)]
        [InlineData("Vuela.\nVínculo vital.", MtgKeyword.Flying | MtgKeyword.Lifelink)]
        [InlineData("Arrollar, toque mortal", MtgKeyword.Trample | MtgKeyword.Deathtouch)]
        [InlineData("First strike", MtgKeyword.FirstStrike)]
        [InlineData("Whenever a defending player attacks...", MtgKeyword.None)]
        [InlineData("", MtgKeyword.None)]
        public void Parse_UnderstandsEnglishAndSpanish(string text, MtgKeyword expected)
        {
            Assert.Equal(expected, MtgKeywordParser.Parse(text));
        }
    }

    public sealed class MtgCombatRulesTests
    {
        private static readonly MtgCreature Dragon = new MtgCreature("Shivan Dragon", 5, 5, MtgKeyword.Flying);
        private static readonly MtgCreature Bear = new MtgCreature("Grizzly Bears", 2, 2, MtgKeyword.None);
        private static readonly MtgCreature Spider = new MtgCreature("Giant Spider", 2, 4, MtgKeyword.Reach);

        [Fact]
        public void Flier_CanOnlyBeBlockedByFlyingOrReach()
        {
            Assert.False(MtgCombatRules.CanBlock(Dragon, Bear));
            Assert.True(MtgCombatRules.CanBlock(Dragon, Spider));
        }

        [Fact]
        public void Unblocked_DealsPowerToPlayer()
        {
            MtgCombatOutcome o = MtgCombatRules.Resolve(Dragon, null);

            Assert.False(o.Blocked);
            Assert.Equal(5, o.DamageToDefendingPlayer);
        }

        [Fact]
        public void Trample_AssignsExcessToPlayer()
        {
            var wurm = new MtgCreature("Wurm", 6, 6, MtgKeyword.Trample);

            MtgCombatOutcome o = MtgCombatRules.Resolve(wurm, Bear);

            Assert.Equal(2, o.DamageToBlocker);
            Assert.Equal(4, o.DamageToDefendingPlayer);
            Assert.True(o.BlockerDies);
        }

        [Fact]
        public void Deathtouch_TrampleNeedsOnlyOneDamageOnBlocker()
        {
            var snake = new MtgCreature("Snake", 3, 1, MtgKeyword.Trample | MtgKeyword.Deathtouch);

            MtgCombatOutcome o = MtgCombatRules.Resolve(snake, Spider);

            Assert.Equal(1, o.DamageToBlocker);
            Assert.Equal(2, o.DamageToDefendingPlayer);
            Assert.True(o.BlockerDies);
        }

        [Fact]
        public void FirstStrike_KillsBlockerBeforeItStrikesBack()
        {
            var knight = new MtgCreature("Knight", 2, 2, MtgKeyword.FirstStrike);

            MtgCombatOutcome o = MtgCombatRules.Resolve(knight, Bear);

            Assert.True(o.BlockerDies);
            Assert.False(o.AttackerDies);
            Assert.Equal(0, o.DamageToAttacker);
        }

        [Fact]
        public void Lifelink_GainsLifeEqualToDamage()
        {
            var vampire = new MtgCreature("Vampire", 3, 3, MtgKeyword.Lifelink);

            Assert.Equal(3, MtgCombatRules.Resolve(vampire, null).LifeGainedByAttacker);
        }

        [Fact]
        public void Defender_CannotAttack_AndHasteIgnoresSummoningSickness()
        {
            var wall = new MtgCreature("Wall", 0, 5, MtgKeyword.Defender);
            var goblin = new MtgCreature("Goblin", 1, 1, MtgKeyword.Haste);

            Assert.False(MtgCombatRules.CanAttack(wall, summoningSick: false));
            Assert.True(MtgCombatRules.CanAttack(goblin, summoningSick: true));
            Assert.False(MtgCombatRules.CanAttack(Bear, summoningSick: true));
        }

        [Fact]
        public void ChooseBestBlocker_PrefersSurvivorsThatCanLegallyBlock()
        {
            int idx = MtgCombatRules.ChooseBestBlocker(Dragon, new[] { Bear, Spider });

            Assert.Equal(1, idx);
        }
    }
}
