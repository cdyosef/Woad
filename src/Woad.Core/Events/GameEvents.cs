using Woad.Core.Content;
using Woad.Core.Characters;
using Woad.Core.Dice;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Events;

/// <summary>
/// Something that happened in the run, in order. The game animates these; a future server
/// would broadcast them. Ids refer to heroes, monsters and cards, which share one id space.
/// </summary>
public abstract record GameEvent;

public sealed record RoundStarted(int Round) : GameEvent;

public sealed record TurnStarted(int HeroId) : GameEvent;

public sealed record TurnEnded(int HeroId) : GameEvent;

/// <summary>
/// A move began: the dice rolled, the Speed added (<paramref name="Bonus"/>) and the steps it gives.
/// The last <paramref name="PowerDice"/> faces of the roll are Power Dice.
/// </summary>
public sealed record MovementRolled(int HeroId, DiceRoll Roll, int Bonus, int MovementPoints, int PowerDice = 0) : GameEvent;

/// <summary>A hero cast a spell at <paramref name="TargetIds"/>, spending <paramref name="Power"/> banked Power Dice.</summary>
public sealed record SpellCast(int HeroId, string SpellId, int Power, IReadOnlyList<int> TargetIds) : GameEvent;

/// <summary>Extra dice a spell rolls against only some of its targets (Quake's against those on rubble), added to their total.</summary>
public sealed record ExtraDiceRolled(int HeroId, DiceRoll Roll, IReadOnlyList<DieType> DieTypes, IReadOnlyList<int> TargetIds) : GameEvent;

/// <summary>A hero spent a scroll trying to learn its spell (the roll follows).</summary>
public sealed record SpellStudied(int HeroId, int CardId, string SpellId) : GameEvent;

/// <summary>A scroll went off in a hero's hands as they failed to learn it: it attacks everyone around them.</summary>
public sealed record Backfired(int HeroId, string SpellId, IReadOnlyList<int> TargetIds) : GameEvent;

/// <summary>A spell's dice rolled for something other than an attack: a boon, or ice.</summary>
public sealed record SpellRolled(int HeroId, DiceRoll Roll, IReadOnlyList<DieType> DieTypes) : GameEvent;

/// <summary>Ice laid on tiles for some rounds (water under it frozen over).</summary>
public sealed record IceLaid(string RoomId, IReadOnlyList<GridPosition> Tiles, int Rounds) : GameEvent;

/// <summary>Ice that melted away (water under it flowing again).</summary>
public sealed record IceMelted(string RoomId, IReadOnlyList<GridPosition> Tiles) : GameEvent;

/// <summary>Someone stepped onto ice and slid on a tile.</summary>
public sealed record Slid(int EntityId, GridPosition From, GridPosition To) : GameEvent;

/// <summary>A hero gained a boon for <paramref name="Rounds"/> rounds (<paramref name="Amount"/>: Shining Light's Defense).</summary>
public sealed record BoonGained(int HeroId, Boon Boon, int Rounds, int Amount) : GameEvent;

/// <summary>A hero's boon ran out (or Shadowbind broke).</summary>
public sealed record BoonEnded(int HeroId, Boon Boon) : GameEvent;

/// <summary>A brazier was lit.</summary>
public sealed record BrazierLit(string RoomId, GridPosition Position) : GameEvent;

/// <summary>A hero learned a spell: a basic one with a rank of Magic, or one from a scroll.</summary>
public sealed record SpellLearned(int HeroId, string SpellId) : GameEvent;

/// <summary>Sixes earned Power Dice the hero now chooses to add or bank (<paramref name="Unsettled"/> in all).</summary>
public sealed record PowerDiceEarned(int HeroId, int Unsettled) : GameEvent;

/// <summary>
/// A Power Die was rolled into a hero's pending attack or move (by choice, or because their bank was
/// full). <paramref name="Total"/> is the attack's new total, or the move's new steps.
/// </summary>
public sealed record PowerDieAdded(int HeroId, int Face, DiceRoll Roll, int Total) : GameEvent;

/// <summary>Power Dice went into a hero's or monster's bank, which now holds <paramref name="Banked"/>.</summary>
public sealed record PowerDiceBanked(int OwnerId, int Count, int Banked) : GameEvent;

/// <summary>Banked Power Dice were rolled into an attack, move or defense; the bank now holds <paramref name="Banked"/>.</summary>
public sealed record PowerDiceSpent(int OwnerId, int Count, int Banked) : GameEvent;

/// <summary>A Luck reroll. <paramref name="MovementPoints"/> is the new total when it was a movement die.</summary>
public sealed record DieRerolled(int HeroId, int DieIndex, int OldFace, int NewFace, DiceRoll Roll, int? MovementPoints) : GameEvent;

public sealed record HeroMoved(int HeroId, IReadOnlyList<GridPosition> Path, int MovementPointsLeft) : GameEvent;

