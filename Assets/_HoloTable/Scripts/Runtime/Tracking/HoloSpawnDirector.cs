using System;
using System.Collections;
using System.Collections.Generic;
using HoloTable.Core;
using HoloTable.Data;
using HoloTable.Domain;
using HoloTable.Entities;
using HoloTable.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HoloTable.Tracking
{
    /// <summary>
    /// Single entry point between tracking SDKs and gameplay.
    ///
    /// Adapters (AR Foundation, Vuforia, UnityEvents, Quest…) call
    /// <see cref="ReportFound"/> / <see cref="ReportLost"/>. The director:
    ///  • debounces flicker (confirm delay on found, grace period on lost),
    ///  • resolves the card in the catalog and lets game modules intercept it,
    ///  • spawns / despawns holograms so Spawn and Die are synced with the physical card,
    ///  • keeps "orphan" holograms as ghosts for a per-game persistence window so a miniature
    ///    lifted to be moved, or a card briefly covered, re-attaches with its state intact,
    ///  • remembers "spent" cards (KO'd creature, cast spell) so re-detection doesn't replay them.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class HoloSpawnDirector : MonoBehaviour
    {
        private sealed class TargetEntry
        {
            public TargetEntry(TrackedTarget target) => Target = target;

            public TrackedTarget Target { get; }
            public LivingEntityController Entity { get; set; }
            public EntityDefinition Definition { get; set; }
            public Coroutine PendingRoutine { get; set; }
            public bool Resolved { get; set; }
            public bool Spent { get; set; }
        }

        private sealed class Orphan
        {
            public LivingEntityController Entity;
            public float ExpiresAt;
            public Vector3 LastPosition;
        }

        [SerializeField] private CardCatalog catalog;
        [SerializeField] private EntityHUD hudPrefab;
        [Tooltip("Card must be seen continuously this long before its hologram appears (filters false positives).")]
        [SerializeField, Min(0f)] private float foundConfirmDelay = 0.12f;
        [Tooltip("Card may disappear this long (hand passing over it, glare) before the hologram leaves its card.")]
        [SerializeField, Min(0f)] private float lostGraceSeconds = 0.6f;

        [Header("Persistence after loss (ghost, then re-attach or dissolve)")]
        [Tooltip("Pokémon: allows swapping in the evolution card and brief occlusions.")]
        [SerializeField, Min(0f)] private float pokemonPersistence = 2.5f;
        [Tooltip("MTG: removing a permanent from the battlefield is meaningful, so it dissolves.")]
        [SerializeField, Min(0f)] private float mtgPersistence;
        [Tooltip("Warhammer: models are lifted to be moved; keep their wounds while in the air.")]
        [SerializeField, Min(0f)] private float warhammerPersistence = 20f;
        [Tooltip("Cards re-attach to their orphan only within this distance (metres). Warhammer ignores it.")]
        [SerializeField, Min(0.01f)] private float reattachMaxDistance = 0.08f;

        [Tooltip("A spent card (KO'd, cast) re-seen within this window is ignored instead of replayed.")]
        [SerializeField, Min(0f)] private float spentMemorySeconds = 8f;
        [SerializeField] private bool verboseLogging;

        private static readonly List<IGameRuleModule> Modules = new List<IGameRuleModule>();

        private readonly Dictionary<string, TargetEntry> _entries = new Dictionary<string, TargetEntry>();
        private readonly HashSet<LivingEntityController> _entities = new HashSet<LivingEntityController>();
        private readonly Dictionary<LivingEntityController, TargetEntry> _entryByEntity = new Dictionary<LivingEntityController, TargetEntry>();
        private readonly List<Orphan> _orphans = new List<Orphan>();
        private readonly Dictionary<string, float> _spentMemory = new Dictionary<string, float>();
        private readonly HashSet<EntityDefinition> _warnedDefinitions = new HashSet<EntityDefinition>();

        public static HoloSpawnDirector Instance { get; private set; }

        private static bool _warnedMissing;

        /// <summary>
        /// Instance for tracking adapters. Logs once (with the adapter as context) when the scene has no
        /// director, instead of silently dropping every detection.
        /// </summary>
        public static HoloSpawnDirector ForAdapter(Object adapter)
        {
            if (Instance == null && !_warnedMissing)
            {
                _warnedMissing = true;
                Debug.LogWarning("[HoloTable] No active HoloSpawnDirector in the scene: tracking events are being dropped.", adapter);
            }

            return Instance;
        }

        public IReadOnlyCollection<LivingEntityController> ActiveEntities => _entities;

        public event Action<LivingEntityController> EntitySpawned;
        public event Action<LivingEntityController> EntityRemoved;
        public event Action<LivingEntityController> EntityReattached;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Modules.Clear();
            _warnedMissing = false;
        }

        public static void RegisterModule(IGameRuleModule module)
        {
            if (module != null && !Modules.Contains(module)) Modules.Add(module);
        }

        public static void UnregisterModule(IGameRuleModule module) => Modules.Remove(module);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[HoloTable] Duplicate HoloSpawnDirector destroyed.", this);
                Destroy(this);
                return;
            }

            Instance = this;
            if (catalog == null)
            {
                Debug.LogError("[HoloTable] HoloSpawnDirector has no CardCatalog assigned: no card will ever spawn.", this);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            for (int i = _orphans.Count - 1; i >= 0; i--)
            {
                Orphan o = _orphans[i];
                if (o.Entity == null)
                {
                    _orphans.RemoveAt(i);
                    continue;
                }

                if (Time.time < o.ExpiresAt) continue;

                _orphans.RemoveAt(i);
                o.Entity.Despawn();
                Log($"Orphan {o.Entity.name} expired; dissolving.");
            }
        }

        // ─────────────────────────────── Tracking input ───────────────────────────────

        /// <summary>Card / miniature seen (or seen again). Safe to call every frame.</summary>
        public void ReportFound(string instanceId, string referenceName, Transform anchor, Vector2 physicalSize)
        {
            if (string.IsNullOrEmpty(instanceId)) return;

            if (_entries.TryGetValue(instanceId, out TargetEntry entry))
            {
                bool wasLost = !entry.Target.IsTracked;
                entry.Target.MarkSeen(anchor, physicalSize);
                if (!wasLost) return;

                // Re-acquired inside the grace period → cancel the pending loss.
                if (entry.PendingRoutine != null && entry.Resolved)
                {
                    StopCoroutine(entry.PendingRoutine);
                    entry.PendingRoutine = null;
                    if (entry.Entity != null) entry.Entity.Follow(entry.Target);
                    Log($"'{referenceName}' re-acquired, loss cancelled.");
                }

                return;
            }

            var target = new TrackedTarget(instanceId, referenceName, anchor, physicalSize);
            entry = new TargetEntry(target);
            _entries.Add(instanceId, entry);

            if (_spentMemory.TryGetValue(instanceId, out float until) && Time.time < until)
            {
                // Same physical card put straight back (KO'd creature, cast spell): don't replay it.
                entry.Resolved = true;
                entry.Spent = true;
                Log($"'{referenceName}' is spent; ignoring re-detection.");
                return;
            }

            entry.PendingRoutine = StartCoroutine(ConfirmFoundRoutine(entry));
        }

        /// <summary>Tracking degraded (Vuforia LIMITED / ARF Limited). Holograms freeze in place.</summary>
        public void ReportLimited(string instanceId)
        {
            if (!string.IsNullOrEmpty(instanceId) && _entries.TryGetValue(instanceId, out TargetEntry entry)) entry.Target.MarkLimited();
        }

        /// <summary>Card no longer visible. The hologram leaves its card after the grace period.</summary>
        public void ReportLost(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId) || !_entries.TryGetValue(instanceId, out TargetEntry entry)) return;
            if (entry.Target.Status == TrackingStatus.Lost) return;

            entry.Target.MarkLost();

            if (!entry.Resolved)
            {
                // Never confirmed: a false positive, forget silently (by design).
                if (entry.PendingRoutine != null) StopCoroutine(entry.PendingRoutine);
                _entries.Remove(instanceId);
                return;
            }

            if (entry.PendingRoutine != null) StopCoroutine(entry.PendingRoutine);
            entry.PendingRoutine = StartCoroutine(LostGraceRoutine(entry));
        }

        // ─────────────────────────────── API for game modules ───────────────────────────────

        /// <summary>Instantiates, initialises and (optionally) plays the Spawn sequence for a definition.</summary>
        public LivingEntityController SpawnEntity(EntityDefinition definition, TrackedTarget target, PlayerSide side, bool playSpawn = true)
        {
            if (definition == null || definition.Prefab == null)
            {
                string label = definition != null ? definition.name : "<null>";
                Debug.LogWarning($"[HoloTable] '{label}' has no hologram prefab; nothing to spawn.", this);
                return null;
            }

            Vector3 position = target != null && target.HasAnchor ? target.Anchor.position : transform.position;
            LivingEntityController entity = Instantiate(definition.Prefab, position, Quaternion.identity);
            entity.name = $"{definition.DisplayName} [{side}]";
            entity.Initialize(definition, target, side);
            if (hudPrefab != null) entity.AttachHud(Instantiate(hudPrefab));

            _entities.Add(entity);
            entity.Despawned += OnEntityDespawned;
            entity.Died += OnEntityDied;

            if (target != null && _entries.TryGetValue(target.InstanceId, out TargetEntry entry))
            {
                entry.Entity = entity;
                _entryByEntity[entity] = entry;
            }

            if (playSpawn) entity.Spawn();

            var context = new SpawnContext(definition, target, side, this);
            foreach (IGameRuleModule module in SnapshotModules())
            {
                if (module.System == definition.System) module.OnEntitySpawned(entity, context);
            }

            EntitySpawned?.Invoke(entity);
            Log($"Spawned {entity.name}.");
            return entity;
        }

        /// <summary>
        /// Unlinks an entity from its card (or from the orphan list) so the director no longer
        /// manages its lifetime. Used by evolution: the old card gets covered by the new one.
        /// </summary>
        public void DetachEntity(LivingEntityController entity)
        {
            if (entity == null) return;

            RemoveOrphan(entity);
            if (_entryByEntity.TryGetValue(entity, out TargetEntry entry))
            {
                _entryByEntity.Remove(entity);
                entry.Entity = null;
                entry.Spent = true;
            }

            entity.StopFollowing();
        }

        public void DespawnEntity(LivingEntityController entity, bool animated = true)
        {
            if (entity == null) return;
            DetachEntity(entity);
            entity.Despawn(animated);
        }

        public TrackedTarget GetTarget(LivingEntityController entity) =>
            entity != null && _entryByEntity.TryGetValue(entity, out TargetEntry e) ? e.Target : null;

        /// <summary>True while the entity is a ghost waiting for its card to come back.</summary>
        public bool IsOrphan(LivingEntityController entity) => FindOrphanIndex(entity) >= 0;

        public PlayerSide ResolveSide(TrackedTarget target) =>
            target != null && target.HasAnchor ? TableSpace.Current.ResolveSide(target.Anchor.position) : PlayerSide.Neutral;

        // ─────────────────────────────── Internals ───────────────────────────────

        private IEnumerator ConfirmFoundRoutine(TargetEntry entry)
        {
            if (foundConfirmDelay > 0f) yield return new WaitForSeconds(foundConfirmDelay);

            entry.PendingRoutine = null;
            entry.Resolved = true;

            if (catalog == null || !catalog.TryResolve(entry.Target.ReferenceName, out EntityDefinition definition))
            {
                if (catalog != null)
                {
                    Debug.LogWarning($"[HoloTable] No definition for reference image '{entry.Target.ReferenceName}' in '{catalog.name}'.", this);
                }

                entry.Spent = true;
                yield break;
            }

            entry.Definition = definition;
            PlayerSide side = ResolveSide(entry.Target);

            if (TryReattachOrphan(entry, definition, side)) yield break;

            var context = new SpawnContext(definition, entry.Target, side, this);
            bool hasModule = false;
            foreach (IGameRuleModule module in SnapshotModules())
            {
                if (module.System != definition.System) continue;
                hasModule = true;
                if (module.TryInterceptSpawn(context))
                {
                    entry.Spent = entry.Entity == null;
                    Log($"'{definition.DisplayName}' handled by {module.GetType().Name}.");
                    yield break;
                }
            }

            if (!definition.SpawnsCreature)
            {
                entry.Spent = true;
                WarnOnce(definition, hasModule
                    ? $"'{definition.DisplayName}' does not spawn a creature (no prefab assigned, or not a creature card)."
                    : $"'{definition.DisplayName}' ({definition.System}) was ignored: no GameLogic module for {definition.System} in the scene, and it has no creature prefab.");
                yield break;
            }

            if (!hasModule)
            {
                WarnOnce(definition, $"No GameLogic module for {definition.System} in the scene; '{definition.DisplayName}' spawns without game rules.");
            }

            SpawnEntity(definition, entry.Target, side);
        }

        private IEnumerator LostGraceRoutine(TargetEntry entry)
        {
            if (lostGraceSeconds > 0f) yield return new WaitForSeconds(lostGraceSeconds);

            entry.PendingRoutine = null;
            _entries.Remove(entry.Target.InstanceId);
            if (entry.Spent && spentMemorySeconds > 0f)
            {
                _spentMemory[entry.Target.InstanceId] = Time.time + spentMemorySeconds;
            }

            LivingEntityController entity = entry.Entity;
            if (entity != null) _entryByEntity.Remove(entity);

            if (entry.Definition != null)
            {
                foreach (IGameRuleModule module in SnapshotModules())
                {
                    if (module.System == entry.Definition.System && module.OnTargetLost(entry.Target, entity))
                    {
                        Log($"'{entry.Target.ReferenceName}' lost; entity claimed by {module.GetType().Name}.");
                        yield break;
                    }
                }
            }

            if (entity == null) yield break;

            entity.StopFollowing();
            float persistence = PersistenceFor(entity.Definition.System);
            if (persistence > 0f && entity.IsAlive)
            {
                entity.SetGhosted(true);
                _orphans.Add(new Orphan
                {
                    Entity = entity,
                    ExpiresAt = Time.time + persistence,
                    LastPosition = entry.Target.HasAnchor ? entry.Target.Position : entity.GroundWorld,
                });
                Log($"'{entry.Target.ReferenceName}' lost; {entity.name} waits {persistence:0.0}s as a ghost.");
                yield break;
            }

            entity.Despawn();
            Log($"'{entry.Target.ReferenceName}' lost; despawning.");
        }

        /// <summary>Re-links a ghost to the same card (or a moved miniature) instead of spawning a clone.</summary>
        private bool TryReattachOrphan(TargetEntry entry, EntityDefinition definition, PlayerSide side)
        {
            TableSpace table = TableSpace.Current;
            int best = -1;
            float bestDistance = float.MaxValue;
            bool ignoreDistance = definition.System == GameSystem.Warhammer;

            for (int i = 0; i < _orphans.Count; i++)
            {
                LivingEntityController candidate = _orphans[i].Entity;
                if (candidate == null || !candidate.IsAlive || candidate.Definition != definition) continue;
                if (side != PlayerSide.Neutral && candidate.Side != side && !ignoreDistance) continue;

                float d = table.TableDistance(_orphans[i].LastPosition, entry.Target.Position);
                if ((ignoreDistance || d <= reattachMaxDistance) && d < bestDistance)
                {
                    best = i;
                    bestDistance = d;
                }
            }

            if (best < 0) return false;

            LivingEntityController entity = _orphans[best].Entity;
            _orphans.RemoveAt(best);
            entry.Entity = entity;
            _entryByEntity[entity] = entry;
            entity.SetGhosted(false);
            entity.Follow(entry.Target);
            EntityReattached?.Invoke(entity);
            Log($"{entity.name} re-attached to '{entry.Target.ReferenceName}'.");
            return true;
        }

        private float PersistenceFor(GameSystem system) => system switch
        {
            GameSystem.Pokemon => pokemonPersistence,
            GameSystem.MagicTheGathering => mtgPersistence,
            GameSystem.Warhammer => warhammerPersistence,
            _ => 0f,
        };

        private void OnEntityDied(LivingEntityController entity)
        {
            // KO'd while its card is still on the table: don't resurrect on re-detection.
            RemoveOrphan(entity);
            if (_entryByEntity.TryGetValue(entity, out TargetEntry entry))
            {
                entry.Entity = null;
                entry.Spent = true;
                _entryByEntity.Remove(entity);
            }
        }

        private void OnEntityDespawned(LivingEntityController entity)
        {
            entity.Despawned -= OnEntityDespawned;
            entity.Died -= OnEntityDied;
            _entities.Remove(entity);
            RemoveOrphan(entity);
            if (_entryByEntity.TryGetValue(entity, out TargetEntry entry))
            {
                entry.Entity = null;
                _entryByEntity.Remove(entity);
            }

            EntityRemoved?.Invoke(entity);
        }

        private int FindOrphanIndex(LivingEntityController entity)
        {
            for (int i = 0; i < _orphans.Count; i++)
            {
                if (_orphans[i].Entity == entity) return i;
            }

            return -1;
        }

        private void RemoveOrphan(LivingEntityController entity)
        {
            int index = FindOrphanIndex(entity);
            if (index >= 0) _orphans.RemoveAt(index);
        }

        private void WarnOnce(EntityDefinition definition, string message)
        {
            if (_warnedDefinitions.Add(definition)) Debug.LogWarning($"[HoloTable] {message}", definition);
        }

        private static IGameRuleModule[] SnapshotModules() => Modules.ToArray();

        private void Log(string message)
        {
            if (verboseLogging) Debug.Log($"[HoloTable] {message}", this);
        }
    }
}
