using HoloTable.Domain;
using HoloTable.Domain.Mtg;
using UnityEngine;

namespace HoloTable.Data
{
    public enum SpellArchetype
    {
        Lightning = 0,
        Darkness = 1,
        Fire = 2,
        Heal = 3,
        Buff = 4,
    }

    [CreateAssetMenu(menuName = "HoloTable/MTG/Card", fileName = "MTG_NewCard")]
    public sealed class MtgCardDefinition : EntityDefinition
    {
        [Header("Magic: The Gathering")]
        [SerializeField] private MtgCardKind kind = MtgCardKind.Creature;
        [SerializeField] private string manaCost = "{1}{R}";
        [SerializeField, Min(0)] private int power = 2;
        [SerializeField, Min(0)] private int toughness = 2;
        [Tooltip("Oracle text (EN or ES). Keywords such as 'Flying' / 'Vuela' are parsed from here.")]
        [SerializeField, TextArea(2, 6)] private string rulesText = "";

        [Header("Spells (Instant / Sorcery)")]
        [SerializeField] private SpellArchetype spellArchetype = SpellArchetype.Lightning;
        [Tooltip("Damage, healing or buff amount.")]
        [SerializeField, Min(0)] private int spellPower = 3;
        [SerializeField] private bool affectsAllTargets;

        private MtgKeyword? _keywords;
        private MtgCreature _creature;

        public override GameSystem System => GameSystem.MagicTheGathering;
        public override int BaseMaxHp => Mathf.Max(1, toughness);
        public override int BaseAttack => power;
        public override int BaseDefense => toughness;
        public override bool SpawnsCreature => IsPermanent && base.SpawnsCreature;

        public MtgCardKind Kind => kind;
        public string ManaCost => manaCost;
        public string RulesText => rulesText;
        public SpellArchetype SpellArchetype => spellArchetype;
        public int SpellPower => spellPower;
        public bool AffectsAllTargets => affectsAllTargets;

        public bool IsSpell => kind == MtgCardKind.Instant || kind == MtgCardKind.Sorcery;
        public bool IsPermanent => !IsSpell;
        public bool IsCreature => kind == MtgCardKind.Creature;

        public MtgKeyword Keywords => _keywords ??= MtgKeywordParser.Parse(rulesText);

        public MtgCreature Creature => _creature ??= new MtgCreature(DisplayName, power, toughness, Keywords);

        public override string BuildStatLine(int attack, int defense, int maxHp) =>
            IsCreature ? $"{manaCost}   {attack}/{maxHp}" : $"{manaCost}   {kind}";

        private void OnValidate()
        {
            _keywords = null;
            _creature = null;
        }
    }
}
