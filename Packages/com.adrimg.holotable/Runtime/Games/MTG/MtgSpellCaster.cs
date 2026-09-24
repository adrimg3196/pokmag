using System;
using System.Collections;
using System.Collections.Generic;
using HoloTable.Combat;
using HoloTable.Core;
using HoloTable.Data;
using HoloTable.Domain;
using HoloTable.Entities;
using HoloTable.UI;
using HoloTable.VFX;
using UnityEngine;

namespace HoloTable.Games.MTG
{
    /// <summary>
    /// Room-scale spell choreography for instants and sorceries: darkens the real room,
    /// calls lightning from the ceiling onto enemy holograms, fireballs, healing light…
    /// Hosted by <see cref="GameLogicMTG"/> (it owns coroutines and life totals).
    /// </summary>
    [Serializable]
    public sealed class MtgSpellCaster
    {
        [SerializeField] private Color lightningColor = new Color(0.55f, 0.75f, 1f);
        [SerializeField] private Color darknessTint = new Color(0.12f, 0f, 0.18f);
        [SerializeField] private Color healColor = new Color(0.4f, 1f, 0.55f);
        [SerializeField] private Color fireColor = new Color(1f, 0.45f, 0.1f);
        [SerializeField] private HoloProjectile fireProjectile;
        [SerializeField] private ParticleSystem darknessVfx;
        [SerializeField] private ParticleSystem healVfx;
        [SerializeField] private Material boltMaterial;
        [SerializeField, Min(0.1f)] private float boltSkyHeight = 0.7f;
        [SerializeField, Range(1, 6)] private int boltsPerTarget = 3;

        private readonly List<LivingEntityController> _buffer = new List<LivingEntityController>();

        public IEnumerator Cast(MtgCardDefinition spell, Vector3 castPoint, PlayerSide caster, GameLogicMTG host)
        {
            if (caster == PlayerSide.Neutral) caster = PlayerSide.PlayerOne;
            PlayerSide opponent = caster.Opponent();

            DamagePopupService.ShowText(castPoint + TableSpace.Current.Normal * 0.06f, spell.DisplayName, Color.white, 0.9f);

            switch (spell.SpellArchetype)
            {
                case SpellArchetype.Lightning:
                    yield return Lightning(spell, castPoint, opponent, host);
                    break;
                case SpellArchetype.Darkness:
                    yield return Darkness(spell, opponent, host);
                    break;
                case SpellArchetype.Fire:
                    yield return Fire(spell, caster, opponent, castPoint, host);
                    break;
                case SpellArchetype.Heal:
                    yield return Heal(spell, caster, host);
                    break;
                case SpellArchetype.Buff:
                    Buff(spell, caster);
                    break;
            }
        }

        private IEnumerator Lightning(MtgCardDefinition spell, Vector3 castPoint, PlayerSide opponent, GameLogicMTG host)
        {
            bool peak = false;
            EnvironmentDimmer.Current.Pulse(0.75f, 0.35f, 1.3f, 0.9f, new Color(0.02f, 0.03f, 0.08f), () => peak = true);
            yield return WaitForPeak(() => peak, 0.35f, spell);

            CollectTargets(spell, castPoint, opponent);
            Vector3 up = TableSpace.Current.Normal;

            if (_buffer.Count == 0)
            {
                Vector3 face = TableSpace.Current.PlayerEdge(opponent);
                for (int i = 0; i < boltsPerTarget; i++)
                {
                    StrikeBolt(face + up * boltSkyHeight + UnityEngine.Random.insideUnitSphere * 0.1f, face);
                    yield return new WaitForSeconds(0.12f);
                }

                host.ChangeLife(opponent, -spell.SpellPower, face);
                yield break;
            }

            foreach (LivingEntityController target in _buffer.ToArray())
            {
                if (target == null) continue;
                for (int i = 0; i < boltsPerTarget && target != null; i++)
                {
                    Vector3 sky = target.TopWorld + up * boltSkyHeight + UnityEngine.Random.insideUnitSphere * 0.12f;
                    StrikeBolt(sky, target.CenterWorld);
                    yield return new WaitForSeconds(0.12f);
                }

                HitWithoutProjectile(target, spell.SpellPower, "¡Rayo!", lightningColor);
            }
        }

        private IEnumerator Darkness(MtgCardDefinition spell, PlayerSide opponent, GameLogicMTG host)
        {
            bool peak = false;
            EnvironmentDimmer.Current.Pulse(0.92f, 0.8f, 1.8f, 1.2f, darknessTint, () => peak = true);
            yield return WaitForPeak(() => peak, 0.8f, spell);

            EntityRegistry.CollectBySide(opponent, _buffer);
            foreach (LivingEntityController target in _buffer.ToArray())
            {
                if (target == null) continue; // destroyed while the previous target was hit
                SpawnFx(darknessVfx, target.GroundWorld, target.WorldHeight);
                HitWithoutProjectile(target, spell.SpellPower, $"-{spell.SpellPower}/-{spell.SpellPower}", darknessTint);
                yield return new WaitForSeconds(0.15f);
            }
        }

