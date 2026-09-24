using System;
using System.Collections;
using System.Collections.Generic;
using HoloTable.Core;
using HoloTable.Data;
using HoloTable.Domain;
using HoloTable.Tracking;
using HoloTable.UI;
using HoloTable.VFX;
using UnityEngine;

namespace HoloTable.Entities
{
    /// <summary>
    /// The "life" of a hologram summoned from a physical card or miniature.
    ///
    /// Owns: the animation state machine (Spawn → Idle → Attack / TakeDamage → Die),
    /// procedural fallbacks for models without clips (scale-in, breathing, lunge, shake,
    /// dissolve), head/body look-at towards the player or the nearest rival, dynamic
    /// scale (small creatures fit their card, dragons tower over the board), smoothed
    /// anchor following, hover height (MTG fliers) and the world-space HUD binding.
    ///
    /// Prefab layout:
    ///   Root (this component)
    ///   └─ Visual           ← procedural offsets / scale (auto-created if missing)
    ///      └─ Model         ← Animator, SkinnedMeshRenderer, bones (Head, Muzzle…)
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LivingEntityController : MonoBehaviour
    {
        private const string VisualRootName = "Visual";

        [Header("Rig")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Animator animator;
        [Tooltip("Optional head/neck bone for procedural look-at on generic (non-humanoid) rigs.")]
        [SerializeField] private Transform headBone;
        [Tooltip("Projectile / laser origin. Falls back to the chest.")]
        [SerializeField] private Transform muzzle;
        [SerializeField] private AudioSource audioSource;

        [Header("Animator")]
        [SerializeField] private AnimationDriveMode driveMode = AnimationDriveMode.Triggers;
        [SerializeField] private string spawnParam = "Spawn";
        [SerializeField] private string idleState = "Idle";
        [SerializeField] private string attackParam = "Attack";
        [SerializeField] private string hitParam = "TakeDamage";
        [SerializeField] private string dieParam = "Die";
        [SerializeField] private string flyingBoolParam = "IsFlying";
        [SerializeField, Range(0f, 0.5f)] private float crossFadeSeconds = 0.15f;
        [Tooltip("Wait for the AnimEvent_AttackImpact animation event instead of the fallback delay.")]
        [SerializeField] private bool useAttackImpactEvent;

        [Header("Timings")]
        [SerializeField, Min(0.1f)] private float spawnDuration = 1.1f;
        [SerializeField] private AnimationCurve spawnScaleCurve = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.7f, 1.12f), new Keyframe(1f, 1f));
        [SerializeField, Min(0.1f)] private float attackDuration = 0.8f;
        [SerializeField, Min(0f)] private float attackImpactDelay = 0.35f;
        [SerializeField, Min(0.05f)] private float hitReactionDuration = 0.4f;
        [SerializeField, Min(0f)] private float deathAnimationLead = 0.35f;
        [SerializeField, Min(0.1f)] private float dissolveDuration = 1.2f;

        [Header("Procedural motion")]
        [SerializeField, Range(0f, 0.08f)] private float breathAmplitude = 0.015f;
        [SerializeField, Min(0.01f)] private float breathFrequency = 0.3f;
        [SerializeField, Range(0f, 0.05f)] private float hoverBobAmplitude = 0.012f;
        [Tooltip("Lunge distance as a fraction of the creature's height.")]
        [SerializeField, Range(0f, 1.5f)] private float lungeDistance = 0.45f;
        [SerializeField, Range(0f, 0.05f)] private float hitShakeAmplitude = 0.008f;
        [SerializeField] private Color hitFlashColor = new Color(1f, 0.25f, 0.2f, 1f);

