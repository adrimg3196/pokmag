using System;
using System.Collections;
using System.Collections.Generic;
using HoloTable.Combat;
using HoloTable.Core;
using HoloTable.Data;
using HoloTable.Domain;
using HoloTable.Domain.Pokemon;
using HoloTable.Entities;
using HoloTable.Tracking;
using HoloTable.UI;
using HoloTable.VFX;
using UnityEngine;

namespace HoloTable.Games.Pokemon
{
    /// <summary>
    /// Pokémon TCG rules on the holographic table.
    ///
    ///  • AR Evolution: placing Charizard ON TOP of Charmeleon (or swapping the cards in the
    ///    same spot) triggers the classic flicker-silhouette evolution with a particle cocoon;
    ///    the old model dissolves and the new one keeps its damage counters and energies.
    ///  • Energy: putting an Energy card next to a Pokémon attaches it (burst + stronger aura).
    ///  • Elemental auras around every Pokémon by type (Fire, Water, Lightning…).
    ///  • Attacks validated against attached energy, damage with weakness ×2 / resistance −30,
    ///    floating HP bar and pop-up damage numbers ("¡Es súper eficaz!").
    /// </summary>
    public sealed class GameLogicPokemon : MonoBehaviour, IGameRuleModule
    {
        [Serializable]
        private sealed class ElementVfx
        {
            public ElementType element = ElementType.Fire;
            public Color color = new Color(1f, 0.45f, 0.1f);
            [Tooltip("Looping aura (should be a child-friendly, local-space particle system).")]
            public ParticleSystem aura;
            [Tooltip("One-shot burst when an energy of this type is attached.")]
            public ParticleSystem attachBurst;
            [Tooltip("Projectile used by attacks of this type.")]
            public HoloProjectile projectile;
        }

        private sealed class PokemonState
        {
            public readonly List<ElementType> Energies = new List<ElementType>();
            public ParticleSystem Aura;
            public float BaseAuraRate;
        }

        [Header("Evolution")]
        [Tooltip("Stacked if the new card centre is within this fraction of the card's short side.")]
        [SerializeField, Range(0.1f, 1.5f)] private float stackTolerance = 0.6f;
        [Tooltip("Max distance (metres) between the removed card and the evolution put in its place. The ghost time is HoloSpawnDirector.pokemonPersistence.")]
        [SerializeField, Min(0.01f)] private float replaceMaxDistance = 0.06f;
        [SerializeField, Min(0.5f)] private float evolutionDuration = 3f;
        [SerializeField] private ParticleSystem evolutionCocoonVfx;
        [SerializeField] private ParticleSystem evolutionBurstVfx;
        [SerializeField] private AudioClip evolutionSfx;
        [Tooltip("If true, a Stage 1/2 card without its previous stage on the table only shows a hint.")]
        [SerializeField] private bool enforceEvolutionRules = true;

        [Header("Energy")]
        [SerializeField, Min(0.01f)] private float energyAttachRadius = 0.12f;
        [Tooltip("Aura emission multiplier added per attached energy.")]
        [SerializeField, Min(0f)] private float auraBoostPerEnergy = 0.5f;
        [SerializeField] private List<ElementVfx> elements = new List<ElementVfx>();

        private readonly Dictionary<LivingEntityController, PokemonState> _states = new Dictionary<LivingEntityController, PokemonState>();
        private readonly HashSet<LivingEntityController> _evolving = new HashSet<LivingEntityController>();

        public GameSystem System => GameSystem.Pokemon;

        public event Action<LivingEntityController, LivingEntityController> Evolved;
        public event Action<LivingEntityController, ElementType> EnergyAttached;

        private void OnEnable() => HoloSpawnDirector.RegisterModule(this);

        private void OnDisable() => HoloSpawnDirector.UnregisterModule(this);

        // ─────────────────────────────── IGameRuleModule ───────────────────────────────

