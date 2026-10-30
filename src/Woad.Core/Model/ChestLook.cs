namespace Woad.Core.Model;

/// <summary>What a chest tile looks like: it opens the same way whatever it is.</summary>
public enum ChestLook
{
    Chest,
    Grave,
    Sarcophagus,
}

/// <summary>Who an enemy turn belongs to. Only a graveyard splits them: there the horde (the undead and their keepers) takes a turn of its own.</summary>
public enum Faction
{
    Monsters,
    Undead,
}

/// <summary>A place in a round's initiative order: a hero's turn, or an enemy turn, and the score it stands at.</summary>
public sealed record TurnSlot(int? HeroId, Faction? Enemy, int Score);
