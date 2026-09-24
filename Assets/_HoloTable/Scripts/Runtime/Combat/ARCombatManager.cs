using System;
using System.Collections;
using System.Collections.Generic;
using HoloTable.Core;
using HoloTable.Domain;
using HoloTable.Domain.Combat;
using HoloTable.Domain.Warhammer;
using HoloTable.Entities;
using HoloTable.VFX;
using UnityEngine;
using UnityEngine.Pool;

namespace HoloTable.Combat
{
    /// <summary>
    /// Global choreographer of fights between holograms.
    ///
    ///  • Scans the physical distance between tracked cards/bases (on the table plane)
    ///    and raises EngagementStarted / EngagementEnded for rival pairs in range.
    ///  • Runs attack coroutines: attacker animation → impact frame → pooled particle
    ///    projectile that homes on the defender's exact position (or melee contact) →
    ///    impact VFX → defender loses HP and plays its hit reaction (or dies).
    ///  • Queues and throttles simultaneous attacks so the table stays readable.
    /// Rules (how much damage) belong to the game modules; this class never decides them.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class ARCombatManager : MonoBehaviour
    {
        [Header("Engagement detection")]
        [Tooltip("Card games: distance between card centres that counts as a face-off (metres).")]
        [SerializeField, Min(0.01f)] private float cardEngagementRange = 0.25f;
        [Tooltip("Warhammer engagement range in inches (base edge to base edge).")]
        [SerializeField, Min(0f)] private float warhammerEngagementInches = 1f;
        [SerializeField, Min(0.02f)] private float scanInterval = 0.15f;

        [Header("Attack choreography")]
        [Tooltip("Closer than this (metres) → melee strike, no projectile.")]
        [SerializeField, Min(0f)] private float meleeRange = 0.1f;
        [SerializeField] private HoloProjectile defaultProjectile;
        [SerializeField] private ParticleSystem defaultImpactVfx;
        [SerializeField] private AudioClip impactSfx;
        [SerializeField, Min(1)] private int maxConcurrentAttacks = 3;
        [SerializeField, Min(0f)] private float hitReactionPause = 0.35f;
        [SerializeField, Min(0.5f)] private float stageTimeout = 4f;
        [SerializeField, Range(0, 16)] private int prewarmCount = 4;

        [Header("Demo mode")]
        [Tooltip("Engaged rivals fight automatically using their ATK stat. Leave off when game modules drive combat.")]
        [SerializeField] private bool autoBattleOnEngagement;
        [SerializeField, Min(0.2f)] private float autoBattleCooldown = 2.5f;

        private readonly List<Combatant> _combatants = new List<Combatant>();
        private readonly List<Engagement> _scan = new List<Engagement>();
        private readonly HashSet<Engagement> _engaged = new HashSet<Engagement>();
        private readonly HashSet<Engagement> _stillEngaged = new HashSet<Engagement>();
        private readonly Queue<AttackRequest> _queue = new Queue<AttackRequest>();
        private readonly HashSet<int> _busyAttackers = new HashSet<int>();
        private readonly Dictionary<int, float> _autoBattleReady = new Dictionary<int, float>();
        private readonly Dictionary<HoloProjectile, ObjectPool<HoloProjectile>> _pools = new Dictionary<HoloProjectile, ObjectPool<HoloProjectile>>();
        private readonly HashSet<AttackRequest> _inFlight = new HashSet<AttackRequest>();
        private Predicate<Engagement> _endedPredicate;

        private float _nextScan;
        private int _running;

        public static ARCombatManager Instance { get; private set; }

        public event Action<LivingEntityController, LivingEntityController, float> EngagementStarted;
        public event Action<LivingEntityController, LivingEntityController> EngagementEnded;
        public event Action<AttackReport> AttackResolved;

        public IReadOnlyCollection<Engagement> Engagements => _engaged;
        public bool IsBusy => _running > 0 || _queue.Count > 0;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[HoloTable] Duplicate ARCombatManager destroyed.", this);
                Destroy(this);
                return;
            }

            Instance = this;
            _endedPredicate = RemoveIfEnded;

            if (defaultProjectile != null)
            {
                ObjectPool<HoloProjectile> pool = GetPool(defaultProjectile);
                var warm = new List<HoloProjectile>(prewarmCount);
                for (int i = 0; i < prewarmCount; i++) warm.Add(pool.Get());
                foreach (HoloProjectile p in warm) pool.Release(p);
            }

