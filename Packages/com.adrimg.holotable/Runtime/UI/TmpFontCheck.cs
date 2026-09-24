using TMPro;
using UnityEngine;

namespace HoloTable.UI
{
    /// <summary>
    /// Text created in code (default HUD, simulator labels, damage numbers) is invisible when the
    /// TMP Essential Resources were never imported. Warn once instead of rendering blank text.
    /// </summary>
    public static class TmpFontCheck
    {
        private static bool _warned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _warned = false;

        public static void WarnIfMissing(Object context)
        {
            if (_warned || TMP_Settings.defaultFontAsset != null) return;
            _warned = true;
            Debug.LogWarning("[HoloTable] TextMeshPro has no default font: names, HP and damage numbers will be invisible. " +
                             "Window ▸ TextMeshPro ▸ Import TMP Essential Resources (or run HoloTable ▸ Create Demo Scene).", context);
        }
    }
}
