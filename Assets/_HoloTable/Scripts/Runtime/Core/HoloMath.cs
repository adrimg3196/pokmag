using UnityEngine;

namespace HoloTable.Core
{
    /// <summary>Bridges UnityEngine and System.Numerics (used by the engine-free domain layer).</summary>
    public static class HoloMath
    {
        public static System.Numerics.Vector3 ToNumerics(this Vector3 v) => new System.Numerics.Vector3(v.x, v.y, v.z);

        public static Vector3 ToUnity(this System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);

        /// <summary>Frame-rate independent exponential smoothing factor.</summary>
        public static float Damp(float sharpness, float deltaTime) => 1f - Mathf.Exp(-sharpness * deltaTime);

        /// <summary>Combined renderer bounds in world space; zero-size bounds at the root if none.</summary>
        public static Bounds CalculateBounds(Transform root, bool includeInactive = false)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(includeInactive);
            Bounds bounds = new Bounds(root.position, Vector3.zero);
            bool initialised = false;

            foreach (Renderer r in renderers)
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;

                if (!initialised)
                {
                    bounds = r.bounds;
                    initialised = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            return bounds;
        }

        /// <summary>Quadratic Bézier, used for arcing projectiles.</summary>
        public static Vector3 Bezier(Vector3 a, Vector3 control, Vector3 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * control + t * t * b;
        }
    }
}
