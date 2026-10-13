using Woad.Core.Grid;

namespace Woad.Core.Commands;

/// <summary>
/// A request from the player controlling <see cref="HeroId"/>. Commands are the only way to
/// change a run, which keeps hot-seat, replays and future online play on the same path.
/// </summary>
public abstract record GameCommand(int HeroId);

/// <summary>
/// Start a move: roll <paramref name="Dice"/> of this turn's unused Move dice (null for all of them),
/// plus Speed if <paramref name="UseSpeed"/> and it isn't used yet this turn. The total is the
/// move's steps. Starting a move ends the previous one. A negative Speed always applies to the first move.
/// </summary>
public sealed record RollMovement(int HeroId, int? Dice = null, bool UseSpeed = true, int PowerDice = 0) : GameCommand(HeroId);

/// <summary>Walk to a reachable tile in the current room, spending one movement point per step.</summary>
public sealed record MoveHero(int HeroId, GridPosition Destination) : GameCommand(HeroId);

/// <summary>Step through the door on the given side (costs 1 movement); you must be standing in the doorway.</summary>
public sealed record GoThroughDoor(int HeroId, Direction Door) : GameCommand(HeroId);

/// <summary>
/// Attack with <paramref name="Dice"/> of this turn's unused Attack dice (null for all of them),
/// plus the weapon's value if <paramref name="UseWeapon"/> and it isn't used yet this turn. The
/// total must beat the monster's armor plus its defense roll. Ends the move in progress.
/// <paramref name="PowerDice"/> banked Power Dice are rolled in too (RollMovement takes them the same way).
/// </summary>
public sealed record AttackMonster(int HeroId, int MonsterId, int? Dice = null, bool UseWeapon = true, int PowerDice = 0) : GameCommand(HeroId);

/// <summary>Roll a Power Die just earned (by a 6) into the pending attack or the move in progress.</summary>
public sealed record AddPowerDie(int HeroId) : GameCommand(HeroId);

/// <summary>Bank a Power Die just earned (by a 6) for a later roll, if the bank has room.</summary>
public sealed record BankPowerDie(int HeroId) : GameCommand(HeroId);

/// <summary>Spend 1 Luck to reroll one die of the pending attack, or of the move in progress before any steps.</summary>
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

/// <summary>Smash an adjacent pot: it may hold coins or a heart. Uses the action.</summary>
public sealed record SmashPot(int HeroId, GridPosition Pot) : GameCommand(HeroId);

/// <summary>Go down the stairs you're standing on to the next floor (costs 1 movement).</summary>
public sealed record TakeStairs(int HeroId) : GameCommand(HeroId);

/// <summary>Leave the dungeon through the exit you're standing on, once its boss is defeated. Ends your turn.</summary>
public sealed record Escape(int HeroId) : GameCommand(HeroId);
