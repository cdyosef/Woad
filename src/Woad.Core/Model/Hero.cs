using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Rules;

namespace Woad.Core.Model;

public enum HeroStatus
{
    Active,

    /// <summary>Reduced to 0 hearts; out for the rest of the run.</summary>
    Downed,

    /// <summary>Left the dungeon through the exit.</summary>
    Escaped,
}

public enum CardLocation
{
    Backpack,
    Belt,
    Equipped,
}

/// <summary>A player's character during a run.</summary>
public sealed class Hero
{
    private readonly Dictionary<EquipSlot, CardInstance> _equipment = [];
    private readonly List<CardInstance> _belt = [];
    private readonly List<CardInstance> _backpack = [];

    internal Hero(int id, int seat, string name, Stats baseStats, string roomId, GridPosition position)
    {
        Id = id;
        Seat = seat;
        Name = name;
        BaseStats = baseStats;
        RoomId = roomId;
        Position = position;
    }

    public int Id { get; }

    /// <summary>Turn order, starting at 0.</summary>
    public int Seat { get; }

    public string Name { get; }

    public Stats BaseStats { get; }

    public int Hearts { get; internal set; }

    public int LuckRemaining { get; internal set; }

    public HeroStatus Status { get; internal set; }

    public string RoomId { get; internal set; }

    public GridPosition Position { get; internal set; }

    public int Kills { get; internal set; }

    public bool IsActive => Status == HeroStatus.Active;

    /// <summary>Equipped cards by slot. A two-handed card appears under both hands.</summary>
    public IReadOnlyDictionary<EquipSlot, CardInstance> Equipment => _equipment;

    public IReadOnlyList<CardInstance> Belt => _belt;

    public IReadOnlyList<CardInstance> Backpack => _backpack;

    public IEnumerable<CardInstance> EquippedCards =>
        _equipment.OrderBy(entry => entry.Key).Select(entry => entry.Value).Distinct();

    public IEnumerable<CardInstance> AllCards => EquippedCards.Concat(_belt).Concat(_backpack);

    /// <summary>Base stats plus the bonuses of everything equipped.</summary>
    public Stats EffectiveStats => EquippedCards.Aggregate(BaseStats, (stats, card) => stats + card.Definition.Modifiers);

    public int MaxHearts => Math.Max(1, EffectiveStats.Hearts);

    /// <summary>Reach of the Attack action: the main-hand weapon's range, or melee when unarmed.</summary>
    public int AttackRange =>
        _equipment.TryGetValue(EquipSlot.MainHand, out CardInstance? weapon) ? weapon.Definition.AttackRange : 1;

    /// <summary>Gold value of the treasure carried; decides the winner.</summary>
    public int TreasureValue =>
        AllCards.Where(card => card.Definition.Type == CardType.Treasure).Sum(card => card.Definition.Value);

    public (CardInstance Card, CardLocation Location)? FindCard(int cardId)
    {
        if (EquippedCards.FirstOrDefault(card => card.Id == cardId) is { } equipped)
        {
            return (equipped, CardLocation.Equipped);
        }

        if (_belt.FirstOrDefault(card => card.Id == cardId) is { } belted)
        {
            return (belted, CardLocation.Belt);
        }

        if (_backpack.FirstOrDefault(card => card.Id == cardId) is { } packed)
        {
            return (packed, CardLocation.Backpack);
        }

        return null;
    }

    /// <summary>The slots an equippable card occupies.</summary>
    public static IReadOnlyList<EquipSlot> SlotsFor(CardDefinition card) =>
        card.TwoHanded ? [EquipSlot.MainHand, EquipSlot.OffHand]
        : card.Slot is { } slot ? [slot]
        : [];

    /// <summary>Cards that equipping <paramref name="card"/> would push out of their slots.</summary>
    public IReadOnlyList<CardInstance> CardsDisplacedBy(CardDefinition card) =>
        SlotsFor(card)
            .Select(slot => _equipment.GetValueOrDefault(slot))
            .OfType<CardInstance>()
            .Distinct()
            .ToList();

    internal void Equip(CardInstance card)
    {
        foreach (EquipSlot slot in SlotsFor(card.Definition))
        {
            _equipment[slot] = card;
        }
    }

    internal void AddToBelt(CardInstance card) => _belt.Add(card);

    internal void AddToBackpack(CardInstance card) => _backpack.Add(card);

    /// <summary>Removes the card from wherever it is held.</summary>
    internal void Remove(CardInstance card)
    {
        foreach (EquipSlot slot in _equipment.Where(entry => entry.Value == card).Select(entry => entry.Key).ToList())
        {
            _equipment.Remove(slot);
        }

        _belt.Remove(card);
        _backpack.Remove(card);
    }

    /// <summary>Trims current hearts after gear that granted hearts is removed.</summary>
    internal void ClampHearts() => Hearts = Math.Min(Hearts, MaxHearts);

    /// <summary>Puts a card the hero starts the run with into the best free place.</summary>
    internal bool TryPlaceStartingCard(CardInstance card)
    {
        CardDefinition definition = card.Definition;
        if (definition.Slot is not null && CardsDisplacedBy(definition).Count == 0)
        {
            Equip(card);
        }
        else if (definition.Usage == CardUsage.SingleUse && _belt.Count < GameRules.BeltCapacity)
        {
            _belt.Add(card);
        }
        else if (_backpack.Count < GameRules.BackpackCapacity)
        {
            _backpack.Add(card);
        }
        else
        {
            return false;
        }

        return true;
    }
}