public sealed record HeroEnteredRoom(int HeroId, string FromRoomId, string RoomId, GridPosition Position, bool FirstVisit) : GameEvent;

/// <summary>
/// A hero or monster rolled an attack against one or more targets. <paramref name="Bonus"/> is the
/// weapon's fixed value, and <paramref name="Total"/> the roll plus it. The last
/// <paramref name="PowerDice"/> faces of the roll are Power Dice (banked ones spent, or a monster's sixes).
/// A <paramref name="Shove"/> pushes its target instead of hurting it.
/// </summary>
public sealed record AttackRolled(int AttackerId, DiceRoll Roll, int Bonus, int Total, IReadOnlyList<int> TargetIds, int PowerDice = 0, bool Shove = false) : GameEvent
{
    /// <summary>Of the faces, after the Rumble dice: the weapon's own dice (the Longsword's d6). Power Dice come last.</summary>
    public int WeaponDice { get; init; }

    /// <summary>Not a blow but a Ghoul's miasma, breathed on whoever started their turn beside it.</summary>
    public bool Miasma { get; init; }

    /// <summary>What each face is made of (fire, ice...), in order; Plain past its end. A typed die's 6 rolled another in.</summary>
    public IReadOnlyList<DieType> DieTypes { get; init; } = [];

    /// <summary>A flung chain (the Iron Warden's), which drags and shackles whoever it hits.</summary>
    public bool Chain { get; init; }

    /// <summary>A hero's weapon swung or shot (its card id), for how the attack looks; null bare-handed or for a spell.</summary>
    public string? WeaponId { get; init; }
}

/// <summary>A tool's or weapon's die came up 6: that many more dice rolled straight into the roll (as Power Dice).</summary>
public sealed record CritRolled(int HeroId, int Dice) : GameEvent;

/// <summary>A lock-picking roll failed with a 1 showing: one of the hero's lockpicks snapped (<paramref name="Left"/> remain).</summary>
public sealed record LockpickSnapped(int HeroId, int CardId, int Left) : GameEvent;

/// <summary>
/// A target answered an attack: its defense dice plus its armor make <paramref name="Total"/>. The
/// last <paramref name="PowerDice"/> faces are Power Dice it added (sixes with a full bank, or banked
/// dice spent to block).
/// </summary>
/// <param name="Armor">The fixed part: armor, plus any Block spent.</param>
public sealed record DefenseRolled(int DefenderId, DiceRoll Roll, int Armor, int Total, int PowerDice = 0) : GameEvent
{
    /// <summary>Block a hero spent on it (part of <see cref="Armor"/>).</summary>
    public int Block { get; init; }

    /// <summary>Allies' Guard Dice spent on it, rolled in right after the base die.</summary>
    public int GuardDice { get; init; }

    /// <summary>A hero's extra Defense dice spent on it, rolled in after the base die (and before any Power Dice).</summary>
    public int ExtraDice { get; init; }

    /// <summary>A last-chance die (the Lucky Helmet's) was rolled in at the end.</summary>
    public bool LastChance { get; init; }

    /// <summary>Faces held for it (Parry, Guard), added right after the base die.</summary>
    public int Held { get; init; }
}

/// <summary>
/// A hero bull rushed from <paramref name="From"/> to <paramref name="To"/>: <paramref name="Roll"/> and
/// <paramref name="Bonus"/> (the Wend bonus) made the charge <paramref name="Length"/> tiles. It reached
/// <paramref name="MonsterId"/> with <paramref name="Leftover"/> tiles to spare (they go on the blow), or
/// no monster (null). A <see cref="HeroMoved"/> follows when they went anywhere.
/// </summary>
public sealed record BullRushed(int HeroId, DiceRoll Roll, int Bonus, int Length, GridPosition From, GridPosition To, int? MonsterId, int Leftover) : GameEvent;

/// <summary>A hero rolled a die and held it for a defense: their own Parry, or Guard for themselves or an ally beside them.</summary>
public sealed record DefenseHeld(int HeroId, int ForHeroId, Skill Skill, DiceRoll Roll) : GameEvent;

/// <summary>A hero spent a Rumble die on High Alert: it rolls in with their next search this turn.</summary>
public sealed record WentOnHighAlert(int HeroId) : GameEvent;

/// <summary>An attack's result: 1 heart when its total beat the defense, otherwise 0 (blocked).</summary>
public sealed record Damaged(int TargetId, int Amount, int HeartsLeft) : GameEvent;

public sealed record MonsterDefeated(int MonsterId, int? KillerHeroId) : GameEvent;

public sealed record MonsterCollapsed(int MonsterId) : GameEvent;

public sealed record MonsterReassembled(int MonsterId, int Hearts) : GameEvent;

