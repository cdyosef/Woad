using Woad.Core.Grid;

namespace Woad.Core.Tavern;

/// <summary>A party inside a tavern, walking in a line from the door. Nothing here is saved: each visit starts at the door.</summary>
public sealed class TavernVisit
{
    private readonly PartyLine _line;

    public TavernVisit(TavernDefinition tavern, int partySize)
    {
        Tavern = tavern;
        _line = new PartyLine(tavern.Start);
        _line.SetSize(partySize);
    }

    public TavernDefinition Tavern { get; }

    public GridPosition Leader => _line.Leader;

    public IReadOnlyList<GridPosition> Trail => _line.Trail;

    /// <summary>Who the leader can talk to from where they stand.</summary>
    public TavernNpc? NpcInReach => Tavern.NpcInReach(Leader);

    public bool AtDoor => Tavern.IsDoor(Leader);

    public void SetPartySize(int size) => _line.SetSize(size);

    /// <summary>Steps the leader one tile, everyone following. Returns why not, or null if they stepped.</summary>
    public string? Step(Direction direction)
    {
        GridPosition next = Leader.Step(direction);
        if (Tavern.NpcAt(next) is { } npc)
        {
            return $"{npc.Name} is in the way.";
        }

        if (!Tavern.IsWalkable(next))
        {
            return "The way is blocked.";
        }

        _line.MoveTo(next);
        return null;
    }

    /// <summary>The shortest walk to a tile, or null if it can't be reached.</summary>
    public IReadOnlyList<Direction>? PathTo(GridPosition target) =>
        Tavern.IsWalkable(target) ? PartyLine.PathTo(Leader, target, Tavern.IsWalkable) : null;
}
