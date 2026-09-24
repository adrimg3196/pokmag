using System;
using HoloTable.Entities;
using UnityEngine;

namespace HoloTable.Combat
{
    /// <summary>
    /// What a game module asks the combat manager to perform. Rules (damage numbers,
    /// weakness, dice) are computed by the module; the manager only choreographs.
    /// </summary>
    public sealed class AttackRequest
    {
        /// <summary>Attacking hologram. Null for environmental sources (spells, traps).</summary>
        public LivingEntityController Attacker { get; set; }

        /// <summary>Target hologram. Null when hitting a point (e.g. the opponent's "face" in MTG).</summary>
        public LivingEntityController Defender { get; set; }

        public Vector3? OriginOverride { get; set; }
        public Vector3? TargetPointOverride { get; set; }
        public int Damage { get; set; }
        public string Label { get; set; }
        public HoloProjectile Projectile { get; set; }
        public ParticleSystem ImpactVfx { get; set; }
        public Color Tint { get; set; } = Color.white;
        public bool ForceMelee { get; set; }
        public bool ForceRanged { get; set; }
        public bool SkipAttackAnimation { get; set; }
        public Action<AttackReport> OnResolved { get; set; }
    }

    public readonly struct AttackReport
    {
        public AttackReport(LivingEntityController attacker, LivingEntityController defender, int damage, bool landed, bool defeated, Vector3 impactPoint)
        {
            Attacker = attacker;
            Defender = defender;
            Damage = damage;
            Landed = landed;
            DefenderDefeated = defeated;
            ImpactPoint = impactPoint;
        }

        public LivingEntityController Attacker { get; }
        public LivingEntityController Defender { get; }
        public int Damage { get; }
        public bool Landed { get; }
        public bool DefenderDefeated { get; }
        public Vector3 ImpactPoint { get; }
    }
}