public sealed record MonsterSpawned(int MonsterId, string MonsterDefinitionId, string RoomId, GridPosition Position, int Hearts) : GameEvent
{
    /// <summary>Where it came from: a split (the default), a mimic waking or the dead raised.</summary>
    public SpawnCause Cause { get; init; }
}

public enum SpawnCause
{
    Split,
    Mimic,
    Raised,

    /// <summary>A Sentient Shrub sprang up where plants grew (a monster's Life dice).</summary>
    Grown,
}

public sealed record MonsterMoved(int MonsterId, IReadOnlyList<GridPosition> Path) : GameEvent;

/// <summary>A monster rolled banked Power Dice (<paramref name="Faces"/>) into its move, for <paramref name="Moves"/> in all.</summary>
public sealed record MonsterSurged(int MonsterId, IReadOnlyList<int> Faces, int Moves) : GameEvent;

public sealed record MonsterFrozen(int MonsterId, int Turns) : GameEvent;

public sealed record MonsterTurnSkipped(int MonsterId) : GameEvent;

public sealed record HeroHealed(int HeroId, int Amount, int Hearts) : GameEvent;

/// <summary>A hero hit 0 hearts and is Keeling Over where they lie. <paramref name="Bane"/> is what brought them down.</summary>
public sealed record HeroKeeledOver(int HeroId, GridPosition Position, string Bane) : GameEvent;

/// <summary>
/// A hero Keeling Over rolled for their life: the Keel Die; the Power Die its 6 rolls or the Doom Die
/// its 1 rolls (<paramref name="BonusDie"/>); the banked Power Dice spent; and the Doom Dice allies
/// left them plus the creeping doom (<paramref name="CreepingDoom"/> of them, one more each roll after
/// the first). Then the marks gained and the totals.
/// </summary>
public sealed record KeelRolled(
    int HeroId,
    int Keel,
    int? BonusDie,
    IReadOnlyList<int> PowerFaces,
    IReadOnlyList<int> DoomFaces,
    int LifeGained,
    int DeathGained,
    int LifeMarks,
    int DeathMarks,
    int CreepingDoom = 0) : GameEvent;

/// <summary>An ally's Od Hand roll roused a hero Keeling Over: marks of life earned, and Doom Dice added to their next Keel roll.</summary>
public sealed record HeroRoused(int HeroId, int ByHeroId, int LifeGained, int DoomDiceAdded, int LifeMarks, int DoomDiceOwed) : GameEvent;

/// <summary>A monster's hit on a hero Keeling Over: a mark of death.</summary>
public sealed record KeelerStruck(int HeroId, int AttackerId, int DeathMarks) : GameEvent;

/// <summary>Three marks of life: the hero stands back up.</summary>
public sealed record HeroStoodUp(int HeroId, int Hearts) : GameEvent;

/// <summary>
/// Three marks of death: the hero died. A tombstone stands where they fell, holding everything they
/// found this run.
/// </summary>
public sealed record HeroDied(int HeroId, Tombstone Tombstone, IReadOnlyList<int> DroppedCardIds) : GameEvent;

public sealed record CardUsed(int HeroId, int CardId, GridPosition? Target) : GameEvent;

/// <summary>A single-use card was used up: removed, or one taken off its stack (<paramref name="Left"/> remain).</summary>
public sealed record CardConsumed(int HeroId, int CardId, int Left = 0) : GameEvent;

public sealed record CardEquipped(int HeroId, int CardId) : GameEvent;

public sealed record CardBelted(int HeroId, int CardId) : GameEvent;

public sealed record CardStowed(int HeroId, int CardId) : GameEvent;

public sealed record CardDropped(int HeroId, int CardId, GridPosition Position) : GameEvent
{
    /// <summary>How many were put down (part of a stack, or all of it).</summary>
    public int Count { get; init; } = 1;
}

/// <summary>
/// A chest gave cards (two from a chest that was locked); those that didn't fit in the backpack went
/// onto the hero's tile (<paramref name="AddedToBackpack"/> is false if any did).
/// </summary>
public sealed record ChestOpened(int HeroId, GridPosition Chest, IReadOnlyList<int> CardIds, bool AddedToBackpack) : GameEvent;

/// <summary>An Od Hand roll for a check, against <paramref name="Difficulty"/> (0 for a steal, a contest).</summary>
public sealed record OdHandRolled(int HeroId, OdHandCheckKind Kind, DiceRoll Roll, int Bonus, int Total, int Difficulty, int PowerDice = 0) : GameEvent
{
    /// <summary>Of the faces, after the Od Hand dice: the tools' own dice (a lockpick's d6). Power Dice come last.</summary>
    public int ToolDice { get; init; }
}

/// <summary>An Od Hand check fell short (or a steal was out-rolled); nothing happens.</summary>
public sealed record CheckFailed(int HeroId, OdHandCheckKind Kind) : GameEvent;

