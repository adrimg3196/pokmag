using System;
using System.Collections;
using System.Collections.Generic;
using HoloTable.Combat;
using HoloTable.Core;
using HoloTable.Data;
using HoloTable.Domain;
using HoloTable.Domain.Warhammer;
using HoloTable.Entities;
using HoloTable.Tracking;
using HoloTable.UI;
using UnityEngine;

namespace HoloTable.Games.Warhammer
{
    /// <summary>
    /// Warhammer 40K (10th ed.) assistance layer for physical miniatures.
    ///
    ///  • Select a unit by looking at it (gaze dwell on Quest / screen centre on mobile) or
    ///    via <see cref="Select"/>: a holographic ring + cylinder shows its exact Move in
    ///    inches, measured from where the base started, and turns red if the physical
    ///    model is pushed beyond it. <see cref="Advance"/> rolls a D6 and grows the ring.
    ///  • Look at an enemy while a unit is selected to lock a target: a red laser from the
    ///    weapon muzzle checks line of sight against virtual terrain and the real room mesh
    ///    (Clear / Cover / Blocked) and range in inches.
    ///  • <see cref="Shoot"/> runs Hit → Wound → Save with physical AR dice thrown by hand
    ///    (or automatic), then the combat manager fires and removes Wounds from the model.
    /// All public commands are parameterless so they can be bound to XR buttons / UnityEvents.
    /// </summary>
    public sealed class GameLogicWarhammer : MonoBehaviour, IGameRuleModule
    {
        private sealed class UnitState
        {
            public WarhammerDatasheet Sheet;
            public int WeaponIndex;
            public Vector3 MoveOrigin;
            public MovementMode Mode = MovementMode.Normal;
            public int AdvanceRoll;
            public bool HasMoved;
        }

        private sealed class DiceResult
        {
            public IReadOnlyList<int> Values;
        }

        [Header("Selection")]
        [SerializeField] private bool gazeSelection = true;
        [SerializeField, Range(1f, 20f)] private float focusConeDegrees = 7f;
        [SerializeField, Min(0.1f)] private float dwellSeconds = 0.7f;

        [Header("Visuals")]
        [SerializeField] private MovementRangeVisualizer movementVisualizer;
        [SerializeField] private LineOfSightLaser laser;
        [SerializeField] private Color moveColor = new Color(0.3f, 0.9f, 1f);
        [SerializeField] private Color advanceColor = new Color(1f, 0.85f, 0.2f);
        [SerializeField] private Color exceededColor = new Color(1f, 0.2f, 0.2f);
        [SerializeField, Min(0.02f)] private float losInterval = 0.1f;

        [Header("Dice")]
        [SerializeField] private DiceTray diceTray;
        [SerializeField] private bool usePhysicalDice = true;
        [Tooltip("Throw automatically (no hand tracking / demo).")]
        [SerializeField] private bool autoThrowDice;
        [Tooltip("If nobody throws the dice within this time, they are thrown automatically.")]
        [SerializeField, Min(3f)] private float diceThrowTimeout = 20f;

        [Header("Shooting")]
        [SerializeField] private HoloProjectile shotProjectile;
        [SerializeField] private Color shotColor = new Color(1f, 0.6f, 0.2f);

        private readonly Dictionary<LivingEntityController, UnitState> _units = new Dictionary<LivingEntityController, UnitState>();
        private readonly IRandomSource _fallbackDice = new SystemRandomSource();
        private LivingEntityController _focus;
        private float _focusTime;
        private float _nextLos;
        private bool _busy;
        private int _lastMoveLabelKey = int.MinValue;
        private int _lastTargetTagKey = int.MinValue;

        public GameSystem System => GameSystem.Warhammer;
        public LivingEntityController Selected { get; private set; }
        public LivingEntityController Target { get; private set; }
        public Visibility TargetVisibility { get; private set; } = Visibility.Blocked;

        public event Action<LivingEntityController> UnitSelected;
        public event Action<LivingEntityController, LivingEntityController> TargetLocked;
        public event Action<LivingEntityController, LivingEntityController, AttackSummary> AttackResolved;

        private void Awake()
        {
            if (movementVisualizer == null)
            {
                movementVisualizer = new GameObject("[MovementRange]").AddComponent<MovementRangeVisualizer>();
            }

            if (laser == null)
            {
                laser = new GameObject("[LoSLaser]").AddComponent<LineOfSightLaser>();
            }
        }

        private void OnEnable() => HoloSpawnDirector.RegisterModule(this);

