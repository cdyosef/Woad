namespace Woad.Core.Randomness;

/// <summary>
/// The only source of randomness the rules engine may use. Keeping every roll behind a
/// seeded source makes runs reproducible (same seed, same dungeon and dice) and lets a
/// future server replay or verify a game.
/// </summary>
public interface IRandomSource
{
    uint NextUInt();

    /// <summary>Returns a uniformly distributed integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
    int NextInt(int minInclusive, int maxExclusive);
}