/// <summary>A door opened: with a key, the Boss Key, or a picked lock.</summary>
public sealed record DoorUnlocked(int HeroId, string RoomId, Direction Side, bool Picked, bool BossDoor) : GameEvent;

/// <summary>A chest's lock opened, with a key or picked. It still has to be opened.</summary>
public sealed record ChestUnlocked(int HeroId, GridPosition Chest, bool Picked) : GameEvent;

/// <summary>
/// A search turned things up: secret doors (by side), traps (tiles, chests and doors), stashes, and
/// secret ways through a wall dividing the room (<paramref name="Gates"/>, now open).
/// </summary>
public sealed record SecretsFound(
    int HeroId,
    string RoomId,
    IReadOnlyList<Direction> Doors,
    IReadOnlyList<GridPosition> Traps,
    IReadOnlyList<GridPosition> Stashes,
    IReadOnlyList<GridPosition>? Gates = null) : GameEvent;

public sealed record TrapDisarmed(int HeroId, GridPosition Target) : GameEvent;

/// <summary>A hero set off a trap at <paramref name="Position"/> (its tile, chest or doorway).</summary>
public sealed record TrapSprung(int HeroId, TrapKind Kind, GridPosition Position) : GameEvent;

/// <summary>A hero dropped through a trap door into another room (<paramref name="Jumped"/> on purpose, unhurt).</summary>
public sealed record HeroFell(int HeroId, string FromRoomId, string RoomId, int Floor, GridPosition Position, bool FirstTimeOnFloor, bool Jumped) : GameEvent;

/// <summary>Keys appeared on the floor: dropped by a monster, from a cleared room or a found stash.</summary>
public sealed record KeysAppeared(string RoomId, GridPosition Position, int Count) : GameEvent;

/// <summary>A hero gained keys (picked up, from a pot or stolen); <paramref name="Total"/> is how many they carry.</summary>
public sealed record KeysFound(int HeroId, int Count, int Total) : GameEvent;

/// <summary>A key opened a lock; <paramref name="Left"/> is how many the hero still carries.</summary>
public sealed record KeyUsed(int HeroId, int Left) : GameEvent;

public sealed record BossKeyFound(int HeroId) : GameEvent;

/// <summary>The Boss Key fell to the floor (its carrier was downed).</summary>
public sealed record BossKeyDropped(string RoomId, GridPosition Position) : GameEvent;

/// <summary>A bomb broke a cracked wall: a block inside the room, or the one in a doorway (<paramref name="Door"/>).</summary>
public sealed record WallBroken(string RoomId, GridPosition Position, Direction? Door) : GameEvent;

/// <summary>A steal worked: a card (<paramref name="CardId"/>) or a key changed hands.</summary>
public sealed record Stole(int ThiefId, int VictimId, int? CardId, bool Key) : GameEvent;

public sealed record LootPickedUp(int HeroId, IReadOnlyList<int> CardIds) : GameEvent
{
    /// <summary>For a single card that joined a stack the hero already held: that stack's card.</summary>
    public int? HolderId { get; init; }
}

public sealed record EnemyPhaseStarted(int Round) : GameEvent;

public sealed record EnemyPhaseEnded(int Round) : GameEvent;

public sealed record RunEnded(IReadOnlyList<int> WinnerHeroIds) : GameEvent;

/// <summary>A hero went down the stairs. <paramref name="FirstTimeOnFloor"/> means Luck and charges were restored.</summary>
public sealed record HeroDescended(int HeroId, string FromRoomId, string RoomId, int Floor, GridPosition Position, bool FirstTimeOnFloor) : GameEvent;

/// <summary>A hero climbed the stairs back up to the floor above.</summary>
public sealed record HeroAscended(int HeroId, string FromRoomId, string RoomId, int Floor, GridPosition Position) : GameEvent;

public sealed record HeroEscaped(int HeroId, int TreasureValue) : GameEvent;

/// <summary>A hero got away from a fight on the Woarld Map; the monsters are still there.</summary>
public sealed record HeroRetreated(int HeroId) : GameEvent;

/// <summary>A retreat failed: the hero bolts in a panic (their Wend dice, <paramref name="Roll"/>), then falls flat.</summary>
public sealed record Routed(int HeroId, DiceRoll Roll, int Steps) : GameEvent;

/// <summary>The last monster in a room was defeated. A trap room's doors open again.</summary>
public sealed record RoomCleared(string RoomId) : GameEvent;

/// <summary>A pot broke. It held a card (<paramref name="CardId"/>), a heart (<paramref name="HeartsRestored"/>), or nothing.</summary>
public sealed record PotSmashed(int HeroId, GridPosition Pot, int? CardId, bool AddedToBackpack, int HeartsRestored) : GameEvent;

/// <summary>A defeated monster left a card on the floor where it fell.</summary>
public sealed record LootDropped(string RoomId, GridPosition Position, int CardId) : GameEvent;

