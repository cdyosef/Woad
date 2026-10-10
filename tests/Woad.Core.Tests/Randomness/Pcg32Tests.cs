using Woad.Core.Randomness;

namespace Woad.Core.Tests.Randomness;

public class Pcg32Tests
{
    [Fact]
    public void MatchesReferenceImplementationOutput()
    {
        // pcg32_srandom_r(&rng, 42u, 54u) from the reference pcg32-demo.
        var rng = new Pcg32(42, 54);

        uint[] expected = [0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e];

        Assert.Equal(expected, Enumerable.Range(0, expected.Length).Select(_ => rng.NextUInt()));
    }

    [Fact]
    public void SameSeedProducesSameSequence()
    {
        var a = new Pcg32(1234);
        var b = new Pcg32(1234);

        Assert.Equal(
            Enumerable.Range(0, 100).Select(_ => a.NextInt(0, 1000)),
            Enumerable.Range(0, 100).Select(_ => b.NextInt(0, 1000)));
    }

    [Fact]
    public void NextIntStaysWithinBounds()
    {
        var rng = new Pcg32(7);

        for (int i = 0; i < 10_000; i++)
        {
            Assert.InRange(rng.NextInt(-3, 4), -3, 3);
        }
    }

    [Fact]
    public void NextIntRejectsEmptyRange()
    {
        var rng = new Pcg32(7);

        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(5, 5));
    }
}
