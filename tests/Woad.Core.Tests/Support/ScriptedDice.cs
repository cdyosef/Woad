using Woad.Core.Randomness;

namespace Woad.Core.Tests.Support;

/// <summary>Dice that roll exactly the faces given, in order. Rolling past the script fails the test.</summary>
internal sealed class ScriptedDice(params int[] faces) : IRandomSource
{
    private readonly Queue<int> _faces = new(faces);

    public int Remaining => _faces.Count;

    public uint NextUInt() => throw new NotSupportedException("Scripted dice only roll faces.");

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (!_faces.TryDequeue(out int face))
        {
            throw new InvalidOperationException("The test rolled more dice than it scripted.");
        }

        if (face < minInclusive || face >= maxExclusive)
        {
            throw new InvalidOperationException($"Scripted face {face} is outside [{minInclusive}, {maxExclusive}).");
        }

        return face;
    }
}
