using Woad.Core.Grid;

namespace Woad.Core.Commands;

/// <summary>
/// A request from the player controlling <see cref="HeroId"/>. Commands are the only way to
/// change a run, which keeps hot-seat, replays and future online play on the same path.
/// </summary>
public abstract record GameCommand(int HeroId);

/// <summary>Roll Move dice; the total plus Speed is this turn's movement.</summary>
public sealed record RollMovement(int HeroId) : GameCommand(HeroId);

/// <summary>Walk to a reachable tile in the current room, spending one movement point per step.</summary>
public sealed record MoveHero(int HeroId, GridPosition Destination) : GameCommand(HeroId);

/// <summary>Step through the door on the given side (costs 1 movement); you must be standing in the doorway.</summary>
public sealed record GoThroughDoor(int HeroId, Direction Door) : GameCommand(HeroId);

/// <summary>Attack with the main-hand weapon (or bare hands). Uses the turn's action.</summary>
public sealed record AttackMonster(int HeroId, int MonsterId) : GameCommand(HeroId);

/// <summary>Spend 1 Luck to reroll one die of the pending attack, or of the movement roll before any steps.</summary>
public sealed record RerollDie(int HeroId, int DieIndex) : GameCommand(HeroId);

/// <summary>Accept the pending attack roll and resolve it.</summary>
public sealed record ConfirmAttack(int HeroId) : GameCommand(HeroId);

/// <summary>
/// Use a card's effect. From the belt it's free once per turn; otherwise it uses the action.
/// <paramref name="Target"/> is the target tile, or null for effects on yourself.
/// </summary>
public sealed record UseCard(int HeroId, int CardId, GridPosition? Target = null) : GameCommand(HeroId);

/// <summary>Equip a card from the backpack; anything in its slot(s) goes to the backpack. Uses the action.</summary>
public sealed record EquipCard(int HeroId, int CardId) : GameCommand(HeroId);

/// <summary>Move a single-use card from the backpack to the belt. Uses the action.</summary>
public sealed record BeltCard(int HeroId, int CardId) : GameCommand(HeroId);

/// <summary>Move an equipped or belted card into the backpack. Uses the action.</summary>
public sealed record StowCard(int HeroId, int CardId) : GameCommand(HeroId);

/// <summary>Drop a card onto your tile as loot. Free.</summary>
public sealed record DropCard(int HeroId, int CardId) : GameCommand(HeroId);

/// <summary>Open an adjacent chest and draw from the loot deck. Uses the action.</summary>
public sealed record OpenChest(int HeroId, GridPosition Chest) : GameCommand(HeroId);

/// <summary>Take loot from your tile into the backpack, as much as fits. Uses the action.</summary>
public sealed record PickUpLoot(int HeroId) : GameCommand(HeroId);

public sealed record EndTurn(int HeroId) : GameCommand(HeroId);
