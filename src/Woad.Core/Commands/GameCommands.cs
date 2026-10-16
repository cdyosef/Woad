using Woad.Core.Grid;
using Woad.Core.Model;

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

/// <summary>
/// Rouse a hero Keeling Over next to you (diagonals too): every 6 of the total earns them a mark of
/// life, and every die showing 1 adds a Doom Die to their next Keel roll.
/// </summary>
public sealed record Rouse(int HeroId, int TargetId, int? Dice = null, bool UseTools = true, int PowerDice = 0)
    : OdHandCommand(HeroId, Dice, UseTools, PowerDice);

/// <summary>
/// Keeling Over, a hero's whole turn: roll the Keel Die, plus <paramref name="PowerDice"/> banked Power
/// Dice whose sixes earn marks of life. Ends the turn.
/// </summary>
public sealed record RollKeel(int HeroId, int PowerDice = 0) : GameCommand(HeroId);

/// <summary>Jump through a trap door you've found, next to you, to a room on the floor below (costs 1 step, no harm).</summary>
public sealed record JumpDown(int HeroId, GridPosition TrapDoor) : GameCommand(HeroId);

public sealed record EndTurn(int HeroId) : GameCommand(HeroId);

/// <summary>Smash an adjacent pot: it may hold coins or a heart. Uses the action.</summary>
public sealed record SmashPot(int HeroId, GridPosition Pot) : GameCommand(HeroId);

/// <summary>Take the stairs you're standing on (costs 1 movement): down to the next floor, or back up to the one above.</summary>
public sealed record TakeStairs(int HeroId) : GameCommand(HeroId);

/// <summary>Leave the dungeon through the exit you're standing on, once its boss is defeated. Ends your turn.</summary>
public sealed record Escape(int HeroId) : GameCommand(HeroId);

/// <summary>
/// Give one of your companions a standing order. Free, at any point in your turn. An order to hold
/// keeps the companion on the tile it stands on now.
/// </summary>
public sealed record OrderCompanion(int HeroId, int CompanionId, CompanionOrder Order) : GameCommand(HeroId);

/// <summary>
/// Shove an adjacent monster (or, with friendly fire on, a hero or companion) one tile straight away
/// from you: roll like an attack (Rumble dice and the weapon); beating its defense pushes it, perhaps
/// onto a trap or into a pit. Bosses can't be shoved, and the tile behind must be open floor, a trap
/// or a pit; a monster shoved into deep water loses a heart and scrambles out.
/// </summary>
public sealed record Shove(int HeroId, int TargetId, int? Dice = null, bool UseWeapon = true, int PowerDice = 0) : GameCommand(HeroId);

/// <summary>
/// Leap: spend one unused Wend die to jump in a straight line (diagonals too) toward <paramref name="Target"/>,
/// up to <see cref="Rules.GameRules.MaxLeap"/> tiles away, over pits, water, traps and figures but not walls.
/// Pick the target first; the die is rolled as you jump, and the leap carries half the roll (rounded down),
/// never past the target. Coming down short over a pit drops you in; over water, you splash in.
/// </summary>
public sealed record Leap(int HeroId, GridPosition Target) : GameCommand(HeroId);

/// <summary>
/// With friendly fire on: attack another hero or companion in reach, exactly as you would a monster
/// (Rumble dice and the weapon against their defense).
/// </summary>
public sealed record AttackAlly(int HeroId, int TargetId, int? Dice = null, bool UseWeapon = true, int PowerDice = 0) : GameCommand(HeroId);

/// <summary>Open the locked gate next to you (up, down, left or right) with a key. Free, like unlocking a door.</summary>
public sealed record OpenGate(int HeroId, GridPosition Gate) : GameCommand(HeroId);

/// <summary>Go through the broken-open breach you're standing on into the neighboring room (costs 1 movement).</summary>
public sealed record GoThroughBreach(int HeroId) : GameCommand(HeroId);

/// <summary>Lie down: costs 1 step. Prone, every step costs double (in deep water it's diving).</summary>
public sealed record GoProne(int HeroId) : GameCommand(HeroId);

/// <summary>Get back on your feet: costs 2 steps.</summary>
public sealed record GetUp(int HeroId) : GameCommand(HeroId);

/// <summary>On fire, with nothing done yet this turn: put the fire out, ending the turn prone.</summary>
public sealed record StopDropRoll(int HeroId) : GameCommand(HeroId);

/// <summary>
/// Strike a bomb in reach with <paramref name="Dice"/> Rumble dice: no roll to hit, but each die (and
/// Power Die) is a 12% chance to set it off, and each point of weapon bonus 2% more.
/// </summary>
public sealed record AttackBomb(int HeroId, int BombId, int? Dice = null, bool UseWeapon = true, int PowerDice = 0) : GameCommand(HeroId);
