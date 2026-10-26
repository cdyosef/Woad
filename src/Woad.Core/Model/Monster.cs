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

    /// <summary>Turns this monster will skip, frozen (the Frost Wand).</summary>
    public int FrozenTurns { get; internal set; }

    /// <summary>Gold it has stolen: dropped where it falls.</summary>
    public int StolenGold { get; internal set; }

    /// <summary>Cards it has stolen: dropped where it falls.</summary>
    public List<CardInstance> StolenCards { get; } = [];

    /// <summary>Carrying anything stolen.</summary>
    public bool HasLoot => StolenGold > 0 || StolenCards.Count > 0;

    /// <summary>Stun and Powerful Stun on it (see <see cref="StunState"/>).</summary>
    public StunState Stun { get; } = new();

    /// <summary>Knocked flat (by a blast): melee on it rolls an extra die, ranged attacks face an extra defense die; getting up costs 2 of its moves.</summary>
    public bool Prone { get; internal set; }

    /// <summary>Moves it has this turn: its own, less what a stun or getting up took.</summary>
    internal int MovesThisTurn { get; set; }

    /// <summary>Attack dice it has this turn: its own, less what a stun took.</summary>
    internal int AttackDiceThisTurn { get; set; }

    /// <summary>Flies over pits and water, unless stunned.</summary>
    public bool Flies => Definition.Flying && !Stun.IsStunned;

    /// <summary>Fire dice burning on it (0 when not on fire): rolled against it at the start of its turn.</summary>
    public int FireDice { get; internal set; }

    /// <summary>The hero whose fire this is, who gets the kill if it burns to death; null if nobody's.</summary>
    public int? IgnitedBy { get; internal set; }

    /// <summary>A reassembling monster knocked down by a weapon; it rises next enemy phase.</summary>
    public bool Collapsed { get; internal set; }

    public bool HasReassembled { get; internal set; }

    /// <summary>Slowed (by a Coward's Cloak): half its moves on its next turn.</summary>
    public bool Slowed { get; internal set; }

    /// <summary>The monster that raised it from the dead (a Necromancer), or null.</summary>
    public int? RaisedBy { get; internal set; }

    /// <summary>How many of the dead it has raised.</summary>
    public int Raised { get; internal set; }

    /// <summary>Turns acted (not counting skipped or rising turns); paces sweep attacks.</summary>
    public int TurnsTaken { get; internal set; }

    /// <summary>A frozen monster can't be frozen again until this round has passed.</summary>
    internal int FreezeImmuneThroughRound { get; set; }

    /// <summary>Whether it stands on a tile: its own, or for a huge monster any of the 2x2 it fills (from its top-left).</summary>
    public bool Occupies(GridPosition tile) =>
        tile.X >= Position.X && tile.Y >= Position.Y && tile.X < Position.X + Definition.Size && tile.Y < Position.Y + Definition.Size;

    /// <summary>Every tile it fills.</summary>
    public IEnumerable<GridPosition> Tiles =>
        from dx in Enumerable.Range(0, Definition.Size)
        from dy in Enumerable.Range(0, Definition.Size)
        select new GridPosition(Position.X + dx, Position.Y + dy);

    /// <summary>The tile it fills nearest <paramref name="from"/>: its own, or for a huge monster the closest of its four.</summary>
    public GridPosition NearestTile(GridPosition from) =>
        new(Math.Clamp(from.X, Position.X, Position.X + Definition.Size - 1), Math.Clamp(from.Y, Position.Y, Position.Y + Definition.Size - 1));

    public override string ToString() => $"{Definition.Name} #{Id} at {Position}";
}
