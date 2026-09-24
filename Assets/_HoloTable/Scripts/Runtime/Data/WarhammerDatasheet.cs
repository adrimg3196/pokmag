using System;
using System.Collections.Generic;
using HoloTable.Domain;
using HoloTable.Domain.Warhammer;
using UnityEngine;

namespace HoloTable.Data
{
    [Serializable]
    public sealed class WeaponData
    {
        [SerializeField] private string name = "Bolt rifle";
        [SerializeField] private bool isMelee;
        [Tooltip("Range in inches (ignored for melee).")]
        [SerializeField, Min(0f)] private float rangeInches = 24f;
        [SerializeField, Min(1)] private int attacks = 2;
        [Tooltip("BS / WS as the X in X+.")]
        [SerializeField, Range(2, 6)] private int skill = 3;
        [SerializeField, Min(1)] private int strength = 4;
        [Tooltip("Positive number: AP-1 → 1.")]
        [SerializeField, Min(0)] private int armourPenetration = 1;
        [SerializeField, Min(1)] private int damage = 1;
        [SerializeField] private ElementType vfxFlavour = ElementType.Fire;

        private WeaponProfile _profile;

        public ElementType VfxFlavour => vfxFlavour;

        public WeaponProfile Profile => _profile ??= new WeaponProfile(
            name, isMelee ? 1f : rangeInches, attacks, skill, strength, armourPenetration, damage, isMelee);

        internal void Invalidate() => _profile = null;
    }

    [CreateAssetMenu(menuName = "HoloTable/Warhammer/Datasheet", fileName = "WH_NewDatasheet")]
    public sealed class WarhammerDatasheet : EntityDefinition
    {
        [Header("Datasheet")]
        [SerializeField, Min(0f)] private float moveInches = 6f;
        [SerializeField, Min(1)] private int toughness = 4;
        [SerializeField, Range(2, 7)] private int save = 3;
        [Tooltip("0 = none.")]
        [SerializeField, Range(0, 6)] private int invulnerableSave;
        [SerializeField, Min(1)] private int wounds = 2;
        [SerializeField, Min(0)] private int objectiveControl = 2;
        [Tooltip("Round base diameter in mm (25, 28.5, 32, 40, 60…).")]
        [SerializeField, Min(10f)] private float baseDiameterMm = 32f;
        [SerializeField] private WeaponData[] weapons = new WeaponData[0];

        [Header("Rig")]
        [Tooltip("Child transform name used as the gun muzzle for LoS and projectiles.")]
        [SerializeField] private string muzzleTransformName = "Muzzle";

        private UnitProfile _unit;

        public override GameSystem System => GameSystem.Warhammer;
        public override int BaseMaxHp => wounds;
        public override int BaseAttack => weapons.Length > 0 ? weapons[0].Profile.Attacks : 0;
        public override int BaseDefense => save;

        public int ObjectiveControl => objectiveControl;
        public float BaseDiameterMm => baseDiameterMm;
        public IReadOnlyList<WeaponData> Weapons => weapons;
        public string MuzzleTransformName => muzzleTransformName;

        public UnitProfile Unit => _unit ??= new UnitProfile(
            DisplayName,
            moveInches,
            toughness,
            save,
            invulnerableSave > 0 ? invulnerableSave : (int?)null,
            wounds,
            baseDiameterMm);

        /// <summary>Miniatures are measured by their base, not by a printed image.</summary>
        public override float ResolveFootprintMeters(float trackedShortSide) =>
            FootprintOverrideMeters > 0f ? FootprintOverrideMeters : TableUnits.MmToMeters(baseDiameterMm);

        public override string BuildStatLine() =>
            $"M{moveInches:0}\"  T{toughness}  Sv{save}+{(invulnerableSave > 0 ? $"/{invulnerableSave}++" : "")}  W{wounds}  OC{objectiveControl}";

        private void OnValidate()
        {
            _unit = null;
            foreach (WeaponData w in weapons) w?.Invalidate();
        }
    }
}