            VfxPool.Prewarm(defaultImpactVfx, prewarmCount);
        }

        private void OnDisable()
        {
            // Coroutines die with the component: never leave a caller waiting on OnResolved.
            int dropped = _queue.Count + _inFlight.Count;
            foreach (AttackRequest r in new List<AttackRequest>(_inFlight)) Complete(r, new AttackReport(r.Attacker, r.Defender, 0, false, false, Vector3.zero));
            while (_queue.Count > 0)
            {
                AttackRequest r = _queue.Dequeue();
                Complete(r, new AttackReport(r.Attacker, r.Defender, 0, false, false, Vector3.zero));
            }

            _inFlight.Clear();
            _busyAttackers.Clear();
            _running = 0;
            if (dropped > 0) Debug.LogWarning($"[HoloTable] ARCombatManager disabled with {dropped} attack(s) pending; resolved as missed.", this);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (ObjectPool<HoloProjectile> pool in _pools.Values) pool.Clear();
        }

        private void Update()
        {
            if (Time.time >= _nextScan)
            {
                _nextScan = Time.time + scanInterval;
                ScanEngagements();
            }

            while (_queue.Count > 0 && _running < maxConcurrentAttacks)
            {
                AttackRequest next = _queue.Peek();
                if (next.Attacker != null && _busyAttackers.Contains(next.Attacker.EntityId)) break;
                _queue.Dequeue();
                StartCoroutine(RunAttack(next));
            }
        }

        // ─────────────────────────────── Public API ───────────────────────────────

        public void RequestAttack(AttackRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.Defender == null && !request.TargetPointOverride.HasValue)
            {
                Debug.LogWarning("[HoloTable] AttackRequest needs a Defender or a TargetPointOverride.", this);
                return;
            }

            _queue.Enqueue(request);
        }

        /// <summary>Physical distance between two entities' cards/bases, on the table plane (metres).</summary>
        public float TableDistance(LivingEntityController a, LivingEntityController b) =>
            TableSpace.Current.TableDistance(PhysicalPosition(a), PhysicalPosition(b));

        public bool AreEngaged(LivingEntityController a, LivingEntityController b) =>
            a != null && b != null && _engaged.Contains(new Engagement(a.EntityId, b.EntityId, 0f));

        public LivingEntityController FindEngagedRival(LivingEntityController entity)
        {
            foreach (Engagement e in _engaged)
            {
                if (!e.Involves(entity.EntityId)) continue;
                LivingEntityController other = EntityRegistry.Find(e.FirstId == entity.EntityId ? e.SecondId : e.FirstId);
                if (other != null && other.IsTargetable) return other;
            }

            return null;
        }

        // ─────────────────────────────── Engagement scan ───────────────────────────────

        private void ScanEngagements()
        {
            TableSpace table = TableSpace.Current;
            _combatants.Clear();
            foreach (LivingEntityController e in EntityRegistry.All)
            {
                if (!e.IsTargetable) continue;
                _combatants.Add(new Combatant(e.EntityId, e.Side, table.ToTable2D(PhysicalPosition(e)), EngagementRadius(e)));
            }

            EngagementRules.FindEngagements(_combatants, _scan);

            _stillEngaged.Clear();
            foreach (Engagement e in _scan)
            {
                _stillEngaged.Add(e);
                if (_engaged.Add(e))
                {
                    LivingEntityController a = EntityRegistry.Find(e.FirstId);
                    LivingEntityController b = EntityRegistry.Find(e.SecondId);
                    EngagementStarted?.Invoke(a, b, e.Distance);
                }
            }

            _engaged.RemoveWhere(_endedPredicate);

            if (autoBattleOnEngagement) DriveAutoBattle();
        }

        private bool RemoveIfEnded(Engagement e)
        {
            if (_stillEngaged.Contains(e)) return false;
            EngagementEnded?.Invoke(EntityRegistry.Find(e.FirstId), EntityRegistry.Find(e.SecondId));
            return true;
        }

        private float EngagementRadius(LivingEntityController e)
        {
            float baseRadius = e.WorldFootprint * 0.5f;
            return e.Definition.System == GameSystem.Warhammer
                ? TableUnits.InchesToMeters(warhammerEngagementInches) + baseRadius * 2f
                : cardEngagementRange;
        }

        private void DriveAutoBattle()
        {
            foreach (Engagement e in _engaged)
            {
                TryAutoAttack(EntityRegistry.Find(e.FirstId), EntityRegistry.Find(e.SecondId));
                TryAutoAttack(EntityRegistry.Find(e.SecondId), EntityRegistry.Find(e.FirstId));
            }
        }

        private void TryAutoAttack(LivingEntityController attacker, LivingEntityController defender)
        {
            if (attacker == null || defender == null || !attacker.CanAct || !defender.IsTargetable) return;
            if (_busyAttackers.Contains(attacker.EntityId)) return;
            if (_autoBattleReady.TryGetValue(attacker.EntityId, out float ready) && Time.time < ready) return;

            _autoBattleReady[attacker.EntityId] = Time.time + autoBattleCooldown + UnityEngine.Random.value;
            RequestAttack(new AttackRequest
            {
                Attacker = attacker,
                Defender = defender,
                Damage = Mathf.Max(1, attacker.AttackValue),
            });
        }

        // ─────────────────────────────── Attack coroutine ───────────────────────────────

        private IEnumerator RunAttack(AttackRequest r)
        {
            _running++;
            _inFlight.Add(r);
            LivingEntityController attacker = r.Attacker;
            LivingEntityController defender = r.Defender;
            int attackerId = attacker != null ? attacker.EntityId : 0;
            if (attacker != null) _busyAttackers.Add(attackerId);

            try
            {
                Vector3 fallbackPoint = r.TargetPointOverride ?? (defender != null ? defender.CenterWorld : Vector3.zero);
                Vector3 TargetPoint() => defender != null && defender.IsAlive ? defender.CenterWorld : fallbackPoint;

                // 1. Attacker animation up to the impact frame.
                if (attacker != null && attacker.IsAlive && !r.SkipAttackAnimation)
                {
                    bool impact = false;
                    attacker.PlayAttack(TargetPoint(), () => impact = true);
                    float deadline = Time.time + stageTimeout;
                    while (!impact && attacker != null && Time.time < deadline) yield return null;
                    if (!impact && attacker != null)
                    {
                        Debug.LogWarning($"[HoloTable] {attacker.name}: attack impact not signalled within {stageTimeout}s (missing AnimEvent_AttackImpact?). Continuing.", attacker);
                    }
                }

                // 2. Melee contact or homing projectile.
                bool melee = !r.ForceRanged && (r.ForceMelee
                    || (attacker != null && defender != null && TableDistance(attacker, defender) <= meleeRange));

                Vector3 impactPoint = TargetPoint();
                if (melee)
                {
                    if (attacker != null && defender != null)
                    {
                        Vector3 dir = (attacker.CenterWorld - defender.CenterWorld).normalized;
                        impactPoint = defender.CenterWorld + dir * (defender.WorldFootprint * 0.5f);
                    }
                }
                else
                {
                    Vector3 origin = r.OriginOverride ?? (attacker != null ? attacker.MuzzleWorld : TargetPoint() + TableSpace.Current.Normal * 0.4f);
                    HoloProjectile prefab = r.Projectile != null ? r.Projectile : defaultProjectile;

                    if (prefab != null)
                    {
                        bool arrived = false;
                        ObjectPool<HoloProjectile> pool = GetPool(prefab);
                        HoloProjectile projectile = pool.Get();
                        projectile.Launch(origin, TargetPoint, r.Tint, p => { arrived = true; impactPoint = p; }, pool.Release);

                        float deadline = Time.time + stageTimeout;
                        while (!arrived && Time.time < deadline) yield return null;
                        if (!arrived)
                        {
                            Debug.LogWarning($"[HoloTable] Projectile '{prefab.name}' did not arrive within {stageTimeout}s (speed too low?). Applying hit now.", this);
                            impactPoint = TargetPoint();
                        }
                    }
                    else
                    {
                        yield return new WaitForSeconds(Vector3.Distance(origin, TargetPoint()) / 1.2f);
                        impactPoint = TargetPoint();
                    }
                }

                // 3. Impact: VFX, SFX, damage & hit reaction.
                ParticleSystem impactVfx = r.ImpactVfx != null ? r.ImpactVfx : defaultImpactVfx;
                VfxPool.Play(impactVfx, impactPoint, Quaternion.LookRotation(TableSpace.Current.Normal), 1f, r.Tint);
                if (impactSfx != null) AudioSource.PlayClipAtPoint(impactSfx, impactPoint, 0.8f);

                bool landed = defender != null && defender.IsAlive;
                if (landed) defender.ApplyDamage(r.Damage, impactPoint, r.Label);
                bool defeated = landed && !defender.IsAlive;

                if (hitReactionPause > 0f) yield return new WaitForSeconds(hitReactionPause);

                Complete(r, new AttackReport(attacker, defender, r.Damage, landed || defender == null, defeated, impactPoint));
            }
            finally
            {
                _inFlight.Remove(r);
                if (attackerId != 0) _busyAttackers.Remove(attackerId);
                _running = Mathf.Max(0, _running - 1);
                if (!r.Completed) Complete(r, new AttackReport(attacker, defender, 0, false, false, Vector3.zero));
            }
        }

        private void Complete(AttackRequest r, AttackReport report)
        {
            if (r.Completed) return;
            r.Completed = true;

            try
            {
                r.OnResolved?.Invoke(report);
                AttackResolved?.Invoke(report);
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }
        }

        // ─────────────────────────────── Helpers ───────────────────────────────

        private static Vector3 PhysicalPosition(LivingEntityController e)
        {
            Tracking.TrackedTarget t = e.Target;
            return t != null && t.HasAnchor ? t.Anchor.position : e.transform.position;
        }

        private ObjectPool<HoloProjectile> GetPool(HoloProjectile prefab)
        {
            if (_pools.TryGetValue(prefab, out ObjectPool<HoloProjectile> pool)) return pool;

            pool = new ObjectPool<HoloProjectile>(
                () => Instantiate(prefab, transform),
                p => { },
                p => p.gameObject.SetActive(false),
                p => { if (p != null) Destroy(p.gameObject); },
                collectionCheck: false,
                defaultCapacity: 4,
                maxSize: 32);
            _pools.Add(prefab, pool);
            return pool;
        }
    }
}
