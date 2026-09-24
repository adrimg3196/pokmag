using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace HoloTable.VFX
{
    /// <summary>
    /// Darkens the *real* room for spell effects while holograms stay bright.
    ///
    /// Two layers: (1) a camera-locked translucent overlay drawn after the AR camera
    /// background but before holograms (render queue 1999, no depth write), and
    /// (2) dimming of virtual lights / ambient so shading matches. For Quest passthrough,
    /// wire <see cref="onDimLevelChanged"/> to your passthrough layer brightness.
    /// </summary>
    public sealed class EnvironmentDimmer : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [Tooltip("Optional. Transparent unlit material; a fallback is created if empty.")]
        [SerializeField] private Material overlayMaterial;
        [SerializeField, Min(0.05f)] private float overlayDistance = 0.2f;
        [SerializeField] private List<Light> lightsToDim = new List<Light>();
        [SerializeField, Range(0f, 1f)] private float lightFloor = 0.2f;
        [Tooltip("0 = normal room, 1 = fully dimmed. Hook to OVRPassthroughLayer brightness, post-processing, etc.")]
        [SerializeField] private UnityEvent<float> onDimLevelChanged = new UnityEvent<float>();

        private static EnvironmentDimmer _instance;

        private readonly Dictionary<Light, float> _originalIntensity = new Dictionary<Light, float>();
        private Transform _overlay;
        private Material _overlayRuntimeMaterial;
        private float _originalAmbient = 1f;
        private float _level;
        private Color _tint = Color.black;
        private float _flash;
        private Color _flashColor = Color.white;
        private Coroutine _pulse;

        /// <summary>Scene dimmer, auto-created on first use.</summary>
        public static EnvironmentDimmer Current
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<EnvironmentDimmer>();
                    if (_instance == null) _instance = new GameObject("[EnvironmentDimmer]").AddComponent<EnvironmentDimmer>();
                }

                return _instance;
            }
        }

        public float Level => _level;

        private void Awake()
        {
            if (_instance == null) _instance = this;
            _originalAmbient = RenderSettings.ambientIntensity;

            if (lightsToDim.Count == 0)
            {
                foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (l.type == LightType.Directional) lightsToDim.Add(l);
                }
            }

            foreach (Light l in lightsToDim)
            {
                if (l != null) _originalIntensity[l] = l.intensity;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            ApplyLevel(0f);
            if (_overlayRuntimeMaterial != null) Destroy(_overlayRuntimeMaterial);
        }

        /// <summary>Fade to <paramref name="amount"/> darkness, hold, fade back.</summary>
        public void Pulse(float amount, float fadeIn, float hold, float fadeOut, Color? tint = null, Action onPeak = null)
        {
            if (_pulse != null) StopCoroutine(_pulse);
            _tint = tint ?? Color.black;
            _pulse = StartCoroutine(PulseRoutine(Mathf.Clamp01(amount), fadeIn, hold, fadeOut, onPeak));
        }

        /// <summary>Very short bright flash (lightning strike, explosion).</summary>
        public void Flash(Color color, float intensity = 0.6f)
        {
            _flashColor = color;
            _flash = Mathf.Clamp01(intensity);
        }

        private IEnumerator PulseRoutine(float amount, float fadeIn, float hold, float fadeOut, Action onPeak)
        {
            float start = _level;
            for (float t = 0f; t < fadeIn; t += Time.deltaTime)
            {
                ApplyLevel(Mathf.Lerp(start, amount, Mathf.SmoothStep(0f, 1f, t / fadeIn)));
                yield return null;
            }

            ApplyLevel(amount);
            onPeak?.Invoke();
            yield return new WaitForSeconds(hold);

            for (float t = 0f; t < fadeOut; t += Time.deltaTime)
            {
                ApplyLevel(Mathf.Lerp(amount, 0f, Mathf.SmoothStep(0f, 1f, t / fadeOut)));
                yield return null;
            }

            ApplyLevel(0f);
            _pulse = null;
        }

        private void LateUpdate()
        {
            _flash = Mathf.MoveTowards(_flash, 0f, Time.deltaTime * 6f);
            UpdateOverlay();
        }

        private void ApplyLevel(float level)
        {
            _level = level;
            foreach (KeyValuePair<Light, float> pair in _originalIntensity)
            {
                if (pair.Key != null) pair.Key.intensity = pair.Value * Mathf.Lerp(1f, lightFloor, level);
            }

            RenderSettings.ambientIntensity = _originalAmbient * Mathf.Lerp(1f, lightFloor, level);
            onDimLevelChanged.Invoke(level);
        }

        private void UpdateOverlay()
        {
            bool active = _level > 0.001f || _flash > 0.001f;
            if (!active)
            {
                if (_overlay != null) _overlay.gameObject.SetActive(false);
                return;
            }

            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null) return;

            if (_overlay == null) CreateOverlay();
            _overlay.gameObject.SetActive(true);
            if (_overlay.parent != cam.transform) _overlay.SetParent(cam.transform, false);

            float height = 2f * overlayDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.6f;
            _overlay.localPosition = new Vector3(0f, 0f, overlayDistance);
            _overlay.localRotation = Quaternion.identity;
            _overlay.localScale = new Vector3(height * Mathf.Max(1f, cam.aspect) * 1.6f, height, 1f);

            Color dark = _tint;
            dark.a = _level * 0.85f;
            Color c = Color.Lerp(dark, new Color(_flashColor.r, _flashColor.g, _flashColor.b, 0.5f), _flash);
            _overlayRuntimeMaterial.color = c;
        }

        private void CreateOverlay()
        {
            _overlayRuntimeMaterial = overlayMaterial != null
                ? new Material(overlayMaterial)
                : HoloMaterials.CreateUnlitTransparent(Color.clear);
            _overlayRuntimeMaterial.renderQueue = 1999;

            GameObject quad = HoloMaterials.CreateFlatQuad("DimOverlay", _overlayRuntimeMaterial);
            _overlay = quad.transform;
        }
    }
}
