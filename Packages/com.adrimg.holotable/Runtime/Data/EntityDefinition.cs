using System.Collections.Generic;
using HoloTable.Domain;
using UnityEngine;

namespace HoloTable.Data
{
    /// <summary>
    /// Read-only card / datasheet data shared by every game. One asset per physical card
    /// (or miniature). Runtime code never mutates it: live state lives on the entity.
    /// </summary>
    public abstract class EntityDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string displayName = "Unnamed";
        [Tooltip("Names of the reference images (AR Foundation XRReferenceImageLibrary / Vuforia database) that summon this entity. Several printings can share one definition.")]
        [SerializeField] private string[] referenceImageNames = new string[0];
        [SerializeField] private Sprite cardArt;

        [Header("Hologram")]
        [SerializeField] private Entities.LivingEntityController prefab;
        [SerializeField] private SizeClass sizeClass = SizeClass.Medium;
        [Tooltip("Physical footprint in metres. 0 = use the tracked image size (cards) or the base diameter (miniatures).")]
        [SerializeField, Min(0f)] private float footprintOverrideMeters;
        [SerializeField] private ElementType element = ElementType.None;
        [SerializeField, ColorUsage(false, true)] private Color hologramTint = new Color(0.4f, 0.9f, 1f, 1f);

        public string DisplayName => displayName;
        public IReadOnlyList<string> ReferenceImageNames => referenceImageNames;
        public Sprite CardArt => cardArt;
        public Entities.LivingEntityController Prefab => prefab;
        public SizeClass SizeClass => sizeClass;
        public float FootprintOverrideMeters => footprintOverrideMeters;
        public ElementType Element => element;
        public Color HologramTint => hologramTint;

        public abstract GameSystem System { get; }

        /// <summary>HP / toughness / wounds.</summary>
        public abstract int BaseMaxHp { get; }

        public abstract int BaseAttack { get; }

        public abstract int BaseDefense { get; }

        /// <summary>
        /// False for spells, energies, lands… anything that should not become a creature.
        /// Creatures without a prefab still spawn, as a procedural placeholder hologram.
        /// </summary>
        public virtual bool SpawnsCreature => true;

        /// <summary>Footprint used by the scale rules: explicit override, else the tracked card size.</summary>
        public virtual float ResolveFootprintMeters(float trackedShortSide) =>
            footprintOverrideMeters > 0f ? footprintOverrideMeters : trackedShortSide;

        /// <summary>Compact line shown under the name in the HUD, built from the entity's live stats (buffs included).</summary>
        public virtual string BuildStatLine(int attack, int defense, int maxHp) => $"ATK {attack}  DEF {defense}";
    }
}
