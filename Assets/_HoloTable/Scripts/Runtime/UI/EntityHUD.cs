using HoloTable.Entities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HoloTable.UI
{
    /// <summary>
    /// World-space HUD floating over a hologram: name, HP bar with a delayed "ghost" bar,
    /// ATK/DEF line, energy/mana, status tag (ATACANTE, TAPPED…) and a 2D card preview.
    /// Billboards to the camera and keeps a readable size at any distance.
    /// Put it on a World Space Canvas prefab (scale ≈ 0.001) and assign it to the spawn director.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public sealed class EntityHUD : MonoBehaviour
    {
        [Header("Widgets")]
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text statsText;
        [SerializeField] private TMP_Text hpText;
        [SerializeField] private TMP_Text resourceText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private Image hpFill;
        [SerializeField] private Image hpGhostFill;
        [SerializeField] private Image cardPreview;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Look")]
        [SerializeField] private Gradient hpGradient = DefaultGradient();
        [SerializeField, Min(0f)] private float verticalPadding = 0.025f;
        [SerializeField, Min(0.0001f)] private float baseScale = 0.0008f;
        [Tooltip("Distance at which the HUD has exactly baseScale; closer/farther scales linearly.")]
        [SerializeField, Min(0.05f)] private float referenceDistance = 0.6f;
        [SerializeField] private Vector2 scaleClamp = new Vector2(0.6f, 2.2f);
        [SerializeField, Min(0f)] private float ghostDelay = 0.35f;
        [SerializeField, Min(0.01f)] private float ghostSpeed = 0.8f;

        private LivingEntityController _entity;
        private float _fill = 1f;
        private float _ghostFill = 1f;
        private float _ghostHoldUntil;
        private float _alpha;

        public void Bind(LivingEntityController entity)
        {
            Unbind();
            _entity = entity;
            _entity.StatsChanged += OnStatsChanged;

            if (nameText != null) nameText.text = entity.Definition.DisplayName;
            if (cardPreview != null)
            {
                cardPreview.sprite = entity.Definition.CardArt;
                cardPreview.enabled = entity.Definition.CardArt != null;
            }

            _fill = _ghostFill = entity.Vitals.Fraction;
            _alpha = 0f;
            OnStatsChanged(entity);
            LateUpdate();
        }

        private void OnDestroy() => Unbind();

        private void Unbind()
        {
            if (_entity != null) _entity.StatsChanged -= OnStatsChanged;
            _entity = null;
        }

        private void OnStatsChanged(LivingEntityController e)
        {
            float fraction = e.Vitals.Fraction;
            if (fraction < _fill) _ghostHoldUntil = Time.time + ghostDelay;
            else _ghostFill = fraction;
            _fill = fraction;

            if (hpText != null) hpText.text = $"{e.Vitals.Current}/{e.Vitals.Max}";
            if (statsText != null) statsText.text = e.Definition.BuildStatLine();
            if (resourceText != null)
            {
                resourceText.text = string.IsNullOrEmpty(e.ResourceLabel) ? "" : $"{e.ResourceLabel} {e.Resource}";
            }

            if (statusText != null)
            {
                statusText.text = e.StatusTag;
                statusText.color = e.StatusColor;
                statusText.enabled = !string.IsNullOrEmpty(e.StatusTag);
            }
        }

        private void LateUpdate()
        {
            if (_entity == null) return;

            float dt = Time.deltaTime;
            if (Time.time >= _ghostHoldUntil) _ghostFill = Mathf.MoveTowards(_ghostFill, _fill, ghostSpeed * dt);

            if (hpFill != null)
            {
                hpFill.fillAmount = _fill;
                hpFill.color = hpGradient.Evaluate(_fill);
            }

            if (hpGhostFill != null) hpGhostFill.fillAmount = _ghostFill;

            bool visible = _entity.State != EntityState.Dormant && _entity.State != EntityState.Dying && _entity.State != EntityState.Dead;
            _alpha = Mathf.MoveTowards(_alpha, visible ? 1f : 0f, dt * 3f);
            if (canvasGroup != null) canvasGroup.alpha = _alpha;

            transform.position = _entity.TopWorld + _entity.Up * verticalPadding;

            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 toHud = transform.position - cam.transform.position;
            float distance = toHud.magnitude;
            if (distance > 1e-4f) transform.rotation = Quaternion.LookRotation(toHud / distance, cam.transform.up);

            float k = Mathf.Clamp(distance / referenceDistance, scaleClamp.x, scaleClamp.y);
            transform.localScale = Vector3.one * (baseScale * k);
        }

        private static Gradient DefaultGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.2f, 0.2f), 0f),
                    new GradientColorKey(new Color(1f, 0.85f, 0.2f), 0.4f),
                    new GradientColorKey(new Color(0.3f, 1f, 0.5f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }
}
