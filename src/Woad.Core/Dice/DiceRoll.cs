namespace Woad.Core.Dice;

/// <summary>The faces showing after rolling a pool of d6.</summary>
public sealed class DiceRoll(IReadOnlyList<int> faces)
{
    /// <summary>Attack hits and defense blocks succeed on a 5 or 6 unless a rule says otherwise.</summary>
    public const int DefaultSuccessFace = 5;

    public IReadOnlyList<int> Faces { get; } = faces;

    public int Total => Faces.Sum();

    public int CountSuccesses(int minimumFace = DefaultSuccessFace) => Faces.Count(face => face >= minimumFace);

    public override string ToString() => $"[{string.Join(", ", Faces)}] = {Total}";
}