        private IEnumerator Fire(MtgCardDefinition spell, PlayerSide caster, PlayerSide opponent, Vector3 castPoint, GameLogicMTG host)
        {
            EnvironmentDimmer.Current.Pulse(0.4f, 0.2f, 0.6f, 0.6f, new Color(0.2f, 0.05f, 0f));
            CollectTargets(spell, castPoint, opponent);

            ARCombatManager combat = ARCombatManager.Instance;
            Vector3 origin = TableSpace.Current.PlayerEdge(caster) + TableSpace.Current.Normal * 0.15f;
            if (_buffer.Count == 0)
            {
                Vector3 face = TableSpace.Current.PlayerEdge(opponent);
                if (combat != null)
                {
                    combat.RequestAttack(new AttackRequest
                    {
                        OriginOverride = origin,
                        TargetPointOverride = face,
                        Damage = spell.SpellPower,
                        Projectile = fireProjectile,
                        Tint = fireColor,
                        ForceRanged = true,
                        OnResolved = _ => host.ChangeLife(opponent, -spell.SpellPower, face),
                    });
                }
                else
                {
                    host.ChangeLife(opponent, -spell.SpellPower, face);
                }

                yield break;
            }

            foreach (LivingEntityController target in _buffer.ToArray())
            {
                if (combat != null)
                {
                    combat.RequestAttack(new AttackRequest
                    {
                        Defender = target,
                        OriginOverride = origin,
                        Damage = spell.SpellPower,
                        Projectile = fireProjectile,
                        Tint = fireColor,
                        ForceRanged = true,
                        Label = spell.DisplayName,
                    });
                }
                else
                {
                    target.ApplyDamage(spell.SpellPower, target.CenterWorld, spell.DisplayName);
                }

                yield return new WaitForSeconds(0.2f);
            }
        }

        private IEnumerator Heal(MtgCardDefinition spell, PlayerSide caster, GameLogicMTG host)
        {
            EnvironmentDimmer.Current.Flash(healColor, 0.35f);
            EntityRegistry.CollectBySide(caster, _buffer);

            if (_buffer.Count == 0)
            {
                host.ChangeLife(caster, spell.SpellPower, TableSpace.Current.PlayerEdge(caster));
                yield break;
            }

            foreach (LivingEntityController ally in _buffer.ToArray())
            {
                if (ally == null) continue;
                SpawnFx(healVfx, ally.GroundWorld, ally.WorldHeight);
                ally.Heal(spell.SpellPower);
                yield return new WaitForSeconds(0.1f);
            }
        }

        private void Buff(MtgCardDefinition spell, PlayerSide caster)
        {
            EntityRegistry.CollectBySide(caster, _buffer);
            foreach (LivingEntityController ally in _buffer)
            {
                int n = spell.SpellPower;
                ally.SetCombatStats(ally.AttackValue + n, ally.DefenseValue + n);
                ally.ReplaceVitals(Vitals.Full(ally.Vitals.Max + n).WithDamage(ally.Vitals.Missing));
                ally.SetStatusTag($"+{n}/+{n}", new Color(1f, 0.85f, 0.3f));
                DamagePopupService.ShowText(ally.TopWorld, $"+{n}/+{n}", new Color(1f, 0.85f, 0.3f));
            }
        }

        private void CollectTargets(MtgCardDefinition spell, Vector3 castPoint, PlayerSide opponent)
        {
            if (spell.AffectsAllTargets)
            {
                EntityRegistry.CollectBySide(opponent, _buffer);
                return;
            }

            _buffer.Clear();
            LivingEntityController nearest = EntityRegistry.FindNearest(castPoint, opponent, 5f);
            if (nearest != null) _buffer.Add(nearest);
        }

        private void StrikeBolt(Vector3 from, Vector3 to)
        {
            LightningBoltEffect.Strike(from, to, lightningColor, 0.4f, 0.006f, boltMaterial);
            EnvironmentDimmer.Current.Flash(lightningColor, 0.55f);
        }

        private static void HitWithoutProjectile(LivingEntityController target, int damage, string label, Color tint)
        {
            if (target == null || !target.IsAlive) return;

            ARCombatManager combat = ARCombatManager.Instance;
            if (combat == null)
            {
                target.ApplyDamage(damage, target.CenterWorld, label);
                return;
            }

            combat.RequestAttack(new AttackRequest
            {
                Defender = target,
                Damage = damage,
                Label = label,
                Tint = tint,
                ForceMelee = true,
                SkipAttackAnimation = true,
            });
        }

        private static void SpawnFx(ParticleSystem prefab, Vector3 position, float height) =>
            VfxPool.Play(prefab, position, Quaternion.identity, Mathf.Max(0.5f, height / 0.1f));

        /// <summary>Waits for the dimmer peak, but never forever (another effect may own the dimmer).</summary>
        private static IEnumerator WaitForPeak(Func<bool> reached, float fadeIn, MtgCardDefinition spell)
        {
            float deadline = Time.time + fadeIn + 0.5f;
            while (!reached() && Time.time < deadline) yield return null;
            if (!reached())
            {
                Debug.LogWarning($"[HoloTable] Spell '{spell.DisplayName}': room-dim peak not reached in time; resolving anyway.", spell);
            }
        }
    }
}
