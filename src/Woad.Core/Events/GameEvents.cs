using Woad.Core.Dice;
using Woad.Core.Grid;

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
/// </summary>
public sealed record AttackRolled(int AttackerId, DiceRoll Roll, int Bonus, int Total, IReadOnlyList<int> TargetIds, int PowerDice = 0) : GameEvent;

/// <summary>
/// A target answered an attack: its defense dice plus its armor make <paramref name="Total"/>. The
/// last <paramref name="PowerDice"/> faces are Power Dice it added (sixes with a full bank, or banked
/// dice spent to block).
/// </summary>
public sealed record DefenseRolled(int DefenderId, DiceRoll Roll, int Armor, int Total, int PowerDice = 0) : GameEvent;

/// <summary>An attack's result: 1 heart when its total beat the defense, otherwise 0 (blocked).</summary>
public sealed record Damaged(int TargetId, int Amount, int HeartsLeft) : GameEvent;

public sealed record MonsterDefeated(int MonsterId, int? KillerHeroId) : GameEvent;

public sealed record MonsterCollapsed(int MonsterId) : GameEvent;

public sealed record MonsterReassembled(int MonsterId, int Hearts) : GameEvent;

public sealed record MonsterSpawned(int MonsterId, string MonsterDefinitionId, string RoomId, GridPosition Position) : GameEvent;

public sealed record MonsterMoved(int MonsterId, IReadOnlyList<GridPosition> Path) : GameEvent;

public sealed record MonsterStunned(int MonsterId, int Turns) : GameEvent;

public sealed record MonsterTurnSkipped(int MonsterId) : GameEvent;

public sealed record HeroHealed(int HeroId, int Amount, int Hearts) : GameEvent;

/// <summary>A hero hit 0 hearts and dropped everything they found this run.</summary>
public sealed record HeroDowned(int HeroId, GridPosition Position, IReadOnlyList<int> DroppedCardIds) : GameEvent;

public sealed record CardUsed(int HeroId, int CardId, GridPosition? Target) : GameEvent;

/// <summary>A single-use card was used up and removed.</summary>
public sealed record CardConsumed(int HeroId, int CardId) : GameEvent;

public sealed record CardEquipped(int HeroId, int CardId) : GameEvent;

public sealed record CardBelted(int HeroId, int CardId) : GameEvent;

public sealed record CardStowed(int HeroId, int CardId) : GameEvent;

public sealed record CardDropped(int HeroId, int CardId, GridPosition Position) : GameEvent;

/// <summary>A chest gave a card; it went to the backpack, or onto the hero's tile when the backpack was full.</summary>
public sealed record ChestOpened(int HeroId, GridPosition Chest, int CardId, bool AddedToBackpack) : GameEvent;

public sealed record LootPickedUp(int HeroId, IReadOnlyList<int> CardIds) : GameEvent;

public sealed record EnemyPhaseStarted(int Round) : GameEvent;

public sealed record EnemyPhaseEnded(int Round) : GameEvent;

public sealed record RunEnded(IReadOnlyList<int> WinnerHeroIds) : GameEvent;

/// <summary>A hero went down the stairs. <paramref name="FirstTimeOnFloor"/> means Luck and charges were restored.</summary>
public sealed record HeroDescended(int HeroId, string FromRoomId, string RoomId, int Floor, GridPosition Position, bool FirstTimeOnFloor) : GameEvent;

public sealed record HeroEscaped(int HeroId, int TreasureValue) : GameEvent;

/// <summary>The last monster in a room was defeated. A trap room's doors open again.</summary>
public sealed record RoomCleared(string RoomId) : GameEvent;

/// <summary>A pot broke. It held a card (<paramref name="CardId"/>), a heart (<paramref name="HeartsRestored"/>), or nothing.</summary>
public sealed record PotSmashed(int HeroId, GridPosition Pot, int? CardId, bool AddedToBackpack, int HeartsRestored) : GameEvent;

/// <summary>A defeated monster left a card on the floor where it fell.</summary>
public sealed record LootDropped(string RoomId, GridPosition Position, int CardId) : GameEvent;

/// <summary>A hero earned XP (from a kill, a new floor or escaping).</summary>
public sealed record XpGained(int HeroId, int Amount, int Total) : GameEvent;