        private void OnDisable()
        {
            HoloSpawnDirector.UnregisterModule(this);
            StopAllCoroutines(); // disabling a component does not stop its coroutines by itself
            _busy = false;
        }

        // ─────────────────────────────── IGameRuleModule ───────────────────────────────

        public bool TryInterceptSpawn(SpawnContext context) => false;

        public void OnEntitySpawned(LivingEntityController entity, SpawnContext context)
        {
            if (!(entity.Definition is WarhammerDatasheet sheet)) return;

            _units[entity] = new UnitState { Sheet = sheet, MoveOrigin = PhysicalPosition(entity) };
            entity.Despawned += OnDespawned;

            Transform muzzle = FindDeep(entity.transform, sheet.MuzzleTransformName);
            if (muzzle != null) entity.SetMuzzle(muzzle);
            RefreshWeaponLabel(entity);
        }

        public bool OnTargetLost(TrackedTarget target, LivingEntityController entity) => false;

        // ─────────────────────────────── Commands ───────────────────────────────

        public void Select(LivingEntityController unit)
        {
            if (unit == null || !_units.TryGetValue(unit, out UnitState state)) return;
            if (_busy)
            {
                DamagePopupService.ShowInfo(unit.TopWorld, "Acción en curso…");
                return;
            }

            if (Selected != null && Selected != unit) Selected.SetStatusTag("");
            Selected = unit;
            ClearTarget();

            if (!state.HasMoved)
            {
                state.MoveOrigin = PhysicalPosition(unit);
                state.Mode = MovementMode.Normal;
                state.AdvanceRoll = 0;
            }

            unit.SetStatusTag("SELECCIONADA", moveColor);
            RefreshMovement(forceShow: true);
            UnitSelected?.Invoke(unit);
        }

        public void SetTarget(LivingEntityController enemy)
        {
            if (Selected == null || enemy == null || !Selected.Side.IsRivalOf(enemy.Side) || _busy) return;
            if (Target != null && Target != enemy) Target.SetStatusTag("");
            _lastTargetTagKey = int.MinValue;
            Target = enemy;
            _nextLos = 0f;
            TargetLocked?.Invoke(Selected, enemy);
        }

        public void Deselect()
        {
            if (_busy) return;
            if (Selected != null) Selected.SetStatusTag("");
            Selected = null;
            ClearTarget();
            movementVisualizer.Hide();
        }

        /// <summary>Locks the current position as the end of the Movement phase for this unit.</summary>
        public void ConfirmMove()
        {
            if (Selected == null || !_units.TryGetValue(Selected, out UnitState state)) return;
            state.HasMoved = true;
            movementVisualizer.Hide();
            DamagePopupService.ShowInfo(Selected.TopWorld, "Movimiento confirmado");
        }

        /// <summary>New battle round: every unit may move again.</summary>
        public void ResetMovement()
        {
            foreach (KeyValuePair<LivingEntityController, UnitState> pair in _units)
            {
                pair.Value.HasMoved = false;
                pair.Value.Mode = MovementMode.Normal;
                pair.Value.AdvanceRoll = 0;
                pair.Value.MoveOrigin = PhysicalPosition(pair.Key);
            }

            if (Selected != null) RefreshMovement(forceShow: true);
        }

        public void Advance()
        {
            if (Selected == null || _busy || !_units.TryGetValue(Selected, out UnitState state) || state.Mode == MovementMode.Advance) return;
            StartCoroutine(Guarded(AdvanceRoutine(Selected, state)));
        }

        public void CycleWeapon()
        {
            if (Selected == null || !_units.TryGetValue(Selected, out UnitState state) || state.Sheet.Weapons.Count == 0) return;
            state.WeaponIndex = (state.WeaponIndex + 1) % state.Sheet.Weapons.Count;
            RefreshWeaponLabel(Selected);
            DamagePopupService.ShowInfo(Selected.TopWorld, state.Sheet.Weapons[state.WeaponIndex].Profile.Name);
        }

