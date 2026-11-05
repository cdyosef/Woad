namespace Woad.Core.Model;

/// <summary>Something that pays and is paid, in copper (see <see cref="Money"/>): a character, or the Party Chest.</summary>
public interface IPurse
{
    string Name { get; }

    /// <summary>What it holds, in copper.</summary>
    int Coins { get; }

    /// <summary>Pays <paramref name="amount"/> copper. False (and nothing paid) if it hasn't that much.</summary>
    bool Spend(int amount);

    /// <summary>Takes <paramref name="amount"/> copper in.</summary>
    void Earn(int amount);
}
