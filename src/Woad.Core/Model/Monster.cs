using Woad.Core.Content;
using Woad.Core.Grid;

namespace Woad.Core.Model;

public sealed class Monster
{
    /// <param name="extraHearts">Hearts on top of the definition's, for a big party (see <see cref="Rules.GameRules.ExtraMonsterHearts"/>).</param>
    internal Monster(int id, MonsterDefinition definition, GridPosition position, int extraHearts = 0)
    {
        Id = id;
        Definition = definition;
        Position = position;
        MaxHearts = Hearts = definition.Hearts + extraHearts;
    }

    public int Id { get; }

    public MonsterDefinition Definition { get; }

    public GridPosition Position { get; internal set; }

    public int Hearts { get; internal set; }

    /// <summary>Starting hearts: the definition's, plus more in a big party.</summary>
    public int MaxHearts { get; }

    /// <summary>Power Dice banked from sixes on defense rolls, spent to save itself from hits.</summary>
    public PowerBank Power { get; } = new();

    /// <summary>Someone has already stolen from it; it guards what's left.</summary>
    public bool Robbed { get; internal set; }

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