        [Header("Look At")]
        [SerializeField] private LookMode lookMode = LookMode.NearestRivalThenPlayer;
        [SerializeField, Min(0f)] private float rivalAwarenessRadius = 0.6f;
        [SerializeField, Min(0.05f)] private float retargetInterval = 0.2f;
        [SerializeField, Min(0.1f)] private float headTurnSharpness = 6f;
        [SerializeField, Range(0f, 90f)] private float maxHeadYaw = 70f;
        [SerializeField, Range(0f, 60f)] private float maxHeadPitch = 35f;
        [SerializeField] private bool rotateBodyTowardsTarget = true;
        [SerializeField, Range(0f, 180f)] private float maxBodyYaw = 120f;
        [SerializeField, Min(1f)] private float bodyTurnSpeed = 140f;
        [SerializeField] private bool useHumanoidIK = true;

        [Header("Scale")]
        [SerializeField] private bool autoScale = true;
        [SerializeField, Min(0.1f)] private float scaleMultiplier = 1f;

        [Header("Anchor follow")]
        [SerializeField, Min(1f)] private float positionSharpness = 18f;
        [SerializeField, Min(1f)] private float rotationSharpness = 12f;

        [Header("VFX / SFX")]
        [SerializeField] private string dissolveProperty = "_DissolveAmount";
        [SerializeField] private ParticleSystem spawnVfx;
        [SerializeField] private ParticleSystem deathVfx;
        [SerializeField] private AudioClip spawnRoar;
        [SerializeField] private AudioClip attackSfx;
        [SerializeField] private AudioClip hitSfx;
        [SerializeField] private AudioClip deathSfx;

        private static int _nextEntityId = 1;

        private readonly HashSet<int> _animatorParams = new HashSet<int>();
        private HologramMaterialDriver _materials;
        private AnimationEventRelay _relay;
        private EntityHUD _hud;
        private TrackedTarget _follow;

        private float _baseScale = 1f;
        private float _groundOffsetUnit;
        private float _nativeHeight = 0.1f;
        private float _phase;

        private float _spawnScale;
        private float _spawnRise;
        private float _dissolveSpawn = 1f;
        private float _dissolveDie;
        private float _ghost;
        private float _ghostTarget;
        private float _hoverCurrent;
        private float _hoverTarget;
        private float _hoverSpeed = 0.3f;
        private float _bodyYaw;
        private Vector3 _lungeOffset;
        private Vector3 _shakeOffset;
        private float _flash;

        private Transform _lookOverride;
        private Vector3? _lookPointOverride;
        private LivingEntityController _rivalTarget;
        private float _nextRetargetTime;
        private bool _hasLookPoint;
        private Vector3 _lookPoint;
        private Vector3 _smoothedLookDir = Vector3.forward;
        private float _lookWeight;
        private Quaternion _headOffset = Quaternion.identity;

        private Action _pendingImpact;
        private Coroutine _actionRoutine;

        public event Action<LivingEntityController, EntityState, EntityState> StateChanged;
        public event Action<LivingEntityController, int, Vector3> Damaged;
        public event Action<LivingEntityController, int> Healed;
        public event Action<LivingEntityController> StatsChanged;
        public event Action<LivingEntityController> Died;
        public event Action<LivingEntityController> Despawned;
        public event Action<LivingEntityController> Roared;

        public int EntityId { get; private set; }
        public EntityDefinition Definition { get; private set; }
        public PlayerSide Side { get; private set; }
        public TrackedTarget Target => _follow;
        public EntityState State { get; private set; } = EntityState.Dormant;
        public Vitals Vitals { get; private set; } = Vitals.Full(1);
        public int AttackValue { get; private set; }
        public int DefenseValue { get; private set; }
        public int Resource { get; private set; }
        public string ResourceLabel { get; private set; } = "";
        public string StatusTag { get; private set; } = "";
        public Color StatusColor { get; private set; } = Color.white;
        public ScaleProfile Scale { get; private set; }
        public EntityHUD Hud => _hud;
        public Transform VisualRoot => visualRoot;
        public Animator Animator => animator;

