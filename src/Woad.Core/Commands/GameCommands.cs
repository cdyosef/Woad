using Woad.Core.Characters;
using Woad.Core.Content;
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
/// <paramref name="WeaponId"/> picks which weapon in hand to attack with (null: the main hand if it reaches, else the off hand).
/// </summary>
public sealed record AttackMonster(int HeroId, int MonsterId, int? Dice = null, bool UseWeapon = true, int PowerDice = 0, int? WeaponId = null) : GameCommand(HeroId);

/// <summary>
/// Cast a spell the hero knows at <paramref name="Target"/> (nothing to aim for one that hits all around,
/// like Quake). It costs its Power plus <paramref name="ExtraPower"/> more (a die each, up to the Magic
/// rank) and the Rumble dice left this turn. Swirly Eyes moves a target it hits to <paramref name="MoveTo"/>.
/// </summary>
public sealed record CastSpell(int HeroId, string SpellId, GridPosition? Target = null, int ExtraPower = 0, GridPosition? MoveTo = null) : GameCommand(HeroId);

/// <summary>Lay a patch of the ice an Icicle Arrow rolled (or give Snowshoes to someone on an icy tile).</summary>
public sealed record LayIce(int HeroId, GridPosition Tile) : GameCommand(HeroId);

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

/// <summary>
/// Equip a card from the backpack; anything in its slot(s) goes to the backpack. Uses the action.
/// <paramref name="Hand"/> picks the hand for a card that goes in either (the Boomerang); such a card
/// already equipped moves to the other hand (or the one chosen).
/// </summary>
public sealed record EquipCard(int HeroId, int CardId, EquipSlot? Hand = null) : GameCommand(HeroId);

/// <summary>Frees a chained merchant beside the hero with a key (their lock can be picked with <see cref="PickLock"/> instead).</summary>
public sealed record FreeMerchant(int HeroId, int MerchantId) : GameCommand(HeroId);

/// <summary>
/// Spends a level's point on a stat, for yourself or a companion of yours (from the Party menu). Free.
/// A rank of Magic teaches the basic spell <paramref name="SpellId"/>.
/// </summary>
public sealed record SpendPoint(int HeroId, int TargetId, StatKind Stat, string? SpellId = null) : GameCommand(HeroId);

/// <summary>Sets how a companion of yours manages its things on its turns (from the Party menu). Free.</summary>
public sealed record SetCompanionPolicy(int HeroId, int CompanionId, CompanionPolicy Policy) : GameCommand(HeroId);

/// <summary>Unlocks the shackles of a hero (yourself, or an ally beside you) with a key.</summary>
public sealed record Unshackle(int HeroId, int TargetId) : GameCommand(HeroId);

/// <summary>Buys a card from a freed merchant beside the hero, with the gold they've found this run.</summary>
public sealed record BuyFromMerchant(int HeroId, int MerchantId, string CardId) : GameCommand(HeroId);

/// <summary>Sells one of a card the hero carries (by its instance id) to a freed merchant beside them.</summary>
public sealed record SellToMerchant(int HeroId, int MerchantId, int CardId) : GameCommand(HeroId);

/// <summary>Attacks a merchant with Rumble dice: a crime against the Guild and the Law.</summary>
public sealed record AttackMerchant(int HeroId, int MerchantId, int? Dice = null, bool UseWeapon = true, int PowerDice = 0) : GameCommand(HeroId);

/// <summary>Reloads the hero's crossbow, spending its reload dice (Od Hand, and Wend for a heavy one).</summary>
public sealed record ReloadWeapon(int HeroId) : GameCommand(HeroId);

/// <summary>Move a single-use card from the backpack to the belt. Uses the action.</summary>
public sealed record BeltCard(int HeroId, int CardId) : GameCommand(HeroId);

/// <summary>Move an equipped or belted card into the backpack. Uses the action.</summary>
public sealed record StowCard(int HeroId, int CardId) : GameCommand(HeroId);

/// <summary>Drop a card onto your tile as loot (of a stack, <paramref name="Count"/> of it; all by default). Free.</summary>
public sealed record DropCard(int HeroId, int CardId, int? Count = null) : GameCommand(HeroId);

/// <summary>
/// Open an adjacent chest and draw from the loot deck. A locked chest takes a key (or pick it first).
/// Looting: the first loot each turn is free, after that it uses the action.
/// </summary>
public sealed record OpenChest(int HeroId, GridPosition Chest) : GameCommand(HeroId);

/// <summary>
/// Take everything (cards as fit, gold, keys, the Boss Key, an unlit bomb) from a pile on or next to
/// you (your own tile by default), or an opened chest. Looting, like opening a chest.
/// </summary>
public sealed record PickUpLoot(int HeroId, GridPosition? Tile = null) : GameCommand(HeroId);

/// <summary>
/// Take one card (or <paramref name="Count"/> off its stack) from a pile on or next to you, or an opened
/// chest; with no card, the gold and keys there. Looting: the first take each turn is the free loot,
/// and more from the same pile that turn are free.
/// </summary>
public sealed record TakeLoot(int HeroId, GridPosition Tile, int? CardId, int? Count = null) : GameCommand(HeroId);

