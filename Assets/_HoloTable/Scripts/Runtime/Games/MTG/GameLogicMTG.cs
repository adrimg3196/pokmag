using System;
using System.Collections;
using System.Collections.Generic;
using HoloTable.Combat;
using HoloTable.Core;
using HoloTable.Data;
using HoloTable.Domain;
using HoloTable.Domain.Mtg;
using HoloTable.Entities;
using HoloTable.Tracking;
using HoloTable.UI;
using HoloTable.VFX;
using UnityEngine;

namespace HoloTable.Games.MTG
{
    /// <summary>
    /// Magic: The Gathering on the holographic table.
    ///
    ///  • Tapping: rotating the physical card ~90° is detected from the tracked pose (with
    ///    hysteresis). Creatures roar-lunge and become "ATACANTE"; other permanents show
    ///    "HABILIDAD ACTIVADA". Untapping clears the state.
    ///  • Physical blocking: during the block window the defender slides one of its
    ///    untapped creatures next to the attacker. Flying/Reach and combat keywords
    ///    (first strike, trample, deathtouch, lifelink) are resolved by the domain rules.
    ///  • Fliers ("Flying" / "Vuela") hover 30 cm above the board with a contact shadow.
    ///  • Instants/Sorceries trigger room-scale effects via <see cref="MtgSpellCaster"/>.
    /// </summary>
    public sealed class GameLogicMTG : MonoBehaviour, IGameRuleModule
    {
        private sealed class PermanentState
        {
            public TapState Tap;
            public bool SummoningSick;
            public float EnteredAt;
            public bool Attacking;
        }

        [Header("Tapping")]
        [SerializeField, Range(30f, 89f)] private float tapThreshold = TapRules.DefaultTapThreshold;
        [SerializeField, Range(1f, 45f)] private float untapThreshold = TapRules.DefaultUntapThreshold;
        [SerializeField] private Color attackerColor = new Color(1f, 0.3f, 0.25f);
        [SerializeField] private Color abilityColor = new Color(0.4f, 0.9f, 1f);
        [SerializeField] private ParticleSystem abilityVfx;

        [Header("Combat")]
        [Tooltip("Seconds the defending player has to slide a blocker next to the attacker.")]
        [SerializeField, Min(0f)] private float blockWindowSeconds = 3f;
        [Tooltip("A defending creature this close (table metres) to the attacker's card is its blocker.")]
        [SerializeField, Min(0.01f)] private float blockRadius = 0.12f;
        [Tooltip("If nobody blocks physically, let the rules pick the best legal blocker.")]
        [SerializeField] private bool autoBlock;
        [SerializeField] private bool enforceSummoningSickness = true;
        [Tooltip("0 = only BeginTurn() clears summoning sickness.")]
        [SerializeField, Min(0f)] private float sicknessAutoClearSeconds = 10f;
        [SerializeField, Min(1)] private int startingLife = 20;

        [Header("Flying")]
        [SerializeField, Min(0f)] private float flyingHeight = 0.30f;
        [SerializeField, Min(0.01f)] private float liftSpeed = 0.35f;

        [Header("Spells")]
        [SerializeField] private MtgSpellCaster spells = new MtgSpellCaster();

        private readonly Dictionary<LivingEntityController, PermanentState> _permanents = new Dictionary<LivingEntityController, PermanentState>();
        private readonly List<LivingEntityController> _snapshot = new List<LivingEntityController>();
        private readonly Dictionary<PlayerSide, int> _life = new Dictionary<PlayerSide, int>();

        public GameSystem System => GameSystem.MagicTheGathering;

        public event Action<PlayerSide, int> LifeChanged;
        public event Action<LivingEntityController> CreatureTapped;
        public event Action<LivingEntityController> CreatureUntapped;
        public event Action<LivingEntityController> AbilityActivated;

        private void Awake()
        {
            _life[PlayerSide.PlayerOne] = startingLife;
            _life[PlayerSide.PlayerTwo] = startingLife;
        }

        private void OnEnable() => HoloSpawnDirector.RegisterModule(this);

