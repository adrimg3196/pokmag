using HoloTable.Core;
using HoloTable.Entities;
using UnityEngine;

namespace HoloTable.VFX
{
    /// <summary>
    /// Soft blob shadow projected on the physical table under a hovering hologram.
    /// Real shadow physics: the higher the creature, the larger, softer and fainter
    /// the shadow. Needs no shadow-receiving geometry, so it works on passthrough.
    /// </summary>
    [RequireComponent(typeof(LivingEntityController))]
    public sealed class ContactShadow : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float maxOpacity = 0.55f;
        [Tooltip("Shadow growth per metre of height.")]
        [SerializeField, Min(0f)] private float spreadPerMeter = 2.2f;
        [Tooltip("Opacity loss per metre of height.")]
        [SerializeField, Min(0f)] private float fadePerMeter = 1.6f;
        [SerializeField, Min(0.1f)] private float footprintFactor = 1.15f;

        private LivingEntityController _entity;
        private Transform _quad;
        private Material _material;

        private void Awake()
        {
            _entity = GetComponent<LivingEntityController>();
            _material = HoloMaterials.CreateUnlitTransparent(new Color(0f, 0f, 0f, 0f), 2999);
            _material.mainTexture = HoloMaterials.RadialGradient;
            _quad = HoloMaterials.CreateFlatQuad($"{name}_Shadow", _material).transform;
        }

        private void OnDestroy()
        {
            if (_quad != null) Destroy(_quad.gameObject);
            if (_material != null) Destroy(_material);
        }

        private void LateUpdate()
        {
            TableSpace table = TableSpace.Current;
            float height = Mathf.Max(0f, _entity.HoverHeight);
            bool visible = _entity.State != EntityState.Dormant && _entity.State != EntityState.Dead;
            _quad.gameObject.SetActive(visible);
            if (!visible) return;

            Vector3 ground = table.ProjectOnTable(_entity.CenterWorld) + table.Normal * 0.0015f;
            float size = _entity.WorldFootprint * footprintFactor * (1f + height * spreadPerMeter);
            float alpha = maxOpacity * Mathf.Clamp01(1f - height * fadePerMeter * 0.5f);
            if (_entity.State == EntityState.Dying) alpha *= 0.5f;

            _quad.SetPositionAndRotation(ground, Quaternion.LookRotation(-table.Normal, table.Forward));
            _quad.localScale = new Vector3(size, size, 1f);
            _material.color = new Color(0f, 0f, 0f, alpha);
        }
    }
}