        public void Shoot()
        {
            if (Selected == null) return;
            if (_busy)
            {
                DamagePopupService.ShowInfo(Selected.TopWorld, "Acción en curso…");
                return;
            }

            if (Target == null || !Target.IsTargetable)
            {
                DamagePopupService.ShowInfo(Selected.TopWorld, "Sin objetivo: mira a una unidad enemiga");
                return;
            }

            if (!_units.TryGetValue(Selected, out UnitState attacker) || !_units.TryGetValue(Target, out UnitState defender)) return;
            if (attacker.Sheet.Weapons.Count == 0)
            {
                Debug.LogWarning($"[HoloTable] Datasheet '{attacker.Sheet.name}' has no weapons.", attacker.Sheet);
                DamagePopupService.ShowInfo(Selected.TopWorld, "Sin armas en la hoja de datos");
                return;
            }

            WeaponData weapon = attacker.Sheet.Weapons[attacker.WeaponIndex];
            WeaponProfile profile = weapon.Profile;
            float distance = TableDistanceEdgeToEdge(Selected, Target);

            if (TargetVisibility == Visibility.Blocked && !profile.IsMelee)
            {
                DamagePopupService.ShowInfo(Selected.TopWorld, "Sin línea de visión");
                return;
            }

            if (!CoverRules.InRange(distance, profile))
            {
                DamagePopupService.ShowInfo(Selected.TopWorld, $"Fuera de alcance ({TableUnits.MetersToInches(distance):0.0}\" > {profile.RangeInches:0}\")");
                return;
            }

            // Benefit of Cover only applies against ranged attacks.
            bool inCover = TargetVisibility == Visibility.PartialCover && !profile.IsMelee;
            StartCoroutine(Guarded(AttackSequence(Selected, Target, weapon, defender.Sheet.Unit, inCover)));
        }

        // ─────────────────────────────── Frame loop ───────────────────────────────

        private void Update()
        {
            if (gazeSelection && !_busy) UpdateGaze();
            if (Selected == null) return;

            if (!Selected.IsAlive)
            {
                Deselect();
                return;
            }

            RefreshMovement(forceShow: false);

            if (Target != null)
            {
                if (!Target.IsAlive)
                {
                    ClearTarget();
                }
                else if (Time.time >= _nextLos)
                {
                    _nextLos = Time.time + losInterval;
                    TargetVisibility = laser.Trace(Selected, Target);
                    UpdateTargetTag();
                }
            }
        }

        private void UpdateGaze()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            LivingEntityController best = null;
            float bestAngle = focusConeDegrees;
            foreach (LivingEntityController unit in _units.Keys)
            {
                if (unit == null || !unit.IsAlive) continue;
                float angle = Vector3.Angle(cam.transform.forward, unit.CenterWorld - cam.transform.position);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    best = unit;
                }
            }

            if (best != _focus)
            {
                _focus = best;
                _focusTime = Time.time;
                return;
            }

            if (_focus == null || Time.time - _focusTime < dwellSeconds || _focus == Selected || _focus == Target) return;

