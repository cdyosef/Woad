namespace Woad.Core.Content;

public sealed record LootDeckEntry
{
    public required string Card { get; init; }

    public int Copies { get; init; } = 1;
}

/// <summary>A dungeon's identity: its name, loot deck and (later) monster roster and art.</summary>
public sealed record ThemeDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public IReadOnlyList<LootDeckEntry> LootDeck { get; init; } = [];
}
