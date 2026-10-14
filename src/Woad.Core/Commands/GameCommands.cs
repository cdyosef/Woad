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
/// move's steps, added to any left over from earlier moves. A negative Speed always applies to the first move.
/// </summary>
public sealed record RollMovement(int HeroId, int? Dice = null, bool UseSpeed = true, int PowerDice = 0) : GameCommand(HeroId);

/// <summary>Walk to a reachable tile in the current room, spending one movement point per step.</summary>
public sealed record MoveHero(int HeroId, GridPosition Destination) : GameCommand(HeroId);

/// <summary>Step through the door on the given side (costs 1 movement); you must be standing in the doorway.</summary>
public sealed record GoThroughDoor(int HeroId, Direction Door) : GameCommand(HeroId);

/// <summary>
/// Attack with <paramref name="Dice"/> of this turn's unused Attack dice (null for all of them),
/// plus the weapon's value if <paramref name="UseWeapon"/> and it isn't used yet this turn. The
/// total must beat the monster's armor plus its defense roll. Steps left in the move stay usable afterwards.
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

/// <summary>
/// Open an adjacent chest and draw from the loot deck. A locked chest takes a key (or pick it first).
/// Looting: the first loot each turn is free, after that it uses the action.
/// </summary>
public sealed record OpenChest(int HeroId, GridPosition Chest) : GameCommand(HeroId);

/// <summary>Take cards (as many as fit), keys and the Boss Key from your tile. Looting, like opening a chest.</summary>
public sealed record PickUpLoot(int HeroId) : GameCommand(HeroId);

/// <summary>Open the locked door you stand in with a key, or the Boss Door with the Boss Key. Free.</summary>
public sealed record UnlockDoor(int HeroId, Direction Door) : GameCommand(HeroId);

/// <summary>
/// An Od Hand roll: <paramref name="Dice"/> of this turn's unused Od Hand dice (null for all), plus
/// the tools' value if <paramref name="UseTools"/> and it isn't used yet, plus banked Power Dice.
/// </summary>
public abstract record OdHandCommand(int HeroId, int? Dice, bool UseTools, int PowerDice) : GameCommand(HeroId);

/// <summary>Pick the lock of the door you stand in (<paramref name="Target"/> is your own tile) or of an adjacent chest.</summary>
public sealed record PickLock(int HeroId, GridPosition Target, int? Dice = null, bool UseTools = true, int PowerDice = 0)
    : OdHandCommand(HeroId, Dice, UseTools, PowerDice);

/// <summary>Search the room: reveals every secret door, trap and stash whose difficulty the total reaches.</summary>
public sealed record Search(int HeroId, int? Dice = null, bool UseTools = true, int PowerDice = 0)
    : OdHandCommand(HeroId, Dice, UseTools, PowerDice);

/// <summary>Disarm a revealed trap: on an adjacent tile, in an adjacent chest, or on the door you stand in (your own tile).</summary>
public sealed record Disarm(int HeroId, GridPosition Target, int? Dice = null, bool UseTools = true, int PowerDice = 0)
    : OdHandCommand(HeroId, Dice, UseTools, PowerDice);

/// <summary>Steal from an adjacent monster or hero, once per turn: beat their roll to take a key or a card.</summary>
public sealed record Steal(int HeroId, int TargetId, int? Dice = null, bool UseTools = true, int PowerDice = 0)
    : OdHandCommand(HeroId, Dice, UseTools, PowerDice);

/// <summary>Jump through a trap door you've found, next to you, to a room on the floor below (costs 1 step, no harm).</summary>
public sealed record JumpDown(int HeroId, GridPosition TrapDoor) : GameCommand(HeroId);

public sealed record EndTurn(int HeroId) : GameCommand(HeroId);

/// <summary>Smash an adjacent pot: it may hold coins or a heart. Uses the action.</summary>
public sealed record SmashPot(int HeroId, GridPosition Pot) : GameCommand(HeroId);

/// <summary>Go down the stairs you're standing on to the next floor (costs 1 movement).</summary>
public sealed record TakeStairs(int HeroId) : GameCommand(HeroId);

/// <summary>Leave the dungeon through the exit you're standing on, once its boss is defeated. Ends your turn.</summary>
public sealed record Escape(int HeroId) : GameCommand(HeroId);
