using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Rules;

namespace Woad.Core.Model;

public enum HeroStatus
{
    Active,

    /// <summary>
    /// Reduced to 0 hearts: lying on the floor, rolling a Keel Die each turn until three marks of
    /// life stand them back up or three marks of death kill them.
    /// </summary>
    KeelingOver,

    /// <summary>Died Keeling Over; out for the rest of the run, with a tombstone where they fell.</summary>
    Dead,

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

    internal Hero(int id, int seat, string name, Stats baseStats, string roomId, GridPosition position, Guid? characterId = null, string portrait = "")
    {
        Id = id;
        Seat = seat;
        Name = name;
        BaseStats = baseStats;
        RoomId = roomId;
        Position = position;
        Footing = position;
        CharacterId = characterId;
        Portrait = portrait;
    }

    public int Id { get; }

    /// <summary>The saved character playing this hero, if any.</summary>
    public Guid? CharacterId { get; }

    public string Portrait { get; }

    /// <summary>Turn order, starting at 0.</summary>
    public int Seat { get; }

    /// <summary>For a companion, the hero (a player) it belongs to; null for a player's own hero.</summary>
    public int? OwnerId { get; internal init; }

    /// <summary>The game plays a companion's turns, following its owner's orders.</summary>
    public bool IsCompanion => OwnerId is not null;

    /// <summary>"Player 2": counting players only, in seat order; 0 for a companion.</summary>
    public int PlayerNumber { get; internal init; }

    /// <summary>A companion's standing order (see <see cref="CompanionOrder"/>).</summary>
    public CompanionOrder Order { get; internal set; }

    /// <summary>
    /// The last tile in its room the hero stood on that isn't deep water: where they wash up if they
    /// drown (see <see cref="Grid.TileKind.DeepWater"/>).
    /// </summary>
    public GridPosition Footing { get; internal set; }

    /// <summary>Where a companion ordered to hold stands guard: the room and tile it was in at the time.</summary>
    public (string RoomId, GridPosition Position)? HoldAt { get; internal set; }

    public string Name { get; }

    public Stats BaseStats { get; }

    public int Hearts { get; internal set; }

    public int LuckRemaining { get; internal set; }

    public PowerBank Power { get; } = new();

    /// <summary>Small keys carried: each opens one locked door or chest. They don't take backpack space.</summary>
    public int Keys { get; internal set; }

    /// <summary>Carrying the Boss Key, the only way through the Boss Door.</summary>
    public bool HasBossKey { get; internal set; }

    public HeroStatus Status { get; internal set; }

    public string RoomId { get; internal set; }

    public GridPosition Position { get; internal set; }

    public int Kills { get; internal set; }

    /// <summary>XP earned this run, added to the character when the run ends.</summary>
    public int Xp { get; internal set; }

    /// <summary>The deepest floor reached. Reaching a new one restores Luck and recharges cards.</summary>
    public int DeepestFloor { get; internal set; } = 1;

    /// <summary>Marks of life earned Keeling Over (three stand the hero back up).</summary>
    public int LifeMarks { get; internal set; }

    /// <summary>Marks of death earned Keeling Over (three and the hero dies).</summary>
    public int DeathMarks { get; internal set; }

    /// <summary>Doom Dice added to the hero's next Keel roll: one for each 1 an ally rolled rousing them.</summary>
    public int DoomDiceOwed { get; internal set; }

    /// <summary>What brought the hero down, as their tombstone tells it ("Slain by a Goblin"), or null.</summary>
    public string? Bane { get; internal set; }

    /// <summary>Standing and able to act.</summary>
    public bool IsActive => Status == HeroStatus.Active;

    public bool IsKeelingOver => Status == HeroStatus.KeelingOver;

    /// <summary>Still in the dungeon and taking turns: standing or Keeling Over.</summary>
    public bool IsInPlay => Status is HeroStatus.Active or HeroStatus.KeelingOver;

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