/// <summary>A hero earned XP (from a kill, a new floor or escaping).</summary>
public sealed record XpGained(int HeroId, int Amount, int Total) : GameEvent;

/// <summary>A player gave their companion a standing order.</summary>
public sealed record CompanionOrdered(int OwnerId, int CompanionId, CompanionOrder Order) : GameEvent;

/// <summary>A hero walked, fell or was shoved into a pit, or a monster was. A bottomless pit has no room below.</summary>
public sealed record FellIntoPit(int EntityId, string RoomId, GridPosition Pit, bool Bottomless) : GameEvent;

/// <summary>A hero who fell into a bottomless pit climbed back out, or a monster shoved into deep water scrambled out.</summary>
public sealed record ClimbedOut(int EntityId, GridPosition Position) : GameEvent;

/// <summary>A hero's shove pushed a monster (or, with friendly fire on, a hero) from one tile to the next.</summary>
public sealed record Shoved(int HeroId, int TargetId, GridPosition From, GridPosition To) : GameEvent;

/// <summary>A shove turned a shelled monster onto its back: it's prone, and its shell gives no armor until it gets up.</summary>
public sealed record Flipped(int MonsterId) : GameEvent;

/// <summary>A card (or <paramref name="Count"/> of a stack) passed from one ally to another.</summary>
public sealed record CardTraded(int FromHeroId, int ToHeroId, int CardId, int Count) : GameEvent;

public sealed record GoldTraded(int FromHeroId, int ToHeroId, int Amount) : GameEvent;

/// <summary>A key (or the Boss Key) passed from one ally to another.</summary>
public sealed record KeyTraded(int FromHeroId, int ToHeroId, bool BossKey) : GameEvent;

/// <summary>A companion's owner equipped, belted or stowed one of its cards.</summary>
public sealed record CompanionOutfitted(int OwnerId, int CompanionId, int CardId, Commands.OutfitAction Action) : GameEvent;

/// <summary>A monster's tongue or hook dragged a hero it hit to its side.</summary>
public sealed record Pulled(int MonsterId, int HeroId, GridPosition From, GridPosition To) : GameEvent;

/// <summary>A shove didn't beat the target's defense: it stands its ground.</summary>
public sealed record ShoveResisted(int HeroId, int TargetId) : GameEvent;

/// <summary>A monster was shoved onto a trap, which went off (and is now revealed).</summary>
public sealed record MonsterTrapped(int MonsterId, TrapKind Kind, GridPosition Position) : GameEvent;

/// <summary>A monster fell through a pit or trap door into the room below.</summary>
public sealed record MonsterFell(int MonsterId, string FromRoomId, string RoomId, GridPosition Position) : GameEvent;


/// <summary>
/// A hero leapt from <paramref name="From"/> toward <paramref name="Target"/>; the roll, halved, carried
/// them to <paramref name="Landing"/> (short of the target if the roll fell short, or where they started on a 1).
/// </summary>
public sealed record Leaped(int HeroId, GridPosition From, GridPosition Target, int Roll, GridPosition Landing) : GameEvent;

/// <summary>A hero ended their turn in deep water and spent a Rumble die to stay afloat.</summary>
public sealed record StayedAfloat(int HeroId) : GameEvent;

/// <summary>A hero ended their turn in deep water with no Rumble die to spare: they wash back to dry footing (and lose a heart).</summary>
public sealed record Drowned(int HeroId, GridPosition From, GridPosition To) : GameEvent;

/// <summary>A hero opened a locked gate inside a room: with a key, or <paramref name="Picked"/> with Od Hand.</summary>
public sealed record GateOpened(int HeroId, string RoomId, GridPosition Gate, bool Picked) : GameEvent;

public enum FireOutCause
{
    /// <summary>Its last fire die came up 1.</summary>
    Snuffed,

    /// <summary>It went into water.</summary>
    Water,

    /// <summary>An ally (or they themselves) put it out with Aid and a Medicine Bag.</summary>
    Aided,

    /// <summary>A hero stopped, dropped and rolled.</summary>
    Rolled,
}

/// <summary>A hero or monster caught fire, or its fire grew: <paramref name="FireDice"/> now burn on it. <paramref name="ById"/> set it (0 if nobody).</summary>
public sealed record Ignited(int EntityId, int FireDice, int ById) : GameEvent;

/// <summary>
/// The start of a burning hero's or monster's turn: its fire dice rolled as an attack on it (the
/// defense and damage follow), then each 6 added a die and each 1 took one away.
/// </summary>
public sealed record FireBurned(int EntityId, DiceRoll Roll, int FireDiceLeft) : GameEvent;

/// <summary>A 6 on a burn roll spread the fire to a neighbor.</summary>
public sealed record FireSpread(int FromId, int ToId, int FireDice) : GameEvent;

