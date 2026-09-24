using System;
using TMPro;
using UnityEngine;

namespace HoloTable.UI
{
    /// <summary>Pooled 3D pop-up number: pops, rises, drifts and fades while facing the camera.</summary>
    [RequireComponent(typeof(TextMeshPro))]
    public sealed class FloatingDamageNumber : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float lifetime = 1.1f;
        [SerializeField, Min(0f)] private float riseMeters = 0.09f;
        [SerializeField] private AnimationCurve popCurve = new AnimationCurve(
            new Keyframe(0f, 0.2f), new Keyframe(0.12f, 1.35f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.9f));

        private TextMeshPro _text;
        private Vector3 _origin;
        private Vector3 _drift;
        private float _age;
        private float _baseSize;
        private Color _color;
        private Action<FloatingDamageNumber> _release;

        private void Awake()
        {
            _text = GetComponent<TextMeshPro>();
            _text.alignment = TextAlignmentOptions.Center;
            _baseSize = 1f;
        }

        public void Play(Vector3 position, string text, Color color, float size, Action<FloatingDamageNumber> release)
        {
            _origin = position;
            _drift = UnityEngine.Random.insideUnitSphere * 0.02f;
            _age = 0f;
            _color = color;
            _baseSize = size;
            _release = release;
            _text.text = text;
            _text.color = color;
            transform.position = position;
            gameObject.SetActive(true);
        }

        private void LateUpdate()
        {
            _age += Time.deltaTime;
            float k = Mathf.Clamp01(_age / lifetime);

            float rise = 1f - (1f - k) * (1f - k); // ease-out
            transform.position = _origin + Vector3.up * (riseMeters * rise) + _drift * k;
            transform.localScale = Vector3.one * (_baseSize * popCurve.Evaluate(k));

            Color c = _color;
            c.a = k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f;
            _text.color = c;

            Camera cam = Camera.main;
            if (cam != null)
            {
                transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, cam.transform.up);
            }

            if (k >= 1f)
            {
                Action<FloatingDamageNumber> release = _release;
                _release = null;
                if (release != null) release(this);
                else Destroy(gameObject);
            }
        }
    }
}
