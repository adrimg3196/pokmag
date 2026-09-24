using UnityEngine;

namespace HoloTable.VFX
{
    /// <summary>
    /// Runtime fallbacks so every feature works before an artist assigns materials.
    /// "Sprites/Default" is always included in builds, unlit, alpha-blended, honours
    /// vertex colours and renders in Built-in, URP and HDRP-less mobile pipelines.
    /// </summary>
    public static class HoloMaterials
    {
        private static Texture2D _radialGradient;

        public static Material CreateUnlitTransparent(Color color, int renderQueue = 3000)
        {
            Shader shader = Shader.Find("Sprites/Default");
            var material = new Material(shader) { color = color, renderQueue = renderQueue };
            return material;
        }

        /// <summary>Soft round alpha falloff, generated once (contact shadows, glows).</summary>
        public static Texture2D RadialGradient
        {
            get
            {
                if (_radialGradient != null) return _radialGradient;

                const int size = 64;
                _radialGradient = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    name = "HoloRadialGradient",
                };

                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x + 0.5f) / size * 2f - 1f;
                        float dy = (y + 0.5f) / size * 2f - 1f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1f - d);
                        a = a * a * (3f - 2f * a); // smoothstep falloff
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                }

                _radialGradient.SetPixels32(pixels);
                _radialGradient.Apply(false, true);
                return _radialGradient;
            }
        }

        /// <summary>Creates a quad without collider. Orient with LookRotation(-normal, …) to lay it on a surface.</summary>
        public static GameObject CreateFlatQuad(string name, Material material)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Object.Destroy(quad.GetComponent<Collider>());
            MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return quad;
        }
    }
}
