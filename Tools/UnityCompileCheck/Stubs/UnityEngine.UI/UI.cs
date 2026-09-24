// Minimal uGUI surface used by HoloTable. Compile-check only.
using UnityEngine;

namespace UnityEngine.UI
{
    public class Image : MonoBehaviour
    {
        public enum Type { Simple, Sliced, Tiled, Filled }

        public Sprite sprite { get; set; }
        public Color color { get; set; }
        public float fillAmount { get; set; }
        public Type type { get; set; }
        public RectTransform rectTransform => null;
    }
}
