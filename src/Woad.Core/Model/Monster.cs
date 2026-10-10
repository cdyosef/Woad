using Woad.Core.Content;
using Woad.Core.Grid;

namespace Woad.Core.Model;

public sealed class Monster
{
    internal Monster(int id, MonsterDefinition definition, GridPosition position)
    {
        Id = id;
        Definition = definition;
        Position = position;
        Hearts = definition.Hearts;
    }

    public int Id { get; }

    public MonsterDefinition Definition { get; }

    public GridPosition Position { get; internal set; }

    public int Hearts { get; internal set; }

    /// <summary>Turns this monster will skip.</summary>
    public int StunnedTurns { get; internal set; }

    /// <summary>A reassembling monster knocked down by a weapon; it rises next enemy phase.</summary>
    public bool Collapsed { get; internal set; }

    public bool HasReassembled { get; internal set; }

    /// <summary>Turns acted (not counting skipped or rising turns); paces sweep attacks.</summary>
    public int TurnsTaken { get; internal set; }

    /// <summary>A stunned monster can't be stunned again until this round has passed.</summary>
    internal int StunImmuneThroughRound { get; set; }

    public override string ToString() => $"{Definition.Name} #{Id} at {Position}";
}
