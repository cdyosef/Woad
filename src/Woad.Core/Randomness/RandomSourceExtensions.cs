namespace Woad.Core.Randomness;

public static class RandomSourceExtensions
{
    /// <summary>Fisher-Yates shuffle in place.</summary>
    public static void Shuffle<T>(this IRandomSource random, IList<T> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = random.NextInt(0, i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    public static T Pick<T>(this IRandomSource random, IReadOnlyList<T> items) => items[random.NextInt(0, items.Count)];

    public static bool CoinFlip(this IRandomSource random) => random.NextInt(0, 2) == 0;
}
