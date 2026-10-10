using System.Numerics;

namespace Woad.Core.Randomness;

/// <summary>
/// PCG-XSH-RR 64/32 generator (https://www.pcg-random.org). Used instead of
/// <see cref="System.Random"/> because its output is fully specified, so a seed produces
/// the same sequence on every platform and .NET version.
/// </summary>
public sealed class Pcg32 : IRandomSource
{
    private const ulong Multiplier = 6364136223846793005UL;
    private const ulong DefaultSequence = 0xDA3E39CB94B95BDBUL;

    private readonly ulong _increment;
    private ulong _state;

    public Pcg32(ulong seed, ulong sequence = DefaultSequence)
    {
        _increment = (sequence << 1) | 1UL;
        _state = 0;
        NextUInt();
        _state += seed;
        NextUInt();
    }

    public uint NextUInt()
    {
        ulong old = _state;
        _state = unchecked(old * Multiplier + _increment);
        uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        int rotation = (int)(old >> 59);
        return BitOperations.RotateRight(xorShifted, rotation);
    }

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than minInclusive.");
        }

        // Rejection sampling removes modulo bias.
        uint range = (uint)(maxExclusive - minInclusive);
        uint threshold = unchecked(0u - range) % range;
        while (true)
        {
            uint value = NextUInt();
            if (value >= threshold)
            {
                return minInclusive + (int)(value % range);
            }
        }
    }
}
