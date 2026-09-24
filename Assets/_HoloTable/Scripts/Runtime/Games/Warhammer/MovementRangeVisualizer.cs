using HoloTable.Core;
using HoloTable.VFX;
using TMPro;
using UnityEngine;

namespace HoloTable.Games.Warhammer
{
    /// <summary>
    /// Holographic movement template on the physical table: a glowing ring at the exact
    /// reach plus a translucent cylinder wall fading upwards, and an inch label.
    /// Meshes are generated procedurally (vertex-coloured), so no art is required.
    /// </summary>
    public sealed class MovementRangeVisualizer : MonoBehaviour
    {
        private const int Segments = 96;

        [SerializeField] private Material material;
        [SerializeField, Min(0.001f)] private float ringWidth = 0.004f;
        [SerializeField, Min(0f)] private float wallHeight = 0.05f;
        [SerializeField, Range(0f, 1f)] private float wallOpacity = 0.25f;
        [SerializeField, Min(0.1f)] private float radiusAnimationSpeed = 0.6f;
        [SerializeField] private TMP_Text label;

        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private Mesh _mesh;
        private float _radius;
        private float _targetRadius;
        private float _builtRadius = -1f;
        private Color _color = Color.cyan;
        private Color _builtColor;
        private Transform _follow;
        private Vector3 _center;

        public bool IsVisible => _renderer != null && _renderer.enabled;

        private void Awake()
        {
            _filter = gameObject.AddComponent<MeshFilter>();
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = material != null ? material : HoloMaterials.CreateUnlitTransparent(Color.white, 3001);
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _mesh = new Mesh { name = "MovementRange" };
            _mesh.MarkDynamic();
            _filter.sharedMesh = _mesh;
            _renderer.enabled = false;
            if (label != null) label.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }

        /// <summary>Shows the template centred on <paramref name="follow"/> (the miniature's base).</summary>
        public void Show(Transform follow, float radiusMeters, Color color, string text)
        {
            _follow = follow;
            _targetRadius = radiusMeters;
            if (!_renderer.enabled) _radius = 0f;
            _color = color;
            _renderer.enabled = true;
            SetLabel(text);
        }

        public void ShowAt(Vector3 center, float radiusMeters, Color color, string text)
        {
            _follow = null;
            _center = center;
            Show(null, radiusMeters, color, text);
        }

        public void UpdateRange(float radiusMeters, Color color, string text)
        {
            _targetRadius = radiusMeters;
            _color = color;
            SetLabel(text);
        }

        public void Hide()
        {
            _renderer.enabled = false;
            if (label != null) label.gameObject.SetActive(false);
        }

        private void SetLabel(string text)
        {
            if (label == null) return;
            label.gameObject.SetActive(!string.IsNullOrEmpty(text));
            label.text = text;
            label.color = _color;
        }

        private void LateUpdate()
        {
            if (!_renderer.enabled) return;

            TableSpace table = TableSpace.Current;
            Vector3 center = _follow != null ? _follow.position : _center;
            transform.SetPositionAndRotation(table.ProjectOnTable(center) + table.Normal * 0.002f,
                Quaternion.LookRotation(table.Forward, table.Normal));

            _radius = Mathf.MoveTowards(_radius, _targetRadius, radiusAnimationSpeed * Time.deltaTime);
            if (Mathf.Abs(_radius - _builtRadius) > 0.0005f || _color != _builtColor) Rebuild();

            if (label != null)
            {
                label.transform.position = transform.position + transform.forward * _radius + table.Normal * (wallHeight + 0.01f);
                Camera cam = Camera.main;
                if (cam != null) label.transform.rotation = Quaternion.LookRotation(label.transform.position - cam.transform.position);
            }
        }

        private void Rebuild()
        {
            _builtRadius = _radius;
            _builtColor = _color;

            int ringVerts = (Segments + 1) * 2;
            var vertices = new Vector3[ringVerts * 2];
            var colors = new Color[vertices.Length];
            var triangles = new int[Segments * 6 * 2];

            float inner = Mathf.Max(0f, _radius - ringWidth);
            Color solid = _color;
            Color wallBottom = new Color(_color.r, _color.g, _color.b, wallOpacity);
            Color wallTop = new Color(_color.r, _color.g, _color.b, 0f);

            for (int i = 0; i <= Segments; i++)
            {
                float a = i / (float)Segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));

                // Ring on the ground.
                vertices[i * 2] = dir * inner;
                vertices[i * 2 + 1] = dir * _radius;
                colors[i * 2] = solid;
                colors[i * 2 + 1] = solid;

                // Cylinder wall.
                int w = ringVerts + i * 2;
                vertices[w] = dir * _radius;
                vertices[w + 1] = dir * _radius + Vector3.up * wallHeight;
                colors[w] = wallBottom;
                colors[w + 1] = wallTop;
            }

            int t = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                int offset = pass * ringVerts;
                for (int i = 0; i < Segments; i++)
                {
                    int a0 = offset + i * 2, a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3;
                    triangles[t++] = a0; triangles[t++] = a1; triangles[t++] = b0;
                    triangles[t++] = b0; triangles[t++] = a1; triangles[t++] = b1;
                }
            }

            _mesh.Clear();
            _mesh.vertices = vertices;
            _mesh.colors = colors;
            _mesh.triangles = triangles;
            _mesh.RecalculateBounds();
        }
    }
}