/// <summary>The floor caught fire: <paramref name="Tiles"/> burn for <paramref name="Rounds"/> rounds.</summary>
public sealed record FloorIgnited(string RoomId, IReadOnlyList<GridPosition> Tiles, int Rounds) : GameEvent;

/// <summary>Burning floor burned out.</summary>
public sealed record FloorBurnedOut(string RoomId, IReadOnlyList<GridPosition> Tiles) : GameEvent;

/// <summary>A hero's or monster's fire went out.</summary>
public sealed record FireOut(int EntityId, FireOutCause Cause) : GameEvent;

/// <summary>A hero or monster lay down (or was knocked down): prone, or diving in deep water.</summary>
public sealed record WentProne(int EntityId, bool Diving) : GameEvent;

/// <summary>A hero beat the Icarus Wings: the die (and any Power Dice it rolled in), and the flight they have now.</summary>
public sealed record WingsBeat(int HeroId, IReadOnlyList<int> Faces, int FlightLeft) : GameEvent;

/// <summary>A hero flew along <paramref name="Path"/> on the Icarus Wings.</summary>
public sealed record Flew(int HeroId, IReadOnlyList<GridPosition> Path, int FlightLeft) : GameEvent;

/// <summary>A hero came down from the air.</summary>
public sealed record Landed(int HeroId, GridPosition Position) : GameEvent;

/// <summary>The Icarus Wings rolled a 1 and broke under a hero.</summary>
public sealed record WingsBroke(int HeroId, GridPosition Position) : GameEvent;

/// <summary>A prone hero or monster got back on its feet.</summary>
public sealed record GotUp(int EntityId) : GameEvent;

/// <summary>Coins went into a hero's purse: <paramref name="Amount"/> gold, <paramref name="Total"/> now carried.</summary>
public sealed record GoldFound(int HeroId, int Amount, int Total) : GameEvent;

/// <summary>Gold landed on the floor: coins a monster dropped or a stash held, or the purse of someone who died.</summary>
public sealed record GoldAppeared(string RoomId, GridPosition Position, int Amount) : GameEvent;

/// <summary>A hero or monster was stunned: its Stun and Powerful Stun stacks now (see <see cref="StunState"/>).</summary>
public sealed record Stunned(int EntityId, int Stacks, int PowerfulStacks, bool Powerful) : GameEvent;

/// <summary>The end of a stunned hero's or monster's turn: the stun wore off.</summary>
public sealed record StunWoreOff(int EntityId) : GameEvent;

/// <summary>Holy Water thrown on the undead: a lesser one destroyed outright, or a greater one about to take its Radiate dice.</summary>
/// <summary>A hero spent points on a skill.</summary>
public sealed record SkillLearned(int HeroId, Skill Skill, int PointsLeft) : GameEvent;

public sealed record Sanctified(int MonsterId, int ByHeroId, bool Destroyed) : GameEvent;

/// <summary>A Sineater Doll soaked up stacks of an ailment from someone; it now holds <paramref name="Held"/> in all.</summary>
public sealed record SoakedUp(int HeroId, int CardId, int TargetId, Ailment Ailment, int Stacks, int Held) : GameEvent;

/// <summary>A thrown Sineater Doll burst on a tile, laying what it held on everyone caught.</summary>
public sealed record DollBurst(int HeroId, GridPosition Tile, IReadOnlyList<int> Caught, IReadOnlyDictionary<Ailment, int> Held) : GameEvent;

/// <summary>A bomb landed in a room: thrown (lit), popped out of a bomb trap (lit, or unlit when it was disarmed).</summary>
public sealed record BombPlaced(string RoomId, int BombId, GridPosition Position, bool Lit, int? OwnerId) : GameEvent;

/// <summary>A lit bomb's fuse burned down a turn: it goes off at the end of its owner's next turn (1) or this one.</summary>
public sealed record FuseBurned(string RoomId, int BombId, int FuseTurns) : GameEvent;

/// <summary>A bomb went off. Its blast (rolled next) catches everyone within a tile, and breaks cracked walls and other bombs.</summary>
public sealed record BombExploded(string RoomId, int BombId, GridPosition Position) : GameEvent;

/// <summary>A blast's dice, compared one by one with each creature's defense; <paramref name="PowerDice"/> of them came from sixes.</summary>
public sealed record BlastRolled(string RoomId, GridPosition Position, DiceRoll Roll, int PowerDice) : GameEvent;

/// <summary>How many of a blast's dice got through to a creature: 1 hurts, 2 shoves, 3 knocks prone, 4 or more Powerful Stuns.</summary>
public sealed record BlastHit(int EntityId, int Hits) : GameEvent;

/// <summary>A hero struck a bomb with their Rumble dice: <paramref name="Chance"/> percent it went off.</summary>
public sealed record BombStruck(int HeroId, int BombId, int Chance, bool Exploded) : GameEvent;