        private void OnDisable() => HoloSpawnDirector.UnregisterModule(this);

        // ─────────────────────────────── IGameRuleModule ───────────────────────────────

        public bool TryInterceptSpawn(SpawnContext context)
        {
            if (!(context.Definition is MtgCardDefinition card) || !card.IsSpell) return false;

            StartCoroutine(spells.Cast(card, context.Target.Position, context.Side, this));
            return true;
        }

        public void OnEntitySpawned(LivingEntityController entity, SpawnContext context)
        {
            if (!(entity.Definition is MtgCardDefinition card)) return;

            float yaw = context.Target != null && context.Target.HasAnchor ? TableSpace.Current.YawDegrees(context.Target.Anchor) : 0f;
            var state = new PermanentState
            {
                Tap = new TapState(false, yaw),
                SummoningSick = enforceSummoningSickness && card.IsCreature && !card.Creature.Has(MtgKeyword.Haste),
                EnteredAt = Time.time,
            };

            _permanents[entity] = state;
            entity.Despawned += OnDespawned;

            if (card.IsCreature && card.Creature.Has(MtgKeyword.Flying))
            {
                entity.SetHoverHeight(flyingHeight, liftSpeed);
                if (entity.GetComponent<ContactShadow>() == null) entity.gameObject.AddComponent<ContactShadow>();
            }

            RefreshTag(entity, state);
        }

        public bool OnTargetLost(TrackedTarget target, LivingEntityController entity) => false;

        // ─────────────────────────────── Public API ───────────────────────────────

        public int GetLife(PlayerSide side) => _life.TryGetValue(side, out int l) ? l : 0;

        public void ChangeLife(PlayerSide side, int delta, Vector3 popupPoint)
        {
            if (side == PlayerSide.Neutral || delta == 0) return;

            int life = GetLife(side) + delta;
            _life[side] = life;
            DamagePopupService.ShowText(popupPoint, $"{(delta > 0 ? "+" : "")}{delta}  ({life})",
                delta > 0 ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.3f, 0.3f), 1.3f);
            LifeChanged?.Invoke(side, life);

            if (life <= 0)
            {
                DamagePopupService.ShowText(TableSpace.Current.Origin + TableSpace.Current.Normal * 0.25f,
                    $"¡Victoria de {side.Opponent()}!", Color.white, 2f);
                EnvironmentDimmer.Current.Pulse(0.8f, 0.5f, 2.5f, 1.5f);
            }
        }

        /// <summary>
        /// Start of <paramref name="side"/>'s turn: clears summoning sickness for its creatures
        /// and removes marked damage from every creature (cleanup step of the previous turn).
        /// </summary>
        public void BeginTurn(PlayerSide side)
        {
            foreach (KeyValuePair<LivingEntityController, PermanentState> pair in _permanents)
            {
                LivingEntityController e = pair.Key;
                if (e == null || !e.IsAlive) continue;
                if (e.Side == side) pair.Value.SummoningSick = false;
                e.ReplaceVitals(Vitals.Full(e.Vitals.Max));
                RefreshTag(e, pair.Value);
            }
        }

        // ─────────────────────────────── Tap detection ───────────────────────────────

        private void Update()
        {
            TableSpace table = TableSpace.Current;
            _snapshot.Clear();
            _snapshot.AddRange(_permanents.Keys);

            foreach (LivingEntityController entity in _snapshot)
            {
                if (entity == null || !_permanents.TryGetValue(entity, out PermanentState state)) continue;

                if (state.SummoningSick && sicknessAutoClearSeconds > 0f && Time.time - state.EnteredAt >= sicknessAutoClearSeconds)
                {
                    state.SummoningSick = false;
                    RefreshTag(entity, state);
                }

                TrackedTarget target = entity.Target;
                if (target == null || !target.IsTracked || !target.HasAnchor || !entity.IsAlive) continue;

                (TapState next, TapTransition transition) = TapRules.Evaluate(
                    state.Tap, table.YawDegrees(target.Anchor), tapThreshold, untapThreshold);
                state.Tap = next;

                if (transition == TapTransition.Tapped) OnTapped(entity, state);
                else if (transition == TapTransition.Untapped) OnUntapped(entity, state);
            }
        }