            if (Selected != null && Selected.Side.IsRivalOf(_focus.Side)) SetTarget(_focus);
            else Select(_focus);
            _focusTime = float.MaxValue; // one action per dwell
        }

        private void RefreshMovement(bool forceShow)
        {
            if (!_units.TryGetValue(Selected, out UnitState state)) return;
            if (state.HasMoved)
            {
                if (movementVisualizer.IsVisible) movementVisualizer.Hide();
                return;
            }

            UnitProfile unit = state.Sheet.Unit;
            float allowed = MovementRules.MaxMoveInches(unit, state.Mode, state.Mode == MovementMode.Advance ? state.AdvanceRoll : 0);
            float travelled = TableSpace.Current.TableDistance(state.MoveOrigin, PhysicalPosition(Selected));
            float remaining = MovementRules.RemainingInches(allowed, travelled);
            bool exceeded = remaining < -0.05f;

            Color color = exceeded ? exceededColor : state.Mode == MovementMode.Advance ? advanceColor : moveColor;
            float radius = MovementRules.RingRadiusMeters(unit, state.Mode, state.Mode == MovementMode.Advance ? state.AdvanceRoll : 0);

            // Only format a new label when the displayed tenth of an inch changes (no per-frame garbage).
            int key = (Mathf.RoundToInt(remaining * 10f) * 31 + Mathf.RoundToInt(allowed)) * 2 + (exceeded ? 1 : 0);
            if (!forceShow && movementVisualizer.IsVisible && key == _lastMoveLabelKey) return;
            _lastMoveLabelKey = key;

            string text = exceeded
                ? $"¡Excedido {-remaining:0.0}\"!"
                : $"{remaining:0.0}\" / {allowed:0}\"";

            if (forceShow || !movementVisualizer.IsVisible) movementVisualizer.ShowAt(state.MoveOrigin, radius, color, text);
            else movementVisualizer.UpdateRange(radius, color, text);
        }

        private void UpdateTargetTag()
        {
            if (!_units.TryGetValue(Selected, out UnitState state) || state.Sheet.Weapons.Count == 0) return;

            WeaponProfile weapon = state.Sheet.Weapons[state.WeaponIndex].Profile;
            float inches = TableUnits.MetersToInches(TableDistanceEdgeToEdge(Selected, Target));
            bool inRange = inches <= weapon.RangeInches;
            int key = ((int)TargetVisibility * 2 + (inRange ? 1 : 0)) * 100000 + Mathf.RoundToInt(inches * 10f);
            if (key == _lastTargetTagKey) return;
            _lastTargetTagKey = key;
            string los = TargetVisibility == Visibility.Clear ? "Visible"
                : TargetVisibility == Visibility.PartialCover ? "Cobertura +1"
                : "Sin LoS";

            Color c = TargetVisibility == Visibility.Blocked || !inRange ? Color.gray
                : TargetVisibility == Visibility.PartialCover ? new Color(1f, 0.6f, 0.1f)
                : new Color(1f, 0.25f, 0.2f);

            Target.SetStatusTag($"{los} · {inches:0.0}\"{(inRange ? "" : " (fuera)")}", c);
        }

        // ─────────────────────────────── Sequences ───────────────────────────────

        private IEnumerator AdvanceRoutine(LivingEntityController unit, UnitState state)
        {
            var roll = new DiceResult();
            yield return RollDice(1, "Avance", null, roll);
            state.Mode = MovementMode.Advance;
            state.AdvanceRoll = roll.Values.Count > 0 ? Mathf.Clamp(roll.Values[0], 1, 6) : 1;
            if (unit != null) DamagePopupService.ShowInfo(unit.TopWorld, $"Avance +{state.AdvanceRoll}\"");
        }

        private IEnumerator AttackSequence(LivingEntityController shooter, LivingEntityController target, WeaponData weapon, UnitProfile targetProfile, bool inCover)
        {
            WeaponProfile w = weapon.Profile;

            var hitRolls = new DiceResult();
            yield return RollDice(w.Attacks, $"Impactar {w.Skill}+", r => WoundRules.RollSucceeds(r, w.Skill), hitRolls);
            DicePhaseResult hits = AttackResolver.ResolveHits(hitRolls.Values, w.Skill);
            DamagePopupService.ShowInfo(SafeTop(shooter), $"{hits.Successes} impactos");

            int woundTarget = WoundRules.RequiredWoundRoll(w.Strength, targetProfile.Toughness);
            var woundRolls = new DiceResult();
            if (hits.Successes > 0)
            {
                yield return RollDice(hits.Successes, $"Herir {woundTarget}+", r => WoundRules.RollSucceeds(r, woundTarget), woundRolls);
            }
            else
            {
                woundRolls.Values = Array.Empty<int>();
            }

            DicePhaseResult wounds = AttackResolver.ResolveWounds(woundRolls.Values, w.Strength, targetProfile.Toughness);

            int saveTarget = WoundRules.SaveTarget(targetProfile.Save, w.ArmourPenetration, targetProfile.InvulnerableSave, inCover);
            var saveRolls = new DiceResult();
            if (wounds.Successes > 0)
            {
                string prompt = saveTarget > 6 ? "Sin salvación posible" : $"Salvación {saveTarget}+ (defensor)";
                yield return RollDice(wounds.Successes, prompt, r => WoundRules.SaveSucceeds(r, saveTarget), saveRolls);
            }
            else
            {
                saveRolls.Values = Array.Empty<int>();
            }

            DicePhaseResult saves = AttackResolver.ResolveSaves(saveRolls.Values, saveTarget);
            int damage = AttackResolver.Damage(saves.Failures, w.Damage);
            var summary = new AttackSummary(hits, wounds, saves, saves.Failures, damage);

            DamagePopupService.ShowText(SafeTop(shooter) + TableSpace.Current.Normal * 0.03f,
                $"{hits.Successes} impactos → {wounds.Successes} heridas → {saves.Failures} fallos", Color.white, 0.7f);

            if (target != null && target.IsAlive)
            {
                ARCombatManager combat = ARCombatManager.Instance;
                if (damage > 0 && combat != null)
                {
                    bool done = false;
                    combat.RequestAttack(new AttackRequest
                    {
                        Attacker = shooter,
                        Defender = target,
                        Damage = damage,
                        Label = $"{saves.Failures}×{w.Damage}D",
                        ForceMelee = w.IsMelee,
                        ForceRanged = !w.IsMelee,
                        Projectile = shotProjectile,
                        Tint = shotColor,
                        OnResolved = _ => done = true,
                    });

                    float deadline = Time.time + 15f;
                    while (!done && Time.time < deadline) yield return null;
                    if (!done) Debug.LogWarning("[HoloTable] Warhammer attack was not resolved by ARCombatManager within 15s.", this);
                }
                else if (damage > 0)
                {
                    target.ApplyDamage(damage, target.CenterWorld);
                }
                else
                {
                    DamagePopupService.ShowInfo(target.TopWorld, "¡Todo salvado!");
                }
            }

            AttackResolved?.Invoke(shooter, target, summary);
        }

        private IEnumerator RollDice(int count, string prompt, Func<int, bool> isSuccess, DiceResult result)
        {
            if (count <= 0)
            {
                result.Values = Array.Empty<int>();
                yield break;
            }

            if (usePhysicalDice && diceTray != null
                && diceTray.RequestRoll(count, prompt, isSuccess, values => result.Values = values, autoThrowDice))
            {
                float deadline = Time.time + diceThrowTimeout;
                while (result.Values == null && Time.time < deadline) yield return null;

                if (result.Values == null && diceTray.IsAwaitingThrow)
                {
                    // Nobody threw (no hand tracking / swipe adapter?): throw for the player.
                    DamagePopupService.ShowInfo(diceTray.PickupPoint, "Lanzamiento automático");
                    diceTray.AutoThrow();
                }

                deadline = Time.time + 12f;
                while (result.Values == null && Time.time < deadline) yield return null;
                if (result.Values != null) yield break;

                Debug.LogWarning("[HoloTable] Physical dice never settled; using virtual dice. Check the DiceTray surface/colliders.", diceTray);
                diceTray.Cancel(); // free the tray so later rolls can use physical dice again
            }

            result.Values = _fallbackDice.RollD6(count);
            DamagePopupService.ShowInfo(SafeTop(Selected), $"{prompt}: {string.Join(" ", result.Values)}");
            yield return new WaitForSeconds(0.6f);
        }

        // ─────────────────────────────── Helpers ───────────────────────────────

        private void ClearTarget()
        {
            _lastTargetTagKey = int.MinValue;
            if (Target != null) Target.SetStatusTag("");
            Target = null;
            TargetVisibility = Visibility.Blocked;
            laser.Hide();
        }

        /// <summary>Runs a command sequence and always releases the busy lock, even on exceptions.</summary>
        private IEnumerator Guarded(IEnumerator body)
        {
            _busy = true;
            try
            {
                while (true)
                {
                    object current;
                    try
                    {
                        if (!body.MoveNext()) break;
                        current = body.Current;
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e, this);
                        break;
                    }

                    yield return current;
                }
            }
            finally
            {
                _busy = false;
            }
        }

        private static Vector3 SafeTop(LivingEntityController e) =>
            e != null ? e.TopWorld : TableSpace.Current.Origin + TableSpace.Current.Normal * 0.15f;

        private void RefreshWeaponLabel(LivingEntityController unit)
        {
            if (!_units.TryGetValue(unit, out UnitState state) || state.Sheet.Weapons.Count == 0) return;
            WeaponProfile w = state.Sheet.Weapons[state.WeaponIndex].Profile;
            string range = w.IsMelee ? "Melee" : $"{w.RangeInches:0}\"";
            unit.SetResource(w.Attacks, $"{w.Name} {range} S{w.Strength} AP-{w.ArmourPenetration} D{w.Damage} · A");
        }

        private static float TableDistanceEdgeToEdge(LivingEntityController a, LivingEntityController b)
        {
            float centre = TableSpace.Current.TableDistance(PhysicalPosition(a), PhysicalPosition(b));
            return Mathf.Max(0f, centre - a.WorldFootprint * 0.5f - b.WorldFootprint * 0.5f);
        }

        private static Vector3 PhysicalPosition(LivingEntityController e)
        {
            TrackedTarget t = e.Target;
            return t != null && t.HasAnchor ? t.Anchor.position : e.transform.position;
        }

        private static Transform FindDeep(Transform root, string childName)
        {
            if (string.IsNullOrEmpty(childName)) return null;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == childName) return t;
            }

            return null;
        }

        private void OnDespawned(LivingEntityController entity)
        {
            entity.Despawned -= OnDespawned;
            _units.Remove(entity);
            if (entity == Target) ClearTarget();
            if (entity == Selected)
            {
                Selected = null;
                movementVisualizer.Hide();
            }
        }
    }
}