/// <summary>A bomb was shoved a tile.</summary>
public sealed record BombMoved(string RoomId, int BombId, GridPosition From, GridPosition To) : GameEvent;

/// <summary>A lit bomb went out: defused with Od Hand, or doused in water. It lies there unlit.</summary>
public sealed record BombDefused(string RoomId, int BombId, bool Doused) : GameEvent;

/// <summary>A bomb fell down a pit: to the room below, or out of the game.</summary>
public sealed record BombFell(string RoomId, int BombId, string? ToRoomId) : GameEvent;

/// <summary>
/// A monster tried to steal from a hero: <paramref name="Hits"/> of its Od Hand dice beat the hero's own
/// roll, taking <paramref name="Gold"/> gold and perhaps a card (0 hits: caught empty-handed).
/// </summary>
public sealed record MonsterStole(int MonsterId, int HeroId, int Hits, int Gold, int? CardId) : GameEvent;

/// <summary>A mugging monster's hit took gold from a hero.</summary>
public sealed record Mugged(int MonsterId, int HeroId, int Gold) : GameEvent;

/// <summary>A chest was a mimic: it woke (opened, picked or found out by a search) and the chest is gone. Its <see cref="MonsterSpawned"/> comes just before.</summary>
/// <param name="HeroId">Who woke it by trying the chest (they're bitten), or 0 when a search found it out.</param>
public sealed record MimicWoke(int MonsterId, string RoomId, GridPosition Chest, int HeroId) : GameEvent;

/// <summary>A grabbing monster's hit took hold of a hero: they can't move until it lets go.</summary>
public sealed record Grabbed(int MonsterId, int HeroId) : GameEvent;

/// <summary>A heavy blow knocked a hero straight back (a tile, or more with a mighty blow's sixes).</summary>
public sealed record KnockedBack(int MonsterId, int HeroId, GridPosition From, GridPosition To) : GameEvent;

/// <summary>A hex drained one of a hero's Luck rerolls (with none left, it stunned them instead).</summary>
public sealed record Hexed(int MonsterId, int HeroId, bool Drained, int LuckLeft) : GameEvent;

/// <summary>A monster swallowed the loot on a tile it crawled over; it drops it where it falls.</summary>
public sealed record Engulfed(int MonsterId, GridPosition Tile, IReadOnlyList<int> CardIds, int Gold) : GameEvent;

/// <summary>A monster raised another from the dead beside it (just after its <see cref="MonsterSpawned"/>).</summary>
public sealed record MonsterRaised(int RaiserId, int MonsterId) : GameEvent;

/// <summary>A raised monster fell to dust when the one that raised it was defeated.</summary>
public sealed record Crumbled(int MonsterId) : GameEvent;

/// <summary>A hero's last-chance gear (the Lucky Helmet) rolled its die to save them, and broke.</summary>
public sealed record LastChanceUsed(int HeroId, int CardId) : GameEvent;

/// <summary>A hero's Guard Die rolled in for the ally beside them (its <see cref="DefenseRolled"/> comes just before).</summary>
public sealed record Guarded(int GuardianId, int HeroId) : GameEvent;

/// <summary>A hero reloaded their crossbow.</summary>
public sealed record WeaponReloaded(int HeroId, int CardId) : GameEvent;

/// <summary>A hero threw a dagger at a target; it lies at <paramref name="LandedAt"/> to be picked up.</summary>
public sealed record DaggerThrown(int HeroId, int CardId, string RoomId, GridPosition LandedAt) : GameEvent;

/// <summary>A cleaving blow (the Great Axe's 6) carried on into another monster beside the attacker.</summary>
public sealed record Cleaved(int HeroId, int MonsterId) : GameEvent;

/// <summary>A point went into a stat mid-run (a level gained in the dungeon); <paramref name="PointsLeft"/> remain.</summary>
public sealed record PointSpent(int HeroId, StatKind Stat, int Level, int PointsLeft) : GameEvent;

/// <summary>A player set how their companion manages its things.</summary>
public sealed record CompanionPolicySet(int OwnerId, int CompanionId, CompanionPolicy Policy) : GameEvent;

/// <summary>A 1 in a torch-lit attack burned the torch out: it's gone.</summary>
public sealed record TorchBurnedOut(int HeroId, int CardId) : GameEvent;

/// <summary>A monster's chain shackled a hero: no moving until the lock (<paramref name="Lock"/>+) is picked or a key frees them.</summary>
public sealed record HeroShackled(int MonsterId, int HeroId, int Lock) : GameEvent;

/// <summary>A hero's shackles came off: a key, or a picked lock (<paramref name="Picked"/>), by <paramref name="ById"/>.</summary>
public sealed record HeroUnshackled(int HeroId, int ById, bool Picked) : GameEvent;

/// <summary>A hero freed a chained merchant, with a key or a picked lock: they follow that hero now.</summary>
public sealed record MerchantFreed(int HeroId, int MerchantId, bool Picked) : GameEvent;

