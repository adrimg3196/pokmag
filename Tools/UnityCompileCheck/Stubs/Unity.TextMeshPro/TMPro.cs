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
        public bool enableAutoSizing { get; set; }
        public float fontSizeMin { get; set; }
        public float fontSizeMax { get; set; }
        public RectTransform rectTransform => null;
    }

    public class TextMeshPro : TMP_Text { }

    public class TMP_FontAsset : ScriptableObject { }

    public static class TMP_Settings
    {
        public static TMP_FontAsset defaultFontAsset => null;
    }

    public class TextMeshProUGUI : TMP_Text { }
}
