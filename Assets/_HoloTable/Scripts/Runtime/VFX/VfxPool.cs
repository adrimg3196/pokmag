using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace HoloTable.VFX
{
    /// <summary>
    /// Pooled one-shot particle effects (impacts, spawn portals, bursts). Instantiating a
    /// ParticleSystem on Quest costs milliseconds on the impact frame; this reuses them.
    /// Each instance returns to its pool after its duration + max start lifetime.
    /// </summary>
    public static class VfxPool
    {
        private static readonly Dictionary<ParticleSystem, ObjectPool<ParticleSystem>> Pools = new Dictionary<ParticleSystem, ObjectPool<ParticleSystem>>();
        private static Transform _root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Pools.Clear();
            _root = null;
        }

        /// <summary>Plays a pooled copy of <paramref name="prefab"/>. No-op for a null prefab.</summary>
        /// <param name="lifetime">Seconds before the instance returns to the pool. Default: duration + max start lifetime.
        /// Pass it explicitly for effects the caller keeps alive and stops itself (e.g. a looping cocoon).</param>
        public static ParticleSystem Play(ParticleSystem prefab, Vector3 position, Quaternion rotation, float scale = 1f, Color? tint = null, float? lifetime = null)
        {
            if (prefab == null) return null;

            ParticleSystem fx = GetPool(prefab).Get();
            PooledVfx handle = fx.GetComponent<PooledVfx>();
            fx.transform.SetPositionAndRotation(position, rotation);
            fx.transform.localScale = Vector3.one * scale;

            ParticleSystem.MainModule main = fx.main;
            main.startColor = tint.HasValue ? new ParticleSystem.MinMaxGradient(tint.Value) : handle.OriginalColor;

            fx.Clear(true);
            fx.Play(true);
            handle.Arm(lifetime ?? main.duration + main.startLifetime.constantMax + 0.25f);
            return fx;
        }

        /// <summary>Creates <paramref name="count"/> instances up-front (call during loading, not in combat).</summary>
        public static void Prewarm(ParticleSystem prefab, int count)
        {
            if (prefab == null || count <= 0) return;

            ObjectPool<ParticleSystem> pool = GetPool(prefab);
            var temp = new List<ParticleSystem>(count);
            for (int i = 0; i < count; i++) temp.Add(pool.Get());
            foreach (ParticleSystem fx in temp) pool.Release(fx);
        }

        private static ObjectPool<ParticleSystem> GetPool(ParticleSystem prefab)
        {
            if (_root == null)
            {
                // Scene changed (root destroyed with it): pooled instances are gone too.
                Pools.Clear();
                _root = new GameObject("[VfxPool]").transform;
            }

            if (Pools.TryGetValue(prefab, out ObjectPool<ParticleSystem> pool)) return pool;

            pool = null;
            pool = new ObjectPool<ParticleSystem>(
                () =>
                {
                    ParticleSystem fx = Object.Instantiate(prefab, _root);
                    // The pool owns the lifetime: a Disable/Destroy stop action would bypass it.
                    ParticleSystem.MainModule main = fx.main;
                    main.stopAction = ParticleSystemStopAction.None;
                    fx.gameObject.AddComponent<PooledVfx>().Bind(pool);
                    return fx;
                },
                fx => fx.gameObject.SetActive(true),
                fx => fx.gameObject.SetActive(false),
                fx => { if (fx != null) Object.Destroy(fx.gameObject); },
                collectionCheck: false,
                defaultCapacity: 4,
                maxSize: 32);
            Pools.Add(prefab, pool);
            return pool;
        }
    }

    /// <summary>Timer that hands a pooled effect back to its pool.</summary>
    [DisallowMultipleComponent]
    public sealed class PooledVfx : MonoBehaviour
    {
        private ObjectPool<ParticleSystem> _pool;
        private ParticleSystem _system;
        private float _releaseAt;
        private bool _armed;

        internal ParticleSystem.MinMaxGradient OriginalColor { get; private set; }

        internal void Bind(ObjectPool<ParticleSystem> pool)
        {
            _pool = pool;
            _system = GetComponent<ParticleSystem>();
            OriginalColor = _system.main.startColor; // restored for untinted plays
        }

        internal void Arm(float seconds)
        {
            _releaseAt = Time.time + seconds;
            _armed = true;
        }

        private void Update()
        {
            if (!_armed || Time.time < _releaseAt) return;
            _armed = false;
            _pool.Release(_system);
        }
    }
}