        public bool TryInterceptSpawn(SpawnContext context)
        {
            if (!(context.Definition is PokemonCardDefinition card)) return false;

            switch (card.Kind)
            {
                case PokemonCardKind.Energy:
                    TryAttachEnergyFromCard(card, context);
                    return true;

                case PokemonCardKind.Trainer:
                    DamagePopupService.ShowInfo(context.Target.Position + Vector3.up * 0.05f, card.DisplayName);
                    return true;
            }

            if (card.Stage == EvolutionStage.Basic) return false;

            LivingEntityController previous = FindEvolutionBase(card, context);
            if (previous != null)
            {
                StartCoroutine(EvolutionRoutine(previous, card, context));
                return true;
            }

            if (enforceEvolutionRules)
            {
                DamagePopupService.ShowInfo(context.Target.Position + Vector3.up * 0.05f,
                    $"{card.DisplayName}\nnecesita {card.Species.EvolvesFrom}");
                return true;
            }

            return false;
        }

        public void OnEntitySpawned(LivingEntityController entity, SpawnContext context)
        {
            if (!(entity.Definition is PokemonCardDefinition card)) return;

            var state = new PokemonState();
            _states[entity] = state;
            entity.Despawned += OnDespawned;
            CreateAura(entity, state, card.Element);
            RefreshResource(entity, state);
        }

        /// <summary>
        /// The director keeps a lost Pokémon as a ghost (pokemonPersistence) and re-attaches it if the
        /// same card returns; an evolution placed in the same spot meanwhile evolves the ghost.
        /// </summary>
        public bool OnTargetLost(TrackedTarget target, LivingEntityController entity) => false;

        // ─────────────────────────────── Public gameplay API ───────────────────────────────

        public IReadOnlyList<ElementType> GetEnergies(LivingEntityController pokemon) =>
            _states.TryGetValue(pokemon, out PokemonState s) ? s.Energies : (IReadOnlyList<ElementType>)Array.Empty<ElementType>();

        public void AttachEnergy(LivingEntityController pokemon, ElementType energy)
        {
            if (pokemon == null || !_states.TryGetValue(pokemon, out PokemonState state)) return;

            state.Energies.Add(energy);
            ElementVfx vfx = FindVfx(energy);
            if (vfx != null) VfxPool.Play(vfx.attachBurst, pokemon.CenterWorld, Quaternion.identity);

            DamagePopupService.ShowText(pokemon.TopWorld, $"+ {energy}", vfx?.color ?? Color.white, 0.8f);
            RefreshResource(pokemon, state);
            EnergyAttached?.Invoke(pokemon, energy);
        }

        /// <summary>Validates energy cost, computes weakness/resistance and launches the attack.</summary>
        public bool DeclareAttack(LivingEntityController attacker, LivingEntityController defender, int attackIndex = 0)
        {
            if (attacker == null || defender == null) return false;
            if (!attacker.CanAct)
            {
                DamagePopupService.ShowInfo(attacker.TopWorld, "Ahora no puede atacar");
                return false;
            }

            if (!defender.IsTargetable)
            {
                DamagePopupService.ShowInfo(attacker.TopWorld, "Objetivo no válido");
                return false;
            }

            if (!(attacker.Definition is PokemonCardDefinition card) || !(defender.Definition is PokemonCardDefinition target)) return false;
            if (attackIndex < 0 || attackIndex >= card.Attacks.Count)
            {
                Debug.LogWarning($"[HoloTable] {card.name} has no attack #{attackIndex}.", card);
                return false;
            }

            PokemonAttackData attack = card.Attacks[attackIndex];
            if (!EnergyRules.CanPayCost(GetEnergies(attacker), attack.Cost))
            {
                DamagePopupService.ShowInfo(attacker.TopWorld, "Energía insuficiente");
                return false;
            }

            PokemonDamageResult result = PokemonDamageCalculator.Calculate(attack.Damage, card.Element, target.Species);
            string label = result.AppliedWeakness ? "¡Es súper eficaz!"
                : result.AppliedResistance ? "No es muy eficaz…"
                : null;

            ElementVfx vfx = FindVfx(card.Element);
            ARCombatManager combat = ARCombatManager.Instance;
            if (combat == null)
            {
                defender.ApplyDamage(result.Amount, defender.CenterWorld, label);
                return true;
            }

            combat.RequestAttack(new AttackRequest
            {
                Attacker = attacker,
                Defender = defender,
                Damage = result.Amount,
                Label = label,
                Tint = vfx?.color ?? Color.white,
                Projectile = attack.Projectile != null ? attack.Projectile : vfx?.projectile,
            });

            DamagePopupService.ShowInfo(attacker.TopWorld, attack.Name);
            return true;
        }

