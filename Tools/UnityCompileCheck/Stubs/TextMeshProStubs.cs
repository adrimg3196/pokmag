// Minimal API surface of TextMeshPro / uGUI used by HoloTable. Compile-check only.
using UnityEngine;

namespace TMPro
{
    public enum TextAlignmentOptions { Center = 514 }

    public enum FontStyles { Normal = 0, Bold = 1 }

    public abstract class TMP_Text : MonoBehaviour
    {
        public string text { get; set; }
        public Color color { get; set; }
        public float fontSize { get; set; }
        public FontStyles fontStyle { get; set; }
        public float outlineWidth { get; set; }
        public TextAlignmentOptions alignment { get; set; }
    }

    public class TextMeshPro : TMP_Text { }

    public class TextMeshProUGUI : TMP_Text { }
}

namespace UnityEngine.UI
{
    public class Image : MonoBehaviour
    {
        public Sprite sprite { get; set; }
        public Color color { get; set; }
        public float fillAmount { get; set; }
    }
}
