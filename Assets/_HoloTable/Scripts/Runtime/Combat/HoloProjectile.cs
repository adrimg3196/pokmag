using System;
using HoloTable.Core;
using UnityEngine;

namespace HoloTable.Combat
{
    /// <summary>
    /// Pooled particle projectile (fireball, lightning orb, bolter round…). Flies on a
    /// Bézier arc and homes on the defender's *current* position, so it still hits when
    /// the player nudges the physical card mid-flight.
    /// </summary>
    public sealed class HoloProjectile : MonoBehaviour
    {
        [SerializeField, Min(0.05f)] private float speed = 0.9f;
        [Tooltip("Arc apex height as a fraction of the travel distance.")]
        [SerializeField, Range(0f, 1f)] private float arcFactor = 0.25f;
        [SerializeField, Min(0f)] private float lingerSeconds = 0.6f;
        [SerializeField] private bool tintParticles = true;

        private ParticleSystem[] _emitters;
        private TrailRenderer[] _trails;
        private Vector3 _start;
        private Func<Vector3> _target;
        private Action<Vector3> _onArrive;
        private Action<HoloProjectile> _release;
        private float _progress;
        private float _lingerUntil;
        private bool _flying;

        private void Awake()
        {
            _emitters = GetComponentsInChildren<ParticleSystem>(true);
            _trails = GetComponentsInChildren<TrailRenderer>(true);
        }

        public void Launch(Vector3 from, Func<Vector3> target, Color tint, Action<Vector3> onArrive, Action<HoloProjectile> release)
        {
            _start = from;
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _onArrive = onArrive;
            _release = release;
            _progress = 0f;
            _flying = true;

            transform.position = from;
            gameObject.SetActive(true);

            foreach (TrailRenderer trail in _trails)
            {
                trail.Clear();
                trail.emitting = true;
                if (tintParticles) trail.startColor = tint;
            }

            foreach (ParticleSystem ps in _emitters)
            {
                if (tintParticles)
                {
                    ParticleSystem.MainModule main = ps.main;
                    main.startColor = tint;
                }

                ps.Clear(true);
                ps.Play(true);
            }
        }

        private void Update()
        {
            if (!_flying)
            {
                if (Time.time >= _lingerUntil) Release();
                return;
            }

            Vector3 end = _target();
            float distance = Mathf.Max(0.01f, Vector3.Distance(_start, end));
            _progress = Mathf.Min(1f, _progress + speed * Time.deltaTime / distance);

            Vector3 up = TableSpace.Current.Normal;
            Vector3 control = (_start + end) * 0.5f + up * (distance * arcFactor);
            Vector3 next = HoloMath.Bezier(_start, control, end, _progress);

            Vector3 velocity = next - transform.position;
            if (velocity.sqrMagnitude > 1e-8f) transform.rotation = Quaternion.LookRotation(velocity, up);
            transform.position = next;

            if (_progress >= 1f) Arrive(end);
        }

        private void Arrive(Vector3 point)
        {
            _flying = false;
            _lingerUntil = Time.time + lingerSeconds;

            foreach (ParticleSystem ps in _emitters) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            foreach (TrailRenderer trail in _trails) trail.emitting = false;

            Action<Vector3> arrive = _onArrive;
            _onArrive = null;
            arrive?.Invoke(point);
        }

        private void Release()
        {
            Action<HoloProjectile> release = _release;
            _release = null;
            if (release != null) release(this);
            else Destroy(gameObject);
        }

        private void OnDisable()
        {
            // Pool/scene teardown while flying: still resolve the hit so combat never stalls.
            // Uses the current position: the target may already be destroyed during teardown.
            if (_flying) Arrive(transform.position);
        }
    }
}