        private void OnTapped(LivingEntityController entity, PermanentState state)
        {
            var card = (MtgCardDefinition)entity.Definition;
            CreatureTapped?.Invoke(entity);

            if (!card.IsCreature)
            {
                entity.SetStatusTag("HABILIDAD ACTIVADA", abilityColor);
                if (abilityVfx != null)
                {
                    ParticleSystem fx = Instantiate(abilityVfx, entity.CenterWorld, Quaternion.identity);
                    Destroy(fx.gameObject, 3f);
                }

                AbilityActivated?.Invoke(entity);
                return;
            }

            if (!MtgCombatRules.CanAttack(card.Creature, state.SummoningSick))
            {
                entity.SetStatusTag(state.SummoningSick ? "MAREO DE INVOCACIÓN" : "NO PUEDE ATACAR", Color.gray);
                return;
            }

            state.Attacking = true;
            entity.SetStatusTag("ATACANTE", attackerColor);
            Vector3 opponentEdge = TableSpace.Current.PlayerEdge(entity.Side.Opponent());
            entity.PlayAttack(opponentEdge, null); // war-cry lunge towards the defending player
            StartCoroutine(ResolveAttackRoutine(entity, state));
        }

        private void OnUntapped(LivingEntityController entity, PermanentState state)
        {
            state.Attacking = false;
            RefreshTag(entity, state);
            CreatureUntapped?.Invoke(entity);
        }

        // ─────────────────────────────── Combat resolution ───────────────────────────────

        private IEnumerator ResolveAttackRoutine(LivingEntityController attacker, PermanentState state)
        {
            DamagePopupService.ShowInfo(attacker.TopWorld, blockWindowSeconds > 0f ? $"¡Ataque! Bloquea en {blockWindowSeconds:0}s" : "¡Ataque!");
            if (blockWindowSeconds > 0f) yield return new WaitForSeconds(blockWindowSeconds);
            if (attacker == null || !attacker.IsAlive || !state.Attacking) yield break;

            var attackerCard = (MtgCardDefinition)attacker.Definition;
            LivingEntityController blocker = FindPhysicalBlocker(attacker, attackerCard) ?? (autoBlock ? FindAutoBlocker(attacker, attackerCard) : null);
            MtgCreature attackerCreature = attackerCard.Creature;
            MtgCreature blockerCreature = blocker != null ? ((MtgCardDefinition)blocker.Definition).Creature : null;

            MtgCombatOutcome outcome = MtgCombatRules.Resolve(
                attackerCreature,
                blockerCreature,
                attacker.Vitals.Missing,
                blocker != null ? blocker.Vitals.Missing : 0);

            ARCombatManager combat = ARCombatManager.Instance;
            PlayerSide defending = attacker.Side.Opponent();
            Vector3 face = TableSpace.Current.PlayerEdge(defending);

            if (outcome.LifeGainedByAttacker > 0)
            {
                ChangeLife(attacker.Side, outcome.LifeGainedByAttacker, TableSpace.Current.PlayerEdge(attacker.Side));
            }

            if (!outcome.Blocked)
            {
                QueueFaceHit(combat, attacker, face, defending, outcome.DamageToDefendingPlayer);
                yield break;
            }

            DamagePopupService.ShowInfo(blocker.TopWorld, "¡BLOQUEA!");
            bool blockerFirst = blockerCreature.Has(MtgKeyword.FirstStrike) && !attackerCreature.Has(MtgKeyword.FirstStrike);

            if (blockerFirst) QueueStrike(combat, blocker, attacker, outcome.DamageToAttacker, outcome.AttackerDies, blockerCreature);
            QueueStrike(combat, attacker, blocker, outcome.DamageToBlocker, outcome.BlockerDies, attackerCreature);
            if (!blockerFirst) QueueStrike(combat, blocker, attacker, outcome.DamageToAttacker, outcome.AttackerDies, blockerCreature);
            if (outcome.DamageToDefendingPlayer > 0) QueueFaceHit(combat, attacker, face, defending, outcome.DamageToDefendingPlayer);
        }

