using System.Collections;
using System.Collections.Generic;
using HoloTable.Core;
using HoloTable.Data;
using HoloTable.Domain;
using HoloTable.Entities;
using HoloTable.UI;
using UnityEngine;

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
    ///  • remembers "spent" cards (KO'd creature still on the table) so they don't respawn.
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

        [SerializeField] private CardCatalog catalog;
        [SerializeField] private EntityHUD hudPrefab;
        [Tooltip("Card must be seen continuously this long before its hologram appears (filters false positives).")]
        [SerializeField, Min(0f)] private float foundConfirmDelay = 0.12f;
        [Tooltip("Card may disappear this long (hand passing over it, glare) before the hologram dies.")]
        [SerializeField, Min(0f)] private float lostGraceSeconds = 0.6f;
        [SerializeField] private bool verboseLogging;

        private static readonly List<IGameRuleModule> Modules = new List<IGameRuleModule>();

        private readonly Dictionary<string, TargetEntry> _entries = new Dictionary<string, TargetEntry>();
        private readonly HashSet<LivingEntityController> _entities = new HashSet<LivingEntityController>();
        private readonly Dictionary<LivingEntityController, TargetEntry> _entryByEntity = new Dictionary<LivingEntityController, TargetEntry>();

        public static HoloSpawnDirector Instance { get; private set; }

        public IReadOnlyCollection<LivingEntityController> ActiveEntities => _entities;

        public System.Action<LivingEntityController> EntitySpawned;
        public System.Action<LivingEntityController> EntityRemoved;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Modules.Clear();

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
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
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

                // Re-acquired inside the grace period → cancel the pending death.
                if (entry.PendingRoutine != null && entry.Resolved)
                {
                    StopCoroutine(entry.PendingRoutine);
                    entry.PendingRoutine = null;
                    if (entry.Entity != null) entry.Entity.Follow(entry.Target);
                    Log($"'{referenceName}' re-acquired, death cancelled.");
                }

                return;
            }

            var target = new TrackedTarget(instanceId, referenceName, anchor, physicalSize);
            entry = new TargetEntry(target);
            _entries.Add(instanceId, entry);
            entry.PendingRoutine = StartCoroutine(ConfirmFoundRoutine(entry));
        }

        /// <summary>Tracking degraded (Vuforia LIMITED / ARF Limited). Holograms freeze in place.</summary>
        public void ReportLimited(string instanceId)
        {
            if (_entries.TryGetValue(instanceId, out TargetEntry entry)) entry.Target.MarkLimited();
        }

        /// <summary>Card no longer visible. The hologram dies after the grace period.</summary>
        public void ReportLost(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId) || !_entries.TryGetValue(instanceId, out TargetEntry entry)) return;
            if (entry.Target.Status == TrackingStatus.Lost) return;

            entry.Target.MarkLost();

            if (!entry.Resolved)
            {
                // Never confirmed: it was a false positive, forget silently.
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
                Debug.LogWarning($"[HoloTable] '{definition?.name}' has no hologram prefab.", this);
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
        /// Unlinks an entity from its card so losing the card no longer kills it
        /// (evolution: the old card gets covered by the new one).
        /// </summary>
        public void DetachEntity(LivingEntityController entity)
        {
            if (entity == null || !_entryByEntity.TryGetValue(entity, out TargetEntry entry)) return;
            _entryByEntity.Remove(entity);
            entry.Entity = null;
            entry.Spent = true;
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
                Debug.LogWarning($"[HoloTable] No definition for reference image '{entry.Target.ReferenceName}'.", this);
                entry.Spent = true;
                yield break;
            }

            entry.Definition = definition;
            PlayerSide side = ResolveSide(entry.Target);
            var context = new SpawnContext(definition, entry.Target, side, this);

            foreach (IGameRuleModule module in SnapshotModules())
            {
                if (module.System == definition.System && module.TryInterceptSpawn(context))
                {
                    entry.Spent = entry.Entity == null;
                    Log($"'{definition.DisplayName}' handled by {module.GetType().Name}.");
                    yield break;
                }
            }

            if (!definition.SpawnsCreature)
            {
                entry.Spent = true;
                yield break;
            }

            SpawnEntity(definition, entry.Target, side);
        }

        private IEnumerator LostGraceRoutine(TargetEntry entry)
        {
            if (lostGraceSeconds > 0f) yield return new WaitForSeconds(lostGraceSeconds);

            entry.PendingRoutine = null;
            _entries.Remove(entry.Target.InstanceId);

            LivingEntityController entity = entry.Entity;
            if (entity != null) _entryByEntity.Remove(entity);

            GameSystem? system = entry.Definition != null ? entry.Definition.System : (GameSystem?)null;
            if (system.HasValue)
            {
                foreach (IGameRuleModule module in SnapshotModules())
                {
                    if (module.System == system.Value && module.OnTargetLost(entry.Target, entity))
                    {
                        Log($"'{entry.Target.ReferenceName}' lost; entity claimed by {module.GetType().Name}.");
                        yield break;
                    }
                }
            }

            if (entity != null)
            {
                entity.StopFollowing();
                entity.Despawn();
                Log($"'{entry.Target.ReferenceName}' lost; despawning.");
            }
        }

        private void OnEntityDied(LivingEntityController entity)
        {
            // KO'd while its card is still on the table: don't resurrect on re-detection.
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
            if (_entryByEntity.TryGetValue(entity, out TargetEntry entry))
            {
                entry.Entity = null;
                _entryByEntity.Remove(entity);
            }

            EntityRemoved?.Invoke(entity);
        }

        private static IGameRuleModule[] SnapshotModules() => Modules.ToArray();

        private void Log(string message)
        {
            if (verboseLogging) Debug.Log($"[HoloTable] {message}", this);
        }
    }
}
