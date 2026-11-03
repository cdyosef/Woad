using Woad.Core.Characters;
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

    /// <summary>In the hero's arms, in the carrying slots: no Rumble and no Od Hand while anything is there.</summary>
    Carried,
}

/// <summary>A player's character during a run.</summary>
public sealed class Hero
{
    private readonly Dictionary<EquipSlot, CardInstance> _equipment = [];
    private readonly List<CardInstance> _belt = [];
    private readonly List<CardInstance> _backpack = [];
    private readonly List<CardInstance> _mods = [];
    private readonly List<CardInstance> _carried = [];
    private readonly List<SpellDefinition> _spells = [];

    internal Hero(int id, int seat, string name, Stats baseStats, string roomId, GridPosition position, Guid? characterId = null, string portrait = "", Appearance? appearance = null)
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
        Appearance = appearance ?? Appearance.Default;
    }

    public int Id { get; }

    /// <summary>Rooms this hero has searched (a companion exploring searches each once).</summary>
    public HashSet<string> SearchedRoomIds { get; } = [];

    /// <summary>The saved character playing this hero, if any.</summary>
    public Guid? CharacterId { get; }

    public string Portrait { get; }

    /// <summary>The character's look and colors (cosmetic).</summary>
    public Appearance Appearance { get; internal set; }

    /// <summary>Turn order, starting at 0.</summary>
    public int Seat { get; }

    /// <summary>For a companion, the hero (a player) it belongs to; null for a player's own hero.</summary>
    public int? OwnerId { get; internal set; }

    /// <summary>The game plays a companion's turns, following its owner's orders.</summary>
    public bool IsCompanion => OwnerId is not null;

    /// <summary>"Player 2": counting players only, in seat order; 0 for a companion.</summary>
    public int PlayerNumber { get; internal set; }

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

    public Stats BaseStats { get; internal set; }

    public int Hearts { get; internal set; }

    public int LuckRemaining { get; internal set; }

    /// <summary>
    /// Clover Luck: points from Lucky Clovers picked up, kept until used (between runs too), used once
    /// <see cref="LuckRemaining"/> runs out, by anyone, Luck stat or not. At most <see cref="GameRules.MaxCloverLuck"/>.
    /// </summary>
    public int CloverLuck { get; internal set; }

    /// <summary>The Clover Luck they started the run with: a hero who doesn't get out keeps no more than this.</summary>
    public int StartCloverLuck { get; internal init; }

    /// <summary>A save after a fall waiting on them (see <c>GameSession.StartFallSave</c>), or null.</summary>
    public FallSave? PendingSave { get; internal set; }

    /// <summary>Od Hand dice spent on their last turn: off their turn, only the rest can be spent (they come back at the start of their turn).</summary>
    public int OdHandSpent { get; internal set; }

    /// <summary>Rolls they've made this round (for the Cursed dice, which go on the first three).</summary>
    public int RollsThisRound { get; internal set; }

    /// <summary>All the Luck they have to try: their Luck left this floor, then their Clover Luck.</summary>
    public int LuckLeft => LuckRemaining + CloverLuck;

    /// <summary>Spends a Luck point: from their Luck this floor first, then their Clover Luck.</summary>
    internal void SpendLuck()
    {
        if (LuckRemaining > 0)
        {
            LuckRemaining--;
        }
        else if (CloverLuck > 0)
        {
            CloverLuck--;
        }
    }

    /// <summary>In the air on the Icarus Wings, for the rest of their turn or until they land.</summary>
    public bool Aloft { get; internal set; }

    /// <summary>Rounds left of Snowshoes (Icicle Arrow): they walk on ice like floor.</summary>
    public int SnowshoesRounds { get; internal set; }

    /// <summary>Rounds left of Shining Light: they give off light, and gain <see cref="ShiningDefense"/> and a Search die.</summary>
    public int ShiningRounds { get; internal set; }

    /// <summary>Rounds of Thieves' Oil left (see <see cref="Boon.ThievesOil"/>).</summary>
    public int ThievesOilRounds { get; internal set; }

    /// <summary>A Medicine Bag in hand: a second roll to resist catching a disease.</summary>
    public bool HoldsMedicineBag => EquippedCards.Any(card => card.Definition.MedicineBag);

    /// <summary>Shining Light's Defense: added to every defense while it lasts.</summary>
    public int ShiningDefense { get; internal set; }

    /// <summary>Rounds left of Shadowbind: invisible to monsters, until they attack, cast or use an item.</summary>
    public int ShadowboundRounds { get; internal set; }

    public PowerBank Power => _power ??= new PowerBank(() => GameRules.MaxBankedPowerDice + Math.Max(0, EffectiveStats.Magic));

    private PowerBank? _power;

    /// <summary>Gold in the purse, from coins found this run. It doesn't take backpack space and counts as treasure.</summary>
    public int Gold { get; internal set; }

    /// <summary>Small keys carried: each opens one locked door or chest. They don't take backpack space.</summary>
    public int Keys { get; internal set; }

    /// <summary>Carrying the Boss Key, the only way through the Boss Door.</summary>
    public bool HasBossKey { get; internal set; }

    public HeroStatus Status { get; internal set; }

    public string RoomId { get; internal set; }

    public GridPosition Position { get; internal set; }

    public int Kills { get; internal set; }

    /// <summary>The rolls Luck is asked on when they're about to fail (the Luck menu's switches).</summary>
    public LuckRoll LuckAsks { get; internal set; } = LuckRoll.All;

    /// <summary>Rooms they searched first (each an Explorer bonus): the most on a run wins the Explorer Award.</summary>
    public int ExplorerBonus { get; internal set; }

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

    /// <summary>Stun and Powerful Stun on the hero (see <see cref="StunState"/>).</summary>
    public StunState Stun { get; } = new();

    /// <summary>Fire dice burning on the hero (0 when not on fire): rolled against them at the start of their turn.</summary>
    public int FireDice { get; internal set; }

    /// <summary>Tokens the typed dice have left on them: poison, freeze, blindness and the rest.</summary>
    public Conditions Conditions { get; } = new();

    /// <summary>
    /// Lying down on purpose (or after stopping, dropping and rolling): every step costs double, melee
    /// attacks on them roll an extra die and ranged attacks face an extra defense die. In deep water it
    /// is diving: out of reach of ranged attacks. See <see cref="IsProne"/>.
    /// </summary>
    public bool Prone { get; internal set; }

    /// <summary>Their name with the Merchant Guild coming into the run (it sets merchants' prices).</summary>
    public int GuildStanding { get; internal init; }

    /// <summary>The character's level, counting the XP earned in this run.</summary>
    public int Level => Progression.LevelForXp(StartXp + Xp);

    /// <summary>The character's XP coming into the run.</summary>
    public int StartXp { get; internal init; }

    /// <summary>The character's points bought coming into the run; null for a debug hero (who can't level up in a run).</summary>
    public StatAllocation? StartAllocation { get; internal init; }

    /// <summary>The points bought so far, counting any spent during the run; null for a debug hero.</summary>
    public StatAllocation? Allocation { get; internal set; }

    /// <summary>
    /// The Focus Bonus: +1 for each rank bought in Od Hand and in Magic, added to magic attacks and to Focus
    /// checks (learning a spell). A hero with no points bought (a debug hero) has their Magic rank.
    /// </summary>
    public int Focus => Allocation is { } bought ? bought.OdHandDice + bought.Magic : Math.Max(0, EffectiveStats.Magic);

    /// <summary>Points the character had to spend coming into the run.</summary>
    public int StartUnspentPoints { get; internal init; }

    /// <summary>Points to spend now: those brought in, plus a level's worth for each level gained in the run, less what's been spent.</summary>
    public int PointsToSpend => StartAllocation is not { } start || Allocation is not { } now ? 0
        : StartUnspentPoints + (Level - Progression.LevelForXp(StartXp)) * Progression.PointsPerLevel - (PointBuy.Cost(now) - PointBuy.Cost(start));

    /// <summary>
    /// Raised or grown in the fight (a zombie, a Sentient Shrub), not a character: it follows its owner
    /// for the rest of the run, looking like the monster <see cref="Guise"/>, and is gone after.
    /// </summary>
    public bool Summoned { get; internal init; }

    /// <summary>A summoned companion's look: the monster it is (or was).</summary>
    public string? Guise { get; internal init; }

    /// <summary>Its Wend dice always come up this (a Sentient Shrub's 1), or null to roll them.</summary>
    public int? FixedWendFace { get; internal init; }

    /// <summary>How it manages its own things on its turns, as a companion.</summary>
    public CompanionPolicy Policy { get; internal set; } = CompanionPolicy.Default;

    /// <summary>What this run has done to their Guild standing: rescues raise it, thefts and murders lower it.</summary>
    public int GuildStandingChange { get; internal set; }

    /// <summary>What this run has done to their standing with the Law (crimes lower it).</summary>
    public int LawStandingChange { get; internal set; }

    /// <summary>Extra Defense dice spent since the start of their turn.</summary>
    public int DefenseDiceSpent { get; internal set; }

    /// <summary>Guard Dice spent on allies since the start of their turn.</summary>
    public int GuardDiceSpent { get; internal set; }

    /// <summary>Their Block is spent until the start of their turn.</summary>
    public bool BlockSpent { get; internal set; }

    /// <summary>Dice rolled and held for their next defense (their Parry, or Guard from them or an ally beside them).</summary>
    public List<HeldDie> HeldDefense { get; } = [];

    /// <summary>The monster whose grab last caught them (a Mimic); see <c>GameSession.HeldBy</c> for whether it still holds them.</summary>
    public int? GrabbedBy { get; internal set; }

    /// <summary>What they've learned to do (see <see cref="Skill"/>).</summary>
    public Skill Skills { get; internal set; } = Content.Skills.All;

    public bool Knows(Skill skill) => (Skills & skill) == skill;

    /// <summary>Torches held (and so lit): one in each hand at most, and a flame of Hurl Fire (see <see cref="Flame"/>).</summary>
    public int TorchesHeld => EquippedCards.Count(card => card.Definition.Torch) + (Flame ? 1 : 0);

    /// <summary>A flame of Hurl Fire cast on themselves: a lit torch needing no hand, until a 1 in an attack puts it out or they fall to the floor below.</summary>
    public bool Flame { get; internal set; }

    /// <summary>Their Medicine Bags, on the belt or in a hand, if they have any to hand.</summary>
    public CardInstance? MedicineBag => EquippedCards.Concat(_belt).FirstOrDefault(card => card.Definition.MedicineBag);

    /// <summary>Their Bandages, anywhere they carry them.</summary>
    public CardInstance? Bandages => AllCards.FirstOrDefault(card => card.Definition.Bandage);

    /// <summary>A Shovel in their hands.</summary>
    public bool HoldsShovel => EquippedCards.Any(card => card.Definition.Shovel);

    /// <summary>Shackled by a monster's chain: the lock's difficulty. No moving until it's picked or a key frees them.</summary>
    public int? Shackled { get; internal set; }

    /// <summary>
    /// Keel rolls made since keeling over. Every roll after the first adds that many more Doom Dice (the
    /// creeping doom): none on the first, 1 on the second, 2 on the third...
    /// </summary>
    public int KeelRolls { get; internal set; }

    /// <summary>What brought the hero down, as their tombstone tells it ("Slain by a Goblin"), or null.</summary>
    public string? Bane { get; internal set; }

    /// <summary>Standing and able to act.</summary>
    public bool IsActive => Status == HeroStatus.Active;

    public bool IsKeelingOver => Status == HeroStatus.KeelingOver;

    /// <summary>Lying down: prone on purpose, or Keeling Over.</summary>
    public bool IsProne => Prone || IsKeelingOver;

    /// <summary>Still in the dungeon and taking turns: standing or Keeling Over.</summary>
    public bool IsInPlay => Status is HeroStatus.Active or HeroStatus.KeelingOver;

    /// <summary>Equipped cards by slot. A two-handed card appears under both hands.</summary>
    public IReadOnlyDictionary<EquipSlot, CardInstance> Equipment => _equipment;

    public IReadOnlyList<CardInstance> Belt => _belt;

    public IReadOnlyList<CardInstance> Backpack => _backpack;

    /// <summary>The mods on the belt and the buckle (Belt Studs, Buckle Horns).</summary>
    public IReadOnlyList<CardInstance> Mods => _mods;

    /// <summary>What the hero carries in their arms (see <see cref="GameRules.CarrySlots"/>).</summary>
    public IReadOnlyList<CardInstance> Carried => _carried;

    /// <summary>How many cards the belt worn holds: none for a hero who is beltless.</summary>
    public int BeltCapacity => _equipment.GetValueOrDefault(EquipSlot.Belt)?.Definition.BeltSlots ?? 0;

    /// <summary>The carrying slots in use.</summary>
    public int CarrySlotsUsed => _carried.Sum(card => card.Definition.CarrySlots);

    /// <summary>Arms full of something: no Rumble and no Od Hand until it's put down or away.</summary>
    public bool IsCarrying => _carried.Count > 0;

    /// <summary>Whether there's room in the hero's arms for this card (or a stack of it they carry already).</summary>
    public bool CanCarryInArms(CardDefinition definition) =>
        _carried.Any(card => card.Definition == definition && definition.IsStackable) || CarrySlotsUsed + definition.CarrySlots <= GameRules.CarrySlots;

    /// <summary>The ids of the belts (or buckles) the hero owns, worn or not (a belt unbuckled whole counts its buckle): the looks they can choose.</summary>
    public IReadOnlyList<string> Looks(CardType type) =>
        [.. AllCards.Concat(AllCards.SelectMany(card => card.Packed?.Attached ?? [])).Where(card => card.Definition.Type == type).Select(card => card.Definition.Id).Distinct()];

    /// <summary>Why this mod can't go on now (no belt or buckle for it, or one like it there already), or null if it can.</summary>
    public string? CheckMod(CardDefinition mod)
    {
        if (mod.ModFor is not { } on)
        {
            return $"The {mod.Name} isn't a mod.";
        }

        if (!_equipment.ContainsKey(on))
        {
            return on == EquipSlot.Belt ? $"The {mod.Name} goes on a belt, and you're beltless." : $"The {mod.Name} goes on a buckle, and you've none.";
        }

        return !mod.Repeats && _mods.Any(card => card.Definition == mod) ? $"Your {(on == EquipSlot.Belt ? "belt" : "buckle")} has {mod.Name} already." : null;
    }

    /// <summary>The spells they know: a basic one for each rank of Magic, and any learned from scrolls.</summary>
    public IReadOnlyList<SpellDefinition> Spells => _spells;

    internal void Learn(SpellDefinition spell) => _spells.Add(spell);

    public IEnumerable<CardInstance> EquippedCards =>
        _equipment.OrderBy(entry => entry.Key).Select(entry => entry.Value).Distinct();

    public IEnumerable<CardInstance> AllCards => EquippedCards.Concat(_mods).Concat(_belt).Concat(_backpack).Concat(_carried);

    /// <summary>Base stats plus the bonuses of everything equipped (mods on the belt and buckle too).</summary>
    public Stats EffectiveStats => EquippedCards.Concat(_mods).Concat(BeltTools).Aggregate(BaseStats, (stats, card) =>
        stats + card.Modifiers + (HasSetBonus(card.Definition) ? card.Definition.Set!.Modifiers : Stats.None));

    /// <summary>Tools on the belt that work from there (Lockpicks): their bonuses count as if in hand.</summary>
    public IEnumerable<CardInstance> BeltTools => _belt.Where(card => card.Definition.BeltsFirst);

    /// <summary>Whether the card's set bonus is on: it has one, and its partner is equipped too.</summary>
    public bool HasSetBonus(CardDefinition card) =>
        card.Set is { } set && EquippedCards.Any(other => other.Definition.Id == set.With);

    public int MaxHearts => Math.Max(1, EffectiveStats.Hearts);

    /// <summary>Hearts healing can bring them back up to: all of them, less any Black Hearts.</summary>
    public int HealableTo => Math.Max(0, MaxHearts - Conditions[Condition.BlackHeart]);

    /// <summary>Hearts healing could give back now.</summary>
    public int HeartsToHeal => Math.Max(0, HealableTo - Hearts);

    /// <summary>The main-hand weapon, if any.</summary>
    public CardInstance? MainWeapon => _equipment.GetValueOrDefault(EquipSlot.MainHand);

    /// <summary>A thrown weapon (the Boomerang) in the off hand, if any: it reaches further than the main hand may.</summary>
    public CardInstance? ThrownOffHand =>
        _equipment.TryGetValue(EquipSlot.OffHand, out CardInstance? off) && off != MainWeapon && off.Definition.Type == CardType.Weapon ? off : null;

    /// <summary>
    /// Furthest reach of the Attack action: the main-hand weapon's range (melee when unarmed, or with an
    /// unloaded crossbow), or an off-hand weapon's, or how far a Dagger can be thrown. See <c>GameSession.CanHeroReach</c>
    /// for whether a tile can be hit.
    /// </summary>
    public int AttackRange => new[] { MainWeapon, ThrownOffHand }
        .Select(card => card is null ? 1 : Math.Max(card.Unloaded || !Loaded(card) ? 1 : card.Definition.AttackRange, card.Definition.ThrownRange))
        .Max();

    /// <summary>Wearing gear that swims (the Diving Helm): water is no harder than floor, and deep water needs no effort to stay afloat.</summary>
    public bool Swims => _equipment.Values.Any(card => card.Definition.Swims);

    /// <summary>Gold value of the treasure carried; decides the winner.</summary>
    public int TreasureValue =>
        Gold + AllCards.Where(card => card.Definition.Type == CardType.Treasure).Sum(card => card.Definition.Value * card.Count);

    /// <summary>The stack this hero already holds of a stackable card (anywhere), which more copies join.</summary>
    public CardInstance? StackOf(CardDefinition definition) =>
        // A torch in hand is one torch: more join the stack carried, not the hand.
        definition.IsStackable ? AllCards.FirstOrDefault(card => card.Definition == definition && !(definition.Torch && EquippedCards.Contains(card))) : null;

    /// <summary>Whether the hero can take this card: it joins a stack they hold (with room in it), or there's room in the backpack. A Lucky Clover while there's room for its point.</summary>
    public bool CanCarry(CardDefinition definition) =>
        definition.Clover ? CloverLuck < GameRules.MaxCloverLuck
        : definition.CarryOnly ? CanCarryInArms(definition)
        : StackOf(definition) is { } stack ? stack.Count < StackLimit(definition)
        : _backpack.Count < GameRules.BackpackCapacity;

    /// <summary>The most one stack of this card can hold: its own limit (10 Rocks), or a mod's that holds them (100 in a Rock Bag); no limit for most.</summary>
    public int StackLimit(CardDefinition definition) =>
        _mods.Where(mod => mod.Definition.Holds == definition.Id).Select(mod => mod.Definition.HoldsUpTo).DefaultIfEmpty(0).Max() is > 0 and var held ? held
        : definition.MaxStack > 0 ? definition.MaxStack
        : int.MaxValue;

    /// <summary>How many more of this card the hero could take into the stack they hold of it (or a new one).</summary>
    public int RoomFor(CardDefinition definition) =>
        definition.Clover ? GameRules.MaxCloverLuck - CloverLuck
        : !CanCarry(definition) ? 0 : StackLimit(definition) - (StackOf(definition)?.Count ?? 0);

    /// <summary>
    /// What a weapon that shoots (the Sling) would load next: its ammo in the off hand, or on the belt when a
    /// mod there holds it (the Rock Bag). Null with none to hand, or for a weapon that doesn't shoot.
    /// </summary>
    public CardInstance? AmmoFor(CardInstance weapon) =>
        weapon.Definition.Ammo is not { } ammo ? null
        : _equipment.GetValueOrDefault(EquipSlot.OffHand) is { } off && off.Definition.Id == ammo ? off
        : _mods.Any(mod => mod.Definition.Holds == ammo) ? _belt.FirstOrDefault(card => card.Definition.Id == ammo)
        : null;

    /// <summary>A weapon ready to strike: anything but one that shoots with nothing to shoot.</summary>
    public bool Loaded(CardInstance weapon) => weapon.Definition.Ammo is null || AmmoFor(weapon) is not null;

    public (CardInstance Card, CardLocation Location)? FindCard(int cardId)
    {
        if (EquippedCards.Concat(_mods).FirstOrDefault(card => card.Id == cardId) is { } equipped)
        {
            return (equipped, CardLocation.Equipped);
        }

        if (_carried.FirstOrDefault(card => card.Id == cardId) is { } carried)
        {
            return (carried, CardLocation.Carried);
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

    /// <summary>
    /// The slots equipping <paramref name="card"/> would fill: the <paramref name="hand"/> chosen for a
    /// card that goes in either (the Boomerang), else its own slot, or its other one when its own is
    /// taken and the other is free.
    /// </summary>
    public IReadOnlyList<EquipSlot> SlotsToFill(CardDefinition card, EquipSlot? hand = null) =>
        hand is { } chosen && !card.TwoHanded && (chosen == card.Slot || chosen == card.AltSlot) ? [chosen]
        : card.AltSlot is { } alt && card.Slot is { } own && _equipment.ContainsKey(own) && !_equipment.ContainsKey(alt) ? [alt]
        : SlotsFor(card);

    /// <summary>Cards that equipping <paramref name="card"/> (in <paramref name="hand"/>, if chosen) would push out of their slots.</summary>
    public IReadOnlyList<CardInstance> CardsDisplacedBy(CardDefinition card, EquipSlot? hand = null) =>
        SlotsToFill(card, hand)
            .Select(slot => _equipment.GetValueOrDefault(slot))
            .OfType<CardInstance>()
            .Distinct()
            .ToList();

    /// <summary>The slot an equipped card is in, or null if it isn't equipped.</summary>
    public EquipSlot? SlotOf(CardInstance card) =>
        _equipment.Where(entry => entry.Value == card).Select(entry => (EquipSlot?)entry.Key).FirstOrDefault();

    internal void Equip(CardInstance card, EquipSlot? hand = null)
    {
        foreach (EquipSlot slot in SlotsToFill(card.Definition, hand))
        {
            _equipment[slot] = card;
        }
    }

    /// <summary>Puts a mod on the belt or the buckle (see <see cref="CheckMod"/>).</summary>
    internal void AddMod(CardInstance card) => _mods.Add(card);

    /// <summary>Puts a card in the hero's arms, or into the stack of it the hero already holds. Returns the card that holds it.</summary>
    internal CardInstance AddToCarried(CardInstance card) => JoinStack(card) ?? Add(_carried, card);

    /// <summary>
    /// Takes the belt off whole: its buckle and mods and the cards on it go with it, packed onto it
    /// (see <see cref="CardInstance.Packed"/>). Returns the belt, or null if beltless.
    /// </summary>
    internal CardInstance? Unbuckle()
    {
        if (_equipment.GetValueOrDefault(EquipSlot.Belt) is not { } belt)
        {
            return null;
        }

        List<CardInstance> attached = [.. _equipment.GetValueOrDefault(EquipSlot.Buckle) is { } buckle ? [buckle] : Array.Empty<CardInstance>(), .. _mods];
        belt.Packed = new PackedBelt(attached, [.. _belt]);
        _equipment.Remove(EquipSlot.Belt);
        _equipment.Remove(EquipSlot.Buckle);
        _mods.Clear();
        _belt.Clear();
        return belt;
    }

    /// <summary>Buckles on a belt unbuckled whole: it's worn again, with its buckle, mods and cards back where they were.</summary>
    internal void BuckleOn(CardInstance belt)
    {
        PackedBelt packed = belt.Packed!;
        belt.Packed = null;
        _equipment[EquipSlot.Belt] = belt;
        foreach (CardInstance card in packed.Attached)
        {
            if (card.Definition.ModFor is not null)
            {
                _mods.Add(card);
            }
            else
            {
                _equipment[EquipSlot.Buckle] = card;
            }
        }

        _belt.AddRange(packed.Held);
    }

    /// <summary>Wears a card: a mod on the belt or buckle, anything else in its slot.</summary>
    internal void Wear(CardInstance card)
    {
        if (card.Definition.ModFor is not null)
        {
            _mods.Add(card);
        }
        else
        {
            Equip(card);
        }
    }

    /// <summary>Puts a card on the belt, or into the stack of it the hero already holds. Returns the card that holds it.</summary>
    internal CardInstance AddToBelt(CardInstance card) => JoinStack(card) ?? Add(_belt, card);

    /// <summary>Puts a card in the backpack, or into the stack of it the hero already holds. Returns the card that holds it.</summary>
    internal CardInstance AddToBackpack(CardInstance card) => JoinStack(card) ?? Add(_backpack, card);

    /// <summary>
    /// A card picked up or handed over: a Medicine Bag goes on the belt when there's room, a carry-only
    /// card (the Relic) in the hero's arms, the rest in the backpack. (Nothing else is carried unasked:
    /// arms full mean no Rumble.)
    /// </summary>
    internal CardInstance AddLoot(CardInstance card) =>
        card.Definition.Clover ? TakeClover(card)
        : card.Definition.CarryOnly ? AddToCarried(card)
        : card.Definition.Belts && (_belt.Count < BeltCapacity || _belt.Any(held => held.Definition == card.Definition)) ? AddToBelt(card)
        : AddToBackpack(card);

    /// <summary>A Lucky Clover picked up: its points, up to the most a hero can hold. The card itself goes nowhere.</summary>
    private CardInstance TakeClover(CardInstance card)
    {
        CloverLuck = Math.Min(GameRules.MaxCloverLuck, CloverLuck + card.Count);
        return card;
    }

    private static CardInstance Add(List<CardInstance> place, CardInstance card)
    {
        place.Add(card);
        return card;
    }

    private CardInstance? JoinStack(CardInstance card)
    {
        if (StackOf(card.Definition) is not { } stack || stack == card)
        {
            return null;
        }

        stack.Absorb(card);
        return stack;
    }

    /// <summary>Removes the card from wherever it is held.</summary>
    internal void Remove(CardInstance card)
    {
        foreach (EquipSlot slot in _equipment.Where(entry => entry.Value == card).Select(entry => entry.Key).ToList())
        {
            _equipment.Remove(slot);
        }

        _belt.Remove(card);
        _backpack.Remove(card);
        _mods.Remove(card);
        _carried.Remove(card);
    }

    /// <summary>Trims current hearts after gear that granted hearts is removed.</summary>
    internal void ClampHearts() => Hearts = Math.Min(Hearts, MaxHearts);

    /// <summary>
    /// A hero standing in for a saved character outside a run (the Woarld Map's Party menu), for their
    /// character sheet: their stats, gear where they keep it, spells, gold and Power Dice.
    /// <paramref name="cardKeys"/> gives each card's key in the character's lists, by its id here.
    /// </summary>
    public static Hero Preview(Character character, ContentCatalog content, int? hearts, out IReadOnlyDictionary<int, string> cardKeys)
    {
        var hero = new Hero(0, 0, character.Name, character.BaseStats, "", default, character.Id, character.Portrait, character.Appearance)
        {
            StartXp = character.Xp,
            Gold = character.Gold,
            Skills = character.Allocation.Skills,
        };
        var keys = new Dictionary<int, string>();
        int nextId = 1;
        CardInstance Make(string key)
        {
            CardInstance card = CardInstance.FromKey(key, content, () => nextId++, null);
            keys[card.Id] = key;
            return card;
        }

        foreach (string key in character.Equipped)
        {
            hero.Wear(Make(key));
        }

        foreach (string key in character.Belt)
        {
            hero._belt.Add(Make(key));
        }

        foreach (string key in character.Backpack)
        {
            hero._backpack.Add(Make(key));
        }

        foreach (string key in character.Carried)
        {
            hero._carried.Add(Make(key));
        }

        foreach (string spellId in character.Spells)
        {
            hero.Learn(content.GetSpell(spellId));
        }

        hero.Hearts = Math.Clamp(hearts ?? hero.MaxHearts, 0, hero.MaxHearts);
        hero.LuckRemaining = hero.EffectiveStats.Luck;
        hero.Power.Banked = Math.Clamp(character.PowerDice, 0, hero.Power.Capacity);
        cardKeys = keys;
        return hero;
    }

    /// <summary>Puts a card the hero starts the run with into the best free place.</summary>
    internal bool TryPlaceStartingCard(CardInstance card)
    {
        CardDefinition definition = card.Definition;
        if (JoinStack(card) is not null)
        {
            return true;
        }

        if (definition.CarryOnly)
        {
            if (!CanCarryInArms(definition))
            {
                return false;
            }

            _carried.Add(card);
        }
        else if (card.Packed is not null)
        {
            // A belt unbuckled whole stays that way, in the bag (or in the arms).
            if (_backpack.Count < GameRules.BackpackCapacity)
            {
                _backpack.Add(card);
            }
            else if (CanCarryInArms(definition))
            {
                _carried.Add(card);
            }
            else
            {
                return false;
            }
        }
        else if (definition.ModFor is not null && CheckMod(definition) is null)
        {
            _mods.Add(card);
        }
        else if (definition.Belts && _belt.Count < BeltCapacity)
        {
            // Belt tools ride on the belt, a torch too (unlit, till it's taken in hand).
            _belt.Add(card);
        }
        else if (definition.Slot is not null && CardsDisplacedBy(definition).Count == 0)
        {
            Equip(card);
        }
        else if (CanBelt(definition) && _belt.Count < BeltCapacity)
        {
            // A spare weapon, shield or tool rides on the belt, where it can be drawn even in combat.
            _belt.Add(card);
        }
        else if (_backpack.Count < GameRules.BackpackCapacity)
        {
            _backpack.Add(card);
        }
        else if (CanCarryInArms(definition))
        {
            // No room anywhere else: it's carried.
            _carried.Add(card);
        }
        else
        {
            return false;
        }

        return true;
    }

    /// <summary>What can ride on a belt: single-use cards, belt tools and hand gear.</summary>
    public static bool CanBelt(CardDefinition definition) =>
        !definition.CarryOnly && (definition.Usage == CardUsage.SingleUse || definition.Belts || IsHandGear(definition));

    /// <summary>Held in a hand: a weapon, shield or tool, which can go on the belt.</summary>
    public static bool IsHandGear(CardDefinition definition) => definition.Slot is EquipSlot.MainHand or EquipSlot.OffHand;
}