/// <summary>Put one of your cards (or <paramref name="Count"/> of a stack) into a pile on or next to you, or an opened chest. Free.</summary>
public sealed record PutLoot(int HeroId, GridPosition Tile, int CardId, int? Count = null) : GameCommand(HeroId);

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

/// <summary>Retreat from a fight out on the Woarld Map: an Od Hand roll against the monsters' chase.</summary>
public sealed record Retreat(int HeroId, int? Dice = null, bool UseTools = false, int PowerDice = 0)
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
/// Od Hand plus Magic: learn the spell on a scroll you carry (6 plus its level). The scroll is used up;
/// fail and it goes off around you.
/// </summary>
public sealed record LearnSpell(int HeroId, int CardId, int? Dice = null, int PowerDice = 0)
    : OdHandCommand(HeroId, Dice, false, PowerDice);

/// <summary>Od Hand: tend blind eyes, your own or an ally's beside you (10 plus their Blind tokens).</summary>
public sealed record Tend(int HeroId, int TargetId, int? Dice = null, bool UseTools = true, int PowerDice = 0)
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

/// <summary>Give one of your cards (or <paramref name="Count"/> of a stack) to an ally beside you. Free.</summary>
public sealed record GiveCard(int HeroId, int ToHeroId, int CardId, int? Count = null) : GameCommand(HeroId);

/// <summary>Take a card (or <paramref name="Count"/> of a stack) from an ally beside you. Free.</summary>
public sealed record TakeCard(int HeroId, int FromHeroId, int CardId, int? Count = null) : GameCommand(HeroId);

/// <summary>Give gold to an ally beside you. Free.</summary>
public sealed record GiveGold(int HeroId, int ToHeroId, int Amount) : GameCommand(HeroId);

/// <summary>Take gold from an ally beside you. Free.</summary>
public sealed record TakeGold(int HeroId, int FromHeroId, int Amount) : GameCommand(HeroId);

/// <summary>Give a key (or the Boss Key) to an ally beside you. Free.</summary>
public sealed record GiveKey(int HeroId, int ToHeroId, bool BossKey = false) : GameCommand(HeroId);

/// <summary>Take a key (or the Boss Key) from an ally beside you. Free.</summary>
public sealed record TakeKey(int HeroId, int FromHeroId, bool BossKey = false) : GameCommand(HeroId);

public enum OutfitAction
{
    Equip,
    Belt,
    Stow,
}

/// <summary>Equip, belt or stow one of your companion's cards, standing beside it. Free.</summary>
public sealed record OutfitCompanion(int HeroId, int CompanionId, int CardId, OutfitAction Action) : GameCommand(HeroId);

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
public sealed record AttackAlly(int HeroId, int TargetId, int? Dice = null, bool UseWeapon = true, int PowerDice = 0, int? WeaponId = null) : GameCommand(HeroId);

/// <summary>Open the locked gate next to you (up, down, left or right) with a key. Free, like unlocking a door.</summary>
public sealed record OpenGate(int HeroId, GridPosition Gate) : GameCommand(HeroId);

/// <summary>Go through the broken-open breach you're standing on into the neighboring room (costs 1 movement).</summary>
public sealed record GoThroughBreach(int HeroId) : GameCommand(HeroId);

/// <summary>Beat the Icarus Wings: a d6 more of flight (a 6 rolls a Power Die in), as often as you dare. A 1 breaks them.</summary>
public sealed record BeatWings(int HeroId) : GameCommand(HeroId);

/// <summary>Aloft on the Icarus Wings: fly to a tile, over pits, water, traps and figures, a step of flight a tile.</summary>
public sealed record Fly(int HeroId, GridPosition Destination) : GameCommand(HeroId);

/// <summary>Come down from the air where you are: over a pit, that's a fall.</summary>
public sealed record Land(int HeroId) : GameCommand(HeroId);

/// <summary>Lie down: costs 1 step. Prone, every step costs double (in deep water it's diving).</summary>
public sealed record GoProne(int HeroId) : GameCommand(HeroId);

/// <summary>Get back on your feet: costs 2 steps.</summary>
public sealed record GetUp(int HeroId) : GameCommand(HeroId);

/// <summary>On fire, with nothing done yet this turn: put the fire out, ending the turn prone.</summary>
public sealed record StopDropRoll(int HeroId) : GameCommand(HeroId);

/// <summary>Buried: dig at the earth with every Rumble die left this turn, every 3 of the roll throwing off a token.</summary>
public sealed record DigOut(int HeroId) : GameCommand(HeroId);

/// <summary>
/// Strike a bomb in reach with <paramref name="Dice"/> Rumble dice: no roll to hit, but each die (and
/// Power Die) is a 12% chance to set it off, and each point of weapon bonus 2% more.
/// </summary>
public sealed record AttackBomb(int HeroId, int BombId, int? Dice = null, bool UseWeapon = true, int PowerDice = 0) : GameCommand(HeroId);
