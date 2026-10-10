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

public sealed record MovementRolled(int HeroId, DiceRoll Roll, int MovementPoints) : GameEvent;

/// <summary>A Luck reroll. <paramref name="MovementPoints"/> is the new total when it was a movement die.</summary>
public sealed record DieRerolled(int HeroId, int DieIndex, int OldFace, int NewFace, DiceRoll Roll, int? MovementPoints) : GameEvent;

public sealed record HeroMoved(int HeroId, IReadOnlyList<GridPosition> Path, int MovementPointsLeft) : GameEvent;

public sealed record HeroEnteredRoom(int HeroId, string FromRoomId, string RoomId, GridPosition Position, bool FirstVisit) : GameEvent;

/// <summary>Attack dice were rolled by a hero or monster against one or more targets.</summary>
public sealed record AttackRolled(int AttackerId, DiceRoll Roll, int Hits, IReadOnlyList<int> TargetIds) : GameEvent;

public sealed record DefenseRolled(int DefenderId, DiceRoll Roll, int Blocks) : GameEvent;

/// <summary>Hits that got past defense. <paramref name="Amount"/> is 0 when everything was blocked.</summary>
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