        private void QueueStrike(ARCombatManager combat, LivingEntityController from, LivingEntityController to, int damage, bool lethal, MtgCreature source)
        {
            if (damage <= 0 || to == null) return;

            // Deathtouch: any damage is lethal, so show at least the remaining toughness.
            int applied = lethal ? Mathf.Max(damage, to.Vitals.Current) : damage;
            string label = lethal && source.Has(MtgKeyword.Deathtouch) ? "Toque mortal" : null;

            if (combat == null)
            {
                to.ApplyDamage(applied, to.CenterWorld, label);
                return;
            }

            combat.RequestAttack(new AttackRequest { Attacker = from, Defender = to, Damage = applied, Label = label, ForceMelee = true });
        }

        private void QueueFaceHit(ARCombatManager combat, LivingEntityController attacker, Vector3 face, PlayerSide defending, int damage)
        {
            if (damage <= 0) return;

            if (combat == null)
            {
                ChangeLife(defending, -damage, face);
                return;
            }

            combat.RequestAttack(new AttackRequest
            {
                Attacker = attacker,
                TargetPointOverride = face,
                Damage = damage,
                ForceRanged = true,
                Tint = attacker.Definition.HologramTint,
                OnResolved = _ => ChangeLife(defending, -damage, face),
            });
        }

        private LivingEntityController FindPhysicalBlocker(LivingEntityController attacker, MtgCardDefinition attackerCard)
        {
            ARCombatManager combat = ARCombatManager.Instance;
            TableSpace table = TableSpace.Current;
            LivingEntityController best = null;
            float bestDistance = blockRadius;

            foreach (KeyValuePair<LivingEntityController, PermanentState> pair in _permanents)
            {
                LivingEntityController candidate = pair.Key;
                if (!IsLegalBlocker(attacker, attackerCard, candidate, pair.Value)) continue;

                float d = combat != null ? combat.TableDistance(attacker, candidate) : table.TableDistance(attacker.GroundWorld, candidate.GroundWorld);
                if (d <= bestDistance)
                {
                    bestDistance = d;
                    best = candidate;
                }
            }

            return best;
        }

        private LivingEntityController FindAutoBlocker(LivingEntityController attacker, MtgCardDefinition attackerCard)
        {
            var candidates = new List<LivingEntityController>();
            var creatures = new List<MtgCreature>();
            foreach (KeyValuePair<LivingEntityController, PermanentState> pair in _permanents)
            {
                if (!IsLegalBlocker(attacker, attackerCard, pair.Key, pair.Value)) continue;
                candidates.Add(pair.Key);
                creatures.Add(((MtgCardDefinition)pair.Key.Definition).Creature);
            }

            int index = MtgCombatRules.ChooseBestBlocker(attackerCard.Creature, creatures, attacker.Vitals.Missing);
            return index >= 0 ? candidates[index] : null;
        }

        private static bool IsLegalBlocker(LivingEntityController attacker, MtgCardDefinition attackerCard, LivingEntityController candidate, PermanentState state)
        {
            return candidate != null
                && candidate.IsAlive
                && attacker.Side.IsRivalOf(candidate.Side)
                && !state.Tap.IsTapped
                && candidate.Definition is MtgCardDefinition card
                && card.IsCreature
                && MtgCombatRules.CanBlock(attackerCard.Creature, card.Creature);
        }

        private void RefreshTag(LivingEntityController entity, PermanentState state)
        {
            if (state.Attacking) return;
            entity.SetStatusTag(state.SummoningSick ? "Mareo de invocación" : (state.Tap.IsTapped ? "GIRADA" : ""), Color.gray);
        }

        private void OnDespawned(LivingEntityController entity)
        {
            entity.Despawned -= OnDespawned;
            _permanents.Remove(entity);
        }
    }
}
