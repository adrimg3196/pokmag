using System;
using System.Collections.Generic;
using HoloTable.Domain;
using HoloTable.Domain.Pokemon;
using UnityEngine;

namespace HoloTable.Data
{
    public enum PokemonCardKind
    {
        Pokemon = 0,
        Energy = 1,
        Trainer = 2,
    }

    [Serializable]
    public sealed class PokemonAttackData
    {
        [SerializeField] private string name = "Tackle";
        [SerializeField] private ElementType[] cost = { ElementType.Colorless };
        [SerializeField, Min(0)] private int damage = 10;
        [Tooltip("Optional projectile override (e.g. Flamethrower cone, Hydro Pump jet).")]
        [SerializeField] private Combat.HoloProjectile projectile;

        public string Name => name;
        public IReadOnlyList<ElementType> Cost => cost;
        public int Damage => damage;
        public Combat.HoloProjectile Projectile => projectile;
    }

    [CreateAssetMenu(menuName = "HoloTable/Pokemon/Card", fileName = "PKM_NewCard")]
    public sealed class PokemonCardDefinition : EntityDefinition
    {
        [Header("Pokémon TCG")]
        [SerializeField] private PokemonCardKind kind = PokemonCardKind.Pokemon;
        [SerializeField] private EvolutionStage stage = EvolutionStage.Basic;
        [Tooltip("Exact name printed in 'Evolves from'.")]
        [SerializeField] private string evolvesFrom = "";
        [SerializeField, Min(10)] private int hp = 60;
        [SerializeField] private ElementType weakness = ElementType.None;
        [SerializeField] private ElementType resistance = ElementType.None;
        [SerializeField] private PokemonAttackData[] attacks = new PokemonAttackData[0];
        [Tooltip("Energy cards: the element they provide.")]
        [SerializeField] private ElementType providesEnergy = ElementType.None;

        private PokemonSpecies _species;

        public override GameSystem System => GameSystem.Pokemon;
        public override int BaseMaxHp => hp;
        public override int BaseAttack => attacks.Length > 0 ? attacks[0].Damage : 0;
        public override int BaseDefense => 0;
        public override bool SpawnsCreature => kind == PokemonCardKind.Pokemon && base.SpawnsCreature;

        public PokemonCardKind Kind => kind;
        public EvolutionStage Stage => stage;
        public IReadOnlyList<PokemonAttackData> Attacks => attacks;
        public ElementType ProvidesEnergy => providesEnergy;

        /// <summary>Domain snapshot, cached because definitions are immutable at runtime.</summary>
        public PokemonSpecies Species => _species ??= new PokemonSpecies(
            DisplayName,
            stage,
            string.IsNullOrWhiteSpace(evolvesFrom) ? null : evolvesFrom,
            Element,
            weakness,
            resistance,
            hp);

        public override string BuildStatLine() => $"{stage}  ·  {Element}";

        private void OnValidate() => _species = null;
    }
}
