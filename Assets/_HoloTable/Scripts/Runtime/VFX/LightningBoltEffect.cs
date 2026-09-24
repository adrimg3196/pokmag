using System.Collections;
using UnityEngine;

namespace HoloTable.VFX
{
    /// <summary>
    /// Procedural jagged lightning (LineRenderer re-jittered every few ms + light flash).
    /// Self-destroys. Use <see cref="Strike"/> from spells, Pikachu attacks, plasma guns…
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class LightningBoltEffect : MonoBehaviour
    {
        private const int Segments = 18;

        private LineRenderer _line;
        private Light _light;

        public static LightningBoltEffect Strike(Vector3 from, Vector3 to, Color color, float duration = 0.45f, float width = 0.006f, Material material = null)
        {
            var go = new GameObject("LightningBolt");
            LightningBoltEffect bolt = go.AddComponent<LightningBoltEffect>();
            bolt.Play(from, to, color, duration, width, material);
            return bolt;
        }

        private void Awake()
        {
            _line = GetComponent<LineRenderer>();
            _line.positionCount = Segments;
            _line.useWorldSpace = true;
            _line.numCapVertices = 2;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _light = new GameObject("Flash").AddComponent<Light>();
            _light.transform.SetParent(transform, false);
            _light.type = LightType.Point;
            _light.range = 0.8f;
        }

        private void Play(Vector3 from, Vector3 to, Color color, float duration, float width, Material material)
        {
            _line.sharedMaterial = material != null ? material : HoloMaterials.CreateUnlitTransparent(Color.white);
            _line.startColor = Color.white;
            _line.endColor = color;
            _light.color = color;
            _light.transform.position = to;
            StartCoroutine(Run(from, to, duration, width));
        }

        private IEnumerator Run(Vector3 from, Vector3 to, float duration, float width)
        {
            Vector3 axis = to - from;
            Vector3 side = Vector3.Cross(axis.normalized, Vector3.up);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
            side.Normalize();
            Vector3 side2 = Vector3.Cross(axis.normalized, side);
            float jitter = axis.magnitude * 0.06f;

            for (float t = 0f; t < duration; t += 0.04f)
            {
                float k = t / duration;
                for (int i = 0; i < Segments; i++)
                {
                    float u = i / (float)(Segments - 1);
                    float envelope = Mathf.Sin(u * Mathf.PI); // pinned at both ends
                    Vector3 offset = (side * Random.Range(-1f, 1f) + side2 * Random.Range(-1f, 1f)) * (jitter * envelope);
                    _line.SetPosition(i, Vector3.Lerp(from, to, u) + offset);
                }

                float flicker = Random.value > 0.3f ? 1f : 0.2f;
                _line.widthMultiplier = width * flicker * (1f - k * 0.6f);
                _light.intensity = 4f * flicker * (1f - k);
                yield return new WaitForSeconds(0.04f);
            }

            Destroy(_line.sharedMaterial);
            Destroy(gameObject);
        }
    }
}