        public bool IsAlive => State != EntityState.Dying && State != EntityState.Dead && !Vitals.IsDefeated;
        public bool CanAct => State == EntityState.Idle || State == EntityState.Hurt;

        /// <summary>Can be attacked / engaged / stared at. False while summoning, transforming or dying.</summary>
        public bool IsTargetable => IsAlive && State != EntityState.Dormant && State != EntityState.Transforming;
        public float HoverHeight => _hoverCurrent;
        public float WorldHeight => Scale != null ? Scale.WorldHeight : _nativeHeight;
        public float WorldFootprint => Scale != null ? Scale.WorldFootprint : 0.05f;
        public Vector3 Up => TableSpace.Current.Normal;
        public Vector3 GroundWorld => transform.position;
        public Vector3 CenterWorld => transform.position + Up * (_hoverCurrent + WorldHeight * 0.5f);
        public Vector3 TopWorld => transform.position + Up * (_hoverCurrent + WorldHeight);
        public Vector3 MuzzleWorld => muzzle != null
            ? muzzle.position
            : CenterWorld + visualRoot.forward * (WorldFootprint * 0.5f);

        // ─────────────────────────────── Lifecycle ───────────────────────────────

        private void Awake()
        {
            EntityId = _nextEntityId++;
            _phase = UnityEngine.Random.value * 10f;
            _nextRetargetTime = UnityEngine.Random.value * retargetInterval; // spread retargets across frames
            EnsureVisualRoot();

            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                foreach (AnimatorControllerParameter p in animator.parameters) _animatorParams.Add(p.nameHash);
                _relay = animator.GetComponent<AnimationEventRelay>();
                if (_relay == null) _relay = animator.gameObject.AddComponent<AnimationEventRelay>();
                _relay.Bind(this);
            }

            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 1f;
                audioSource.minDistance = 0.3f;
            }

            _materials = new HologramMaterialDriver(visualRoot, dissolveProperty);
        }

        private void OnDestroy()
        {
            EntityRegistry.Unregister(this);
            if (_hud != null) Destroy(_hud.gameObject);
        }

        /// <summary>Binds data, measures the model and snaps to the physical anchor. Hidden until <see cref="Spawn"/>.</summary>
        public void Initialize(EntityDefinition definition, TrackedTarget target, PlayerSide side)
        {
            Definition = definition ? definition : throw new ArgumentNullException(nameof(definition));
            Side = side;
            Vitals = Vitals.Full(definition.BaseMaxHp);
            AttackValue = definition.BaseAttack;
            DefenseValue = definition.BaseDefense;
            _materials.SetTint(definition.HologramTint);

            float trackedShortSide = target != null ? target.ShortSide : TrackedTarget.DefaultCardShortSide;
            MeasureAndScale(definition.ResolveFootprintMeters(trackedShortSide));
            CacheHeadOffset();

            Follow(target);
            SnapToAnchor();
            EntityRegistry.Register(this);

            _spawnScale = 0f;
            _dissolveSpawn = 1f;
            _materials.SetVisible(false);
            StatsChanged?.Invoke(this);
        }

        public void AttachHud(EntityHUD hud)
        {
            if (_hud != null) Destroy(_hud.gameObject);
            _hud = hud;
            if (_hud != null) _hud.Bind(this);
        }

        public void Follow(TrackedTarget target) => _follow = target;

        public void StopFollowing() => _follow = null;

        public void SnapToAnchor()
        {
            if (_follow == null || !_follow.HasAnchor) return;
            transform.SetPositionAndRotation(_follow.Anchor.position, AnchorYawRotation(_follow.Anchor));
        }

        // ─────────────────────────────── Actions ───────────────────────────────

        /// <summary>Invocation: roar, particles, scale-in and dissolve-in, then Idle.</summary>
        public void Spawn(bool withEffects = true)
        {
            if (State != EntityState.Dormant) return;
            RunAction(SpawnRoutine(withEffects));
        }

        /// <summary>
        /// Plays the attack (clip or procedural lunge) towards <paramref name="targetPoint"/>.
        /// <paramref name="onImpact"/> fires on the impact frame (animation event or delay).
        /// </summary>
        public void PlayAttack(Vector3 targetPoint, Action onImpact)
        {
            if (!CanAct)
            {
                onImpact?.Invoke();
                return;
            }

            RunAction(AttackRoutine(targetPoint, onImpact));
        }

        public void ApplyDamage(int amount, Vector3 hitPoint, string label = null)
        {
            if (!IsAlive || amount < 0) return;

            Vitals = Vitals.WithDamage(amount);
            Damaged?.Invoke(this, amount, hitPoint);
            StatsChanged?.Invoke(this);
            DamagePopupService.ShowDamage(hitPoint, amount, label);
            PlayClip(hitSfx);

            if (Vitals.IsDefeated)
            {
                Died?.Invoke(this);
                Despawn();
                return;
            }

            StartCoroutine(HitReactionRoutine());
        }

        public void Heal(int amount)
        {
            if (!IsAlive || amount <= 0) return;
            Vitals = Vitals.WithHealing(amount);
            Healed?.Invoke(this, amount);
            StatsChanged?.Invoke(this);
            DamagePopupService.ShowHeal(TopWorld, amount);
        }

        /// <summary>Plays death / removal: Die clip, particles, dissolve, then destroys the GameObject.</summary>
        public void Despawn(bool animated = true)
        {
            if (State == EntityState.Dying || State == EntityState.Dead) return;

            if (!animated)
            {
                SetState(EntityState.Dead);
                Despawned?.Invoke(this);
                Destroy(gameObject);
                return;
            }

            RunAction(DieRoutine());
        }

        // ─────────────────────────────── Mutators used by game modules ───────────────────────────────

        public void ReplaceVitals(Vitals vitals)
        {
            Vitals = vitals ?? throw new ArgumentNullException(nameof(vitals));
            StatsChanged?.Invoke(this);
        }

        public void SetCombatStats(int attack, int defense)
        {
            AttackValue = attack;
            DefenseValue = defense;
            StatsChanged?.Invoke(this);
        }

        public void SetResource(int value, string label)
        {
            Resource = value;
            ResourceLabel = label ?? "";
            StatsChanged?.Invoke(this);
        }

        public void SetStatusTag(string tag, Color color)
        {
            StatusTag = tag ?? "";
            StatusColor = color;
            StatsChanged?.Invoke(this);
        }

        public void SetStatusTag(string tag) => SetStatusTag(tag, Color.white);

        /// <summary>Lifts the creature off the board (MTG fliers). Speed in m/s.</summary>
        public void SetHoverHeight(float meters, float speed = 0.3f)
        {
            _hoverTarget = Mathf.Max(0f, meters);
            _hoverSpeed = Mathf.Max(0.01f, speed);
            SetAnimatorBool(flyingBoolParam, _hoverTarget > 0.01f);
        }

        public void SetMuzzle(Transform newMuzzle) => muzzle = newMuzzle;

        /// <summary>Assigns rig bones for code-built holograms. Call before <see cref="Initialize"/>.</summary>
        public void ConfigureRig(Transform head, Transform muzzleTransform)
        {
            headBone = head;
            muzzle = muzzleTransform;
        }

        public void SetLookOverride(Transform target)
        {
            _lookOverride = target;
            _lookPointOverride = null;
        }

        public void SetLookOverride(Vector3 worldPoint)
        {
            _lookOverride = null;
            _lookPointOverride = worldPoint;
        }

        public void ClearLookOverride()
        {
            _lookOverride = null;
            _lookPointOverride = null;
        }

        /// <summary>Semi-transparent "limbo" look while its card is out of view.</summary>
        public void SetGhosted(bool ghosted) => _ghostTarget = ghosted ? 0.55f : 0f;

        public void SetVisible(bool visible) => _materials.SetVisible(visible);

        public void SetFlash(float amount, Color color)
        {
            _flash = amount;
            _materials.SetFlash(amount, color);
        }

        /// <summary>Locks the entity for an external sequence (evolution, transformation…).</summary>
        public bool BeginTransformation()
        {
            if (!IsAlive) return false;
            StopAction();
            SetState(EntityState.Transforming);
            return true;
        }

        public void EndTransformation()
        {
            if (State == EntityState.Transforming) SetState(EntityState.Idle);
        }

        /// <summary>Instantly shows a fully materialised entity (used after evolution sequences).</summary>
        public void ForceMaterialized()
        {
            if (State == EntityState.Dying || State == EntityState.Dead) return;

            StopAction();
            _spawnScale = 1f;
            _spawnRise = 0f;
            _dissolveSpawn = 0f;
            _materials.SetVisible(true);
            SetState(EntityState.Idle);
            CrossFadeOrTrigger(idleState, null);
        }

        // ─────────────────────────────── Animation relay callbacks ───────────────────────────────

        internal void NotifyAttackImpact()
        {
            Action impact = _pendingImpact;
            _pendingImpact = null;
            impact?.Invoke();
        }

        internal void NotifyRoar() => Roared?.Invoke(this);

        internal void HandleAnimatorIK(int layer)
        {
            if (!useHumanoidIK || animator == null || !animator.isHuman) return;
            animator.SetLookAtWeight(_lookWeight, 0.15f, 1f, 0.6f, 0.5f);
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            Vector3 origin = head != null ? head.position : CenterWorld;
            animator.SetLookAtPosition(origin + _smoothedLookDir);
        }

        // ─────────────────────────────── Frame update ───────────────────────────────

        private void Update()
        {
            FollowAnchor(Time.deltaTime);
            if (Time.time >= _nextRetargetTime)
            {
                _nextRetargetTime = Time.time + retargetInterval;
                _rivalTarget = lookMode == LookMode.PlayerOnly || lookMode == LookMode.None
                    ? null
                    : EntityRegistry.FindNearestRival(this, rivalAwarenessRadius);
            }
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            _hoverCurrent = Mathf.MoveTowards(_hoverCurrent, _hoverTarget, _hoverSpeed * dt);
            _ghost = Mathf.MoveTowards(_ghost, _ghostTarget, dt * 2f);

            _hasLookPoint = TryGetLookPoint(out _lookPoint); // once per frame, shared by body and head
            ComposeVisual(dt);
            UpdateLook(dt);

            _materials.SetDissolve(Mathf.Max(_dissolveSpawn, Mathf.Max(_dissolveDie, _ghost)));
            _materials.Apply();
        }

        private void FollowAnchor(float dt)
        {
            if (_follow == null || !_follow.HasAnchor || !_follow.IsTracked) return;

            Transform anchor = _follow.Anchor;
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, anchor.position, HoloMath.Damp(positionSharpness, dt)),
                Quaternion.Slerp(transform.rotation, AnchorYawRotation(anchor), HoloMath.Damp(rotationSharpness, dt)));
        }

        private void ComposeVisual(float dt)
        {
            float t = Time.time + _phase;
            bool alive = State != EntityState.Dead;

            float breath = alive ? 1f + Mathf.Sin(t * Mathf.PI * 2f * breathFrequency) * breathAmplitude : 1f;
            float scale = _baseScale * _spawnScale * breath;
            float bob = _hoverCurrent > 0.01f ? Mathf.Sin(t * 1.7f) * hoverBobAmplitude : 0f;

            if (rotateBodyTowardsTarget && CanAct && _hasLookPoint)
            {
                Vector3 flat = Vector3.ProjectOnPlane(_lookPoint - transform.position, transform.up);
                if (flat.sqrMagnitude > 1e-5f)
                {
                    float desired = Mathf.Clamp(Vector3.SignedAngle(transform.forward, flat, transform.up), -maxBodyYaw, maxBodyYaw);
                    _bodyYaw = Mathf.MoveTowardsAngle(_bodyYaw, desired, bodyTurnSpeed * dt);
                }
            }

            float height = _groundOffsetUnit * scale + _hoverCurrent + bob + _spawnRise;
            visualRoot.localPosition = Vector3.up * height + _lungeOffset + _shakeOffset;
            visualRoot.localRotation = Quaternion.Euler(0f, _bodyYaw, 0f);
            visualRoot.localScale = Vector3.one * Mathf.Max(0.0001f, scale);
        }

        private void UpdateLook(float dt)
        {
            bool hasTarget = _hasLookPoint && State != EntityState.Dying && State != EntityState.Dead;
            Vector3 lookPoint = _lookPoint;
            _lookWeight = Mathf.MoveTowards(_lookWeight, hasTarget ? 1f : 0f, dt * 2.5f);
            if (!hasTarget && _lookWeight <= 0f) return;

            Vector3 origin = headBone != null ? headBone.position : CenterWorld;
            Vector3 up = visualRoot.up;
            Vector3 bodyForward = visualRoot.forward;
            Vector3 desired = hasTarget ? (lookPoint - origin).normalized : bodyForward;

            Vector3 flat = Vector3.ProjectOnPlane(desired, up);
            float yaw = flat.sqrMagnitude > 1e-6f
                ? Mathf.Clamp(Vector3.SignedAngle(Vector3.ProjectOnPlane(bodyForward, up), flat, up), -maxHeadYaw, maxHeadYaw)
                : 0f;
            float pitch = Mathf.Clamp(90f - Vector3.Angle(up, desired), -maxHeadPitch, maxHeadPitch);

            Vector3 clamped = Quaternion.AngleAxis(yaw, up) * (Quaternion.AngleAxis(-pitch, visualRoot.right) * bodyForward);
            _smoothedLookDir = Vector3.Slerp(_smoothedLookDir, clamped, HoloMath.Damp(headTurnSharpness, dt));

            bool humanoidIK = useHumanoidIK && animator != null && animator.isHuman;
            if (headBone != null && !humanoidIK && _smoothedLookDir.sqrMagnitude > 1e-6f)
            {
                Quaternion look = Quaternion.LookRotation(_smoothedLookDir, up) * _headOffset;
                headBone.rotation = Quaternion.Slerp(headBone.rotation, look, _lookWeight);
            }
        }

        private bool TryGetLookPoint(out Vector3 point)
        {
            if (_lookPointOverride.HasValue)
            {
                point = _lookPointOverride.Value;
                return true;
            }

            if (_lookOverride != null)
            {
                point = _lookOverride.position;
                return true;
            }

            if (lookMode != LookMode.PlayerOnly && lookMode != LookMode.None && _rivalTarget != null && _rivalTarget.IsTargetable)
            {
                point = _rivalTarget.CenterWorld + _rivalTarget.Up * (_rivalTarget.WorldHeight * 0.3f);
                return true;
            }

            Camera cam = Camera.main;
            if ((lookMode == LookMode.NearestRivalThenPlayer || lookMode == LookMode.PlayerOnly) && cam != null)
            {
                point = cam.transform.position;
                return true;
            }

            point = default;
            return false;
        }

        // ─────────────────────────────── Routines ───────────────────────────────

        private IEnumerator SpawnRoutine(bool withEffects)
        {
            SetState(EntityState.Spawning);
            _materials.SetVisible(true);
            _dissolveSpawn = 1f;

            if (withEffects)
            {
                PlayVfx(spawnVfx, GroundWorld);
                PlayClip(spawnRoar);
            }

            CrossFadeOrTrigger(spawnParam, spawnParam);

            float riseDepth = WorldHeight * 0.35f;
            for (float t = 0f; t < spawnDuration; t += Time.deltaTime)
            {
                float k = t / spawnDuration;
                _spawnScale = spawnScaleCurve.Evaluate(k);
                _dissolveSpawn = 1f - Mathf.SmoothStep(0f, 1f, k * 1.3f);
                _spawnRise = -riseDepth * (1f - Mathf.SmoothStep(0f, 1f, k));
                yield return null;
            }

            _spawnScale = 1f;
            _dissolveSpawn = 0f;
            _spawnRise = 0f;
            SetState(EntityState.Idle);
            _actionRoutine = null;
        }

        private IEnumerator AttackRoutine(Vector3 targetPoint, Action onImpact)
        {
            SetState(EntityState.Attacking);
            SetLookOverride(targetPoint);
            _pendingImpact = onImpact;
            CrossFadeOrTrigger(attackParam, attackParam);
            PlayClip(attackSfx);

            Vector3 flat = Vector3.ProjectOnPlane(targetPoint - transform.position, transform.up);
            Vector3 localDir = flat.sqrMagnitude > 1e-6f ? transform.InverseTransformDirection(flat.normalized) : Vector3.forward;
            float maxLunge = Mathf.Min(WorldHeight * lungeDistance, flat.magnitude * 0.5f);

            for (float t = 0f; t < attackDuration; t += Time.deltaTime)
            {
                float k = t / attackDuration;
                // Anticipation (pull back) → strike → recover.
                float curve = k < 0.25f ? -0.25f * Mathf.Sin(k / 0.25f * Mathf.PI * 0.5f)
                    : k < 0.45f ? Mathf.Lerp(-0.25f, 1f, (k - 0.25f) / 0.2f)
                    : 1f - Mathf.SmoothStep(0f, 1f, (k - 0.45f) / 0.55f);
                _lungeOffset = localDir * (maxLunge * curve);

                if (_pendingImpact != null && !useAttackImpactEvent && t >= attackImpactDelay)
                {
                    NotifyAttackImpact();
                }

                yield return null;
            }

            NotifyAttackImpact(); // guarantees exactly one impact even if the clip lacks the event
            _lungeOffset = Vector3.zero;
            ClearLookOverride();
            if (State == EntityState.Attacking) SetState(EntityState.Idle);
            _actionRoutine = null;
        }

        private IEnumerator HitReactionRoutine()
        {
            bool reacting = CanAct;
            if (reacting)
            {
                SetState(EntityState.Hurt);
                CrossFadeOrTrigger(hitParam, hitParam);
            }

            for (float t = 0f; t < hitReactionDuration; t += Time.deltaTime)
            {
                float k = 1f - t / hitReactionDuration;
                _shakeOffset = UnityEngine.Random.insideUnitSphere * (hitShakeAmplitude * k);
                SetFlash(k, hitFlashColor);
                yield return null;
            }

            _shakeOffset = Vector3.zero;
            SetFlash(0f, hitFlashColor);
            if (reacting && State == EntityState.Hurt) SetState(EntityState.Idle);
        }

        private IEnumerator DieRoutine()
        {
            SetState(EntityState.Dying);
            _pendingImpact = null;
            _lungeOffset = Vector3.zero;
            CrossFadeOrTrigger(dieParam, dieParam);
            PlayClip(deathSfx);

            yield return new WaitForSeconds(deathAnimationLead);
            PlayVfx(deathVfx, CenterWorld);

            float startScale = _spawnScale;
            for (float t = 0f; t < dissolveDuration; t += Time.deltaTime)
            {
                float k = t / dissolveDuration;
                _dissolveDie = k;
                if (!_materials.SupportsDissolve) _spawnScale = Mathf.Lerp(startScale, 0f, k * k);
                _spawnRise = -WorldHeight * 0.2f * k;
                yield return null;
            }

            SetState(EntityState.Dead);
            Despawned?.Invoke(this);
            Destroy(gameObject);
        }

        // ─────────────────────────────── Helpers ───────────────────────────────

        private void RunAction(IEnumerator routine)
        {
            StopAction();
            _actionRoutine = StartCoroutine(routine);
        }

        private void StopAction()
        {
            if (_actionRoutine != null) StopCoroutine(_actionRoutine);
            _actionRoutine = null;
            _lungeOffset = Vector3.zero;
            NotifyAttackImpact(); // never leave a combat sequence waiting forever
        }

        private void SetState(EntityState next)
        {
            if (State == next) return;
            EntityState previous = State;
            State = next;
            StateChanged?.Invoke(this, previous, next);
        }

        private Quaternion AnchorYawRotation(Transform anchor)
        {
            Vector3 normal = TableSpace.Current.Normal;
            Vector3 forward = Vector3.ProjectOnPlane(anchor.forward, normal);
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.ProjectOnPlane(anchor.up, normal);
            return Quaternion.LookRotation(forward.normalized, normal);
        }

        private void EnsureVisualRoot()
        {
            if (visualRoot != null) return;

            Transform existing = transform.Find(VisualRootName);
            if (existing != null)
            {
                visualRoot = existing;
                return;
            }

            visualRoot = new GameObject(VisualRootName).transform;
            var children = new List<Transform>();
            foreach (Transform child in transform) children.Add(child);
            visualRoot.SetParent(transform, false);
            foreach (Transform child in children) child.SetParent(visualRoot, true);
        }

        private void MeasureAndScale(float footprint)
        {
            Vector3 savedPos = transform.position;
            Quaternion savedRot = transform.rotation;
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            visualRoot.localPosition = Vector3.zero;
            visualRoot.localRotation = Quaternion.identity;
            visualRoot.localScale = Vector3.one;

            Bounds b = HoloMath.CalculateBounds(visualRoot, true);
            _nativeHeight = Mathf.Max(0.001f, b.size.y);
            float nativeFootprint = Mathf.Max(0.001f, Mathf.Max(b.size.x, b.size.z));
            _groundOffsetUnit = -b.min.y;

            Scale = ScaleRules.Compute(_nativeHeight, nativeFootprint, Mathf.Max(0.001f, footprint), Definition.SizeClass, scaleMultiplier);
            _baseScale = autoScale ? Scale.UniformScale : 1f;
            if (!autoScale)
            {
                Scale = new ScaleProfile(1f, _nativeHeight, nativeFootprint, nativeFootprint > footprint);
            }

            transform.SetPositionAndRotation(savedPos, savedRot);
        }

        private void CacheHeadOffset()
        {
            if (headBone == null) return;
            _headOffset = Quaternion.Inverse(Quaternion.LookRotation(visualRoot.forward, visualRoot.up)) * headBone.rotation;
        }

        private void CrossFadeOrTrigger(string stateName, string triggerName)
        {
            if (animator == null || !animator.isActiveAndEnabled) return;

            if (driveMode == AnimationDriveMode.CrossFadeStates || string.IsNullOrEmpty(triggerName))
            {
                int hash = Animator.StringToHash(stateName);
                if (animator.HasState(0, hash)) animator.CrossFadeInFixedTime(hash, crossFadeSeconds, 0);
                return;
            }

            int param = Animator.StringToHash(triggerName);
            if (_animatorParams.Contains(param)) animator.SetTrigger(param);
        }

        private void SetAnimatorBool(string name, bool value)
        {
            if (animator == null || string.IsNullOrEmpty(name)) return;
            int hash = Animator.StringToHash(name);
            if (_animatorParams.Contains(hash)) animator.SetBool(hash, value);
        }

        private void PlayClip(AudioClip clip)
        {
            if (clip != null && audioSource != null) audioSource.PlayOneShot(clip);
        }

        private void PlayVfx(ParticleSystem prefab, Vector3 position)
        {
            VfxPool.Play(prefab, position, Quaternion.LookRotation(transform.forward, Up), Mathf.Max(0.3f, WorldHeight / 0.1f));
        }
    }
}