/// <summary>A freed merchant moved: up beside the hero they follow, or away from monsters.</summary>
public sealed record MerchantMoved(int MerchantId, IReadOnlyList<GridPosition> Path) : GameEvent;

/// <summary>A freed merchant followed the hero they follow into another room (or down a floor).</summary>
public sealed record MerchantFollowed(int MerchantId, string FromRoomId, string RoomId, GridPosition Position) : GameEvent;

/// <summary>A hero bought from (or sold to) a merchant for <paramref name="Gold"/>.</summary>
public sealed record MerchantTraded(int HeroId, int MerchantId, int CardId, int Gold, bool Bought) : GameEvent;

/// <summary>A hero stole from a merchant (<paramref name="CardId"/>), or was caught trying (null); what it cost them with the Guild and the Law.</summary>
public sealed record MerchantRobbed(int HeroId, int MerchantId, string? CardId, bool Caught, int GuildLost, int LawLost) : GameEvent;

/// <summary>A merchant was struck down (by a hero, a crime; or by a monster); their wares lie at <paramref name="DroppedAt"/>.</summary>
public sealed record MerchantSlain(int MerchantId, int KillerId, bool ByHero, GridPosition DroppedAt) : GameEvent;

/// <summary>A merchant left the dungeon alive with the party: rescued.</summary>
public sealed record MerchantRescued(int MerchantId, string Name, int FreedByHeroId) : GameEvent;

/// <summary>A regenerating monster (a troll) got a heart back.</summary>
public sealed record MonsterRegenerated(int MonsterId, int Hearts) : GameEvent;

/// <summary>A hero threw on the Coward's Cloak: Rumble and Od Hand dice turned to <paramref name="WendDice"/> Wend dice, and the monsters beside them slowed.</summary>
public sealed record CowardsFlight(int HeroId, int WendDice, IReadOnlyList<int> SlowedMonsterIds) : GameEvent;

/// <summary>
/// A typed die's token on a hero or monster changed (poison, freeze, blindness...): it now has
/// <paramref name="Count"/>, put there (or taken off) by <paramref name="ById"/> (0 for nobody: it wore off).
/// </summary>
public sealed record ConditionChanged(int EntityId, Condition Condition, int Count, int ById) : GameEvent;

/// <summary>
/// A gust (a Squall die) couldn't push them as far as it should: they slammed into a wall, or into
/// <paramref name="IntoId"/>, and the <paramref name="Roll"/> (a die for every tile it was stopped) hits them, and whoever stopped them too.
/// </summary>
public sealed record Slammed(int EntityId, int? IntoId, DiceRoll Roll) : GameEvent;

/// <summary>A buried hero or monster dug at the earth: every 3 of <paramref name="Roll"/> threw off a token, <paramref name="BuriedLeft"/> left.</summary>
public sealed record DugOut(int EntityId, DiceRoll Roll, int BuriedLeft) : GameEvent;

/// <summary>
/// The end of a poisoned hero's or monster's turn: they rolled <paramref name="Face"/> against their
/// tokens, losing a heart if it wasn't higher; a 6 threw one off (<paramref name="PoisonLeft"/> left).
/// </summary>
public sealed record PoisonRolled(int EntityId, int Face, bool LostHeart, int PoisonLeft) : GameEvent;

/// <summary>An Acid token ate in at the end of their turn: its die attacks them (the defense and damage follow).</summary>
public sealed record AcidBurned(int EntityId, DiceRoll Roll) : GameEvent;

/// <summary>A hero or monster is frozen solid: its turn passes with nothing done.</summary>
public sealed record FrozenSolid(int EntityId) : GameEvent;

/// <summary>A confused hero or monster lashed out at whoever was beside it and staggered off.</summary>
public sealed record Staggered(int EntityId) : GameEvent;

/// <summary>Plants grew on a tile (Life dice), for the heroes' side or the monsters'.</summary>
public sealed record PlantsGrew(string RoomId, GridPosition Tile, bool ForHeroes) : GameEvent;

/// <summary>
/// A companion was raised or grown in the fight (a zombie, a Sentient Shrub) and follows
/// <paramref name="OwnerId"/>'s player for the rest of the run; it looks like the monster <paramref name="Guise"/>.
/// </summary>
public sealed record HeroSummoned(int HeroId, int OwnerId, string Name, string Guise, string RoomId, GridPosition Position) : GameEvent;

/// <summary>Acid ate at a hero's gear: the card is worn by <paramref name="Wear"/>, giving <paramref name="ArmorLeft"/> armor until mended.</summary>
public sealed record GearCorroded(int HeroId, int CardId, int Wear, int ArmorLeft) : GameEvent;

/// <summary>A worn card was mended whole (an Autosmith Hammer).</summary>
public sealed record GearMended(int HeroId, int CardId) : GameEvent;