        /// <summary>Convenience for UI buttons / voice commands: attack the engaged or nearest rival.</summary>
        public bool DeclareAttackOnNearestRival(LivingEntityController attacker, int attackIndex = 0)
        {
            ARCombatManager combat = ARCombatManager.Instance;
            LivingEntityController rival = combat != null ? combat.FindEngagedRival(attacker) : null;
            if (rival == null) rival = EntityRegistry.FindNearestRival(attacker, 2f);
            if (rival == null)
            {
                DamagePopupService.ShowInfo(attacker.TopWorld, "Sin rival a la vista");
                return false;
            }

            return DeclareAttack(attacker, rival, attackIndex);
        }

        // ─────────────────────────────── Evolution ───────────────────────────────

        private LivingEntityController FindEvolutionBase(PokemonCardDefinition card, SpawnContext context)
        {
            TableSpace table = TableSpace.Current;
            Vector3 newPos = context.Target.Position;
            float shortSide = context.Target.ShortSide;

            LivingEntityController best = null;
            float bestDistance = float.MaxValue;

            foreach (LivingEntityController candidate in context.Director.ActiveEntities)
            {
                if (candidate == null || !candidate.IsTargetable || _evolving.Contains(candidate)) continue;
                if (candidate.Side != context.Side && context.Side != PlayerSide.Neutral) continue;
                if (!(candidate.Definition is PokemonCardDefinition current)) continue;
                if (!EvolutionRules.CanEvolve(current.Species, card.Species)) continue;

                TrackedTarget oldTarget = candidate.Target;
                Vector3 oldPos = oldTarget != null && oldTarget.HasAnchor ? oldTarget.Position : candidate.GroundWorld;
                float distance = table.TableDistance(oldPos, newPos);

                bool stacked = EvolutionRules.IsStackedOver(
                    table.ToTable2D(oldPos), table.ToTable2D(newPos), shortSide, stackTolerance);

                // Replacement: the previous card is gone (ghost or inside its grace period) and the
                // evolution was put down in the same spot.
                bool previousGone = context.Director.IsOrphan(candidate)
                    || (oldTarget != null && oldTarget.Status == TrackingStatus.Lost);
                bool replaced = previousGone && distance <= replaceMaxDistance;

                if ((stacked || replaced) && distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private IEnumerator EvolutionRoutine(LivingEntityController previous, PokemonCardDefinition evolution, SpawnContext context)
        {
            _evolving.Add(previous);

            LivingEntityController next = context.Director.SpawnEntity(evolution, context.Target, previous.Side, playSpawn: false);
            if (next == null)
            {
                // Keep the previous stage exactly as it was (still linked to its card / ghost timer).
                _evolving.Remove(previous);
                Debug.LogWarning($"[HoloTable] Evolution to '{evolution.DisplayName}' failed: no prefab. {previous.name} stays.", evolution);
                yield break;
            }

            previous.SetGhosted(false);
            context.Director.DetachEntity(previous);
            previous.BeginTransformation();
            next.BeginTransformation();
            next.SetVisible(false);
            next.ReplaceVitals(EvolutionRules.CarryDamage(previous.Vitals, evolution.BaseMaxHp));

            PokemonState oldState = _states.TryGetValue(previous, out PokemonState s) ? s : null;
            Vector3 center = previous.CenterWorld;
            string previousName = previous.Definition.DisplayName;

            if (evolutionSfx != null) AudioSource.PlayClipAtPoint(evolutionSfx, center);
            float fxScale = Mathf.Max(0.5f, previous.WorldHeight / 0.1f);
            ParticleSystem cocoon = VfxPool.Play(evolutionCocoonVfx, previous.GroundWorld, Quaternion.identity, fxScale);

            EnvironmentDimmer.Current.Pulse(0.45f, 0.4f, evolutionDuration * 0.7f, 0.8f);

            // Classic evolution: both silhouettes turn white and alternate faster and faster.
            float elapsed = 0f;
            float interval = 0.35f;
            bool showNew = false;
            while (elapsed < evolutionDuration && next != null && previous != null)
            {
                float whiten = Mathf.Clamp01(elapsed / (evolutionDuration * 0.3f));
                previous.SetFlash(whiten, Color.white);
                next.SetFlash(1f, Color.white);

                showNew = !showNew;
                previous.SetVisible(!showNew);
                next.SetVisible(showNew);

                yield return new WaitForSeconds(interval);
                elapsed += interval;
                interval = Mathf.Max(0.04f, interval * 0.8f);
            }

            if (cocoon != null) cocoon.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            _evolving.Remove(previous);
            if (previous != null) previous.Despawn(animated: false);

            if (next == null || !next.IsAlive)
            {
                // Destroyed or KO'd mid-sequence (card removed, scene change): nothing to reveal.
                yield break;
            }

            VfxPool.Play(evolutionBurstVfx, center, Quaternion.identity, Mathf.Max(0.5f, next.WorldHeight / 0.1f));
            EnvironmentDimmer.Current.Flash(Color.white, 0.8f);

            // Carry energies over to the new stage.
            if (oldState != null && _states.TryGetValue(next, out PokemonState newState))
            {
                foreach (ElementType e in oldState.Energies) newState.Energies.Add(e);
                RefreshResource(next, newState);
            }

            next.ForceMaterialized();
            StartCoroutine(FadeFlash(next, 0.6f));
            DamagePopupService.ShowText(next.TopWorld, $"¡{previousName} evolucionó a {evolution.DisplayName}!", Color.white, 0.7f);
            Evolved?.Invoke(previous, next);
        }

        private static IEnumerator FadeFlash(LivingEntityController entity, float duration)
        {
            for (float t = 0f; t < duration && entity != null; t += Time.deltaTime)
            {
                entity.SetFlash(1f - t / duration, Color.white);
                yield return null;
            }

            if (entity != null) entity.SetFlash(0f, Color.white);
        }

        // ─────────────────────────────── Energy & aura ───────────────────────────────

        private void TryAttachEnergyFromCard(PokemonCardDefinition card, SpawnContext context)
        {
            Vector3 position = context.Target.Position;
            LivingEntityController pokemon = EntityRegistry.FindNearest(position, context.Side, energyAttachRadius, e => _states.ContainsKey(e));
            if (pokemon == null)
            {
                DamagePopupService.ShowInfo(position + Vector3.up * 0.05f, "Coloca la energía junto a un Pokémon");
                return;
            }

            ElementType energy = card.ProvidesEnergy != ElementType.None ? card.ProvidesEnergy : card.Element;
            AttachEnergy(pokemon, energy);
        }

        private void CreateAura(LivingEntityController entity, PokemonState state, ElementType element)
        {
            ElementVfx vfx = FindVfx(element);
            if (vfx?.aura == null) return;

            ParticleSystem aura = Instantiate(vfx.aura, entity.VisualRoot);
            aura.transform.localPosition = Vector3.zero;
            aura.transform.localRotation = Quaternion.identity;

            ParticleSystem.MainModule main = aura.main;
            main.startColor = vfx.color;
            ParticleSystem.ShapeModule shape = aura.shape;
            shape.radius = Mathf.Max(0.5f, shape.radius);

            state.Aura = aura;
            state.BaseAuraRate = aura.emission.rateOverTimeMultiplier;
            aura.Play(true);
        }

        private void RefreshResource(LivingEntityController entity, PokemonState state)
        {
            entity.SetResource(state.Energies.Count, "Energía");
            if (state.Aura != null)
            {
                ParticleSystem.EmissionModule emission = state.Aura.emission;
                emission.rateOverTimeMultiplier = state.BaseAuraRate * (1f + auraBoostPerEnergy * state.Energies.Count);
            }
        }

        private ElementVfx FindVfx(ElementType element)
        {
            foreach (ElementVfx e in elements)
            {
                if (e.element == element) return e;
            }

            return null;
        }

        private void OnDespawned(LivingEntityController entity)
        {
            entity.Despawned -= OnDespawned;
            _states.Remove(entity);
            _evolving.Remove(entity);
        }
    }
}
