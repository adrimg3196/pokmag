using System;

namespace HoloTable.Domain
{
    /// <summary>
    /// Outbound port for randomness. Physical AR dice, a seeded RNG for replays
    /// or a scripted sequence in tests all implement it.
    /// </summary>
    public interface IRandomSource
    {
        /// <summary>Returns an integer in [minInclusive, maxExclusive).</summary>
        int Range(int minInclusive, int maxExclusive);
    }

    public static class RandomSourceExtensions
    {
        public static int RollD6(this IRandomSource random) => random.Range(1, 7);

        public static int[] RollD6(this IRandomSource random, int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

            var results = new int[count];
            for (int i = 0; i < count; i++)
            {
                results[i] = random.RollD6();
            }

            return results;
        }
    }

    public sealed class SystemRandomSource : IRandomSource
    {
        private readonly Random _random;

        public SystemRandomSource(int? seed = null)
        {
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public int Range(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);
    }
}
