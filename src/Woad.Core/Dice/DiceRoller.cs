using Woad.Core.Randomness;

namespace Woad.Core.Dice;

/// <summary>Rolls pools of six-sided dice for movement, attack and defense.</summary>
public sealed class DiceRoller(IRandomSource random)
{
    public const int Sides = 6;

    public DiceRoll Roll(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var faces = new int[count];
        for (int i = 0; i < count; i++)
        {
            faces[i] = random.NextInt(1, Sides + 1);
        }

        return new DiceRoll(faces);
    }
}
