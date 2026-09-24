using System;

namespace HoloTable.Domain
{
    /// <summary>Result of the dynamic scale computation for a hologram.</summary>
    public sealed record ScaleProfile(float UniformScale, float WorldHeight, float WorldFootprint, bool OverflowsFootprint);

    /// <summary>
    /// Decides how big a hologram is on the table. Small creatures are clamped so they
    /// stay inside their card/base; Large and above intentionally tower over the board.
    /// </summary>
    public static class ScaleRules
    {
        /// <summary>Fraction of the card footprint a small creature may occupy.</summary>
        public const float FootprintFill = 0.9f;

        /// <summary>Target standing height in metres for each size class.</summary>
        public static float TargetHeightMeters(SizeClass size) => size switch
        {
            SizeClass.Tiny => 0.035f,
            SizeClass.Small => 0.055f,
            SizeClass.Medium => 0.085f,
            SizeClass.Large => 0.16f,
            SizeClass.Huge => 0.26f,
            SizeClass.Colossal => 0.40f,
            _ => 0.085f,
        };

        public static bool MustFitFootprint(SizeClass size) => size <= SizeClass.Medium;

        /// <param name="nativeHeight">Model height at scale 1 (metres).</param>
        /// <param name="nativeFootprint">Largest horizontal extent at scale 1 (metres).</param>
        /// <param name="cardFootprint">Short side of the card or base diameter (metres).</param>
        /// <param name="size">Size class from the card data.</param>
        /// <param name="multiplier">Global user preference (accessibility / table size).</param>
        public static ScaleProfile Compute(
            float nativeHeight,
            float nativeFootprint,
            float cardFootprint,
            SizeClass size,
            float multiplier = 1f)
        {
            if (nativeHeight <= 0f) throw new ArgumentOutOfRangeException(nameof(nativeHeight));
            if (nativeFootprint <= 0f) throw new ArgumentOutOfRangeException(nameof(nativeFootprint));
            if (cardFootprint <= 0f) throw new ArgumentOutOfRangeException(nameof(cardFootprint));
            if (multiplier <= 0f) throw new ArgumentOutOfRangeException(nameof(multiplier));

            float scale = TargetHeightMeters(size) * multiplier / nativeHeight;

            if (MustFitFootprint(size))
            {
                float maxByFootprint = cardFootprint * FootprintFill / nativeFootprint;
                scale = Math.Min(scale, maxByFootprint);
            }

            float worldFootprint = nativeFootprint * scale;
            return new ScaleProfile(
                scale,
                nativeHeight * scale,
                worldFootprint,
                worldFootprint > cardFootprint + 1e-5f);
        }
    }
}
