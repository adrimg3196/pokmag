using System.Collections.Generic;
using UnityEngine;

namespace HoloTable.VFX
{
    /// <summary>
    /// Drives hologram shader parameters through MaterialPropertyBlocks (no material
    /// instancing, no leaks). Works with any shader: properties that are missing are
    /// skipped, and flash falls back to tinting _BaseColor / _Color.
    /// Expected (optional) properties on the hologram shader:
    ///   _DissolveAmount (0 = solid, 1 = gone) · _HoloFlash (0..1) · _HoloFlashColor · _HoloTint
    /// </summary>
    public sealed class HologramMaterialDriver
    {
        private static readonly int FlashId = Shader.PropertyToID("_HoloFlash");
        private static readonly int FlashColorId = Shader.PropertyToID("_HoloFlashColor");
        private static readonly int TintId = Shader.PropertyToID("_HoloTint");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly struct Slot
        {
            public Slot(Renderer renderer, bool dissolve, bool flash, bool tint, int colorId, Color original)
            {
                Renderer = renderer;
                HasDissolve = dissolve;
                HasFlash = flash;
                HasTint = tint;
                ColorPropertyId = colorId;
                OriginalColor = original;
            }

            public Renderer Renderer { get; }
            public bool HasDissolve { get; }
            public bool HasFlash { get; }
            public bool HasTint { get; }
            public int ColorPropertyId { get; }
            public Color OriginalColor { get; }
        }

        private readonly List<Slot> _slots = new List<Slot>();
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private readonly int _dissolveId;

        private float _dissolve;
        private float _flash;
        private Color _flashColor = Color.white;
        private Color _tint = Color.white;
        private bool _dirty = true;

        public HologramMaterialDriver(Transform root, string dissolveProperty = "_DissolveAmount")
        {
            _dissolveId = Shader.PropertyToID(dissolveProperty);

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;

                bool dissolve = false, flash = false, tint = false;
                int colorId = 0;
                Color original = Color.white;

                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    dissolve |= m.HasProperty(_dissolveId);
                    flash |= m.HasProperty(FlashId);
                    tint |= m.HasProperty(TintId);
                    if (colorId == 0)
                    {
                        if (m.HasProperty(BaseColorId)) { colorId = BaseColorId; original = m.GetColor(BaseColorId); }
                        else if (m.HasProperty(ColorId)) { colorId = ColorId; original = m.GetColor(ColorId); }
                    }
                }

                _slots.Add(new Slot(r, dissolve, flash, tint, colorId, original));
                SupportsDissolve |= dissolve;
            }
        }

        /// <summary>True if at least one renderer can dissolve; otherwise callers fall back to scaling.</summary>
        public bool SupportsDissolve { get; }

        public void SetDissolve(float amount)
        {
            amount = Mathf.Clamp01(amount);
            if (Mathf.Approximately(amount, _dissolve)) return;
            _dissolve = amount;
            _dirty = true;
        }

        public void SetFlash(float amount, Color color)
        {
            amount = Mathf.Clamp01(amount);
            if (Mathf.Approximately(amount, _flash) && color == _flashColor) return;
            _flash = amount;
            _flashColor = color;
            _dirty = true;
        }

        public void SetTint(Color tint)
        {
            if (tint == _tint) return;
            _tint = tint;
            _dirty = true;
        }

        public void SetVisible(bool visible)
        {
            foreach (Slot s in _slots)
            {
                if (s.Renderer != null) s.Renderer.enabled = visible;
            }
        }

        /// <summary>Pushes pending changes. Call once per frame (LateUpdate).</summary>
        public void Apply()
        {
            if (!_dirty) return;
            _dirty = false;

            foreach (Slot s in _slots)
            {
                if (s.Renderer == null) continue;

                s.Renderer.GetPropertyBlock(_block);
                if (s.HasDissolve) _block.SetFloat(_dissolveId, _dissolve);
                if (s.HasTint) _block.SetColor(TintId, _tint);

                if (s.HasFlash)
                {
                    _block.SetFloat(FlashId, _flash);
                    _block.SetColor(FlashColorId, _flashColor);
                }
                else if (s.ColorPropertyId != 0)
                {
                    _block.SetColor(s.ColorPropertyId, Color.Lerp(s.OriginalColor, _flashColor, _flash));
                }

                s.Renderer.SetPropertyBlock(_block);
            }
        }
    }
}
