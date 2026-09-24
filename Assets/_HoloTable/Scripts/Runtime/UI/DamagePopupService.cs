using TMPro;
using UnityEngine;
using UnityEngine.Pool;

namespace HoloTable.UI
{
    /// <summary>
    /// Pooled floating numbers ("-60", "+30", "¡Súper eficaz!"). Static facade so any
    /// system can call <see cref="ShowDamage"/> without references; silently no-ops when
    /// no service exists in the scene.
    /// </summary>
    public sealed class DamagePopupService : MonoBehaviour
    {
        [SerializeField] private FloatingDamageNumber prefab;
        [SerializeField] private Color damageColor = new Color(1f, 0.35f, 0.25f);
        [SerializeField] private Color critColor = new Color(1f, 0.85f, 0.1f);
        [SerializeField] private Color healColor = new Color(0.35f, 1f, 0.5f);
        [SerializeField] private Color infoColor = new Color(0.6f, 0.9f, 1f);
        [Tooltip("World size of the text (TextMeshPro 3D, metres per unit font size).")]
        [SerializeField, Min(0.001f)] private float textScale = 0.02f;
        [SerializeField, Min(1)] private int prewarm = 8;

        private static DamagePopupService _instance;
        private ObjectPool<FloatingDamageNumber> _pool;

        private void Awake()
        {
            _instance = this;
            _pool = new ObjectPool<FloatingDamageNumber>(
                Create,
                n => { },
                n => n.gameObject.SetActive(false),
                n => Destroy(n.gameObject),
                collectionCheck: false,
                defaultCapacity: prewarm,
                maxSize: 64);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            _pool?.Clear();
        }

        public static void ShowDamage(Vector3 position, int amount, string label = null)
        {
            if (_instance == null) return;
            bool special = !string.IsNullOrEmpty(label);
            string text = special ? $"-{amount}\n<size=55%>{label}</size>" : $"-{amount}";
            _instance.Show(position, text, special ? _instance.critColor : _instance.damageColor, special ? 1.25f : 1f);
        }

        public static void ShowHeal(Vector3 position, int amount)
        {
            if (_instance == null) return;
            _instance.Show(position, $"+{amount}", _instance.healColor, 1f);
        }

        public static void ShowInfo(Vector3 position, string text)
        {
            if (_instance == null) return;
            _instance.Show(position, text, _instance.infoColor, 0.8f);
        }

        public static void ShowText(Vector3 position, string text, Color color, float relativeSize = 1f)
        {
            if (_instance == null) return;
            _instance.Show(position, text, color, relativeSize);
        }

        private void Show(Vector3 position, string text, Color color, float relativeSize)
        {
            FloatingDamageNumber n = _pool.Get();
            n.Play(position, text, color, textScale * relativeSize, _pool.Release);
        }

        private FloatingDamageNumber Create()
        {
            FloatingDamageNumber n;
            if (prefab != null)
            {
                n = Instantiate(prefab, transform);
            }
            else
            {
                var go = new GameObject("DamageNumber");
                go.transform.SetParent(transform, false);
                TextMeshPro tmp = go.AddComponent<TextMeshPro>();
                tmp.fontSize = 3f;
                tmp.fontStyle = FontStyles.Bold;
                tmp.outlineWidth = 0.2f;
                n = go.AddComponent<FloatingDamageNumber>();
            }

            n.gameObject.SetActive(false);
            return n;
        }
    }
}
