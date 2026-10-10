namespace Woad.Core.Randomness;

/// <summary>
/// Independent random streams derived from one run seed. Keeping dice separate from loot and
/// monster behavior means, for example, that drawing a card never changes the next dice roll.
/// Tests can replace a single stream with scripted values.
/// </summary>
public sealed class GameRandom(ulong seed)
{
    public IRandomSource Dice { get; init; } = new Pcg32(seed, sequence: 1);

    public IRandomSource Loot { get; init; } = new Pcg32(seed, sequence: 2);

    public IRandomSource Behavior { get; init; } = new Pcg32(seed, sequence: 3);
}
