using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// The Od Hand card with something in the off hand: an attack with it, on top of the Rumble attack. A torch,
/// a Stick, a Rock, a Dagger or any weapon held there: one Od Hand die at most, plus the Focus Bonus and the
/// item's own value (a Dagger's doubles against a stunned or prone monster), once a turn. It reaches as the
/// item does (a Rock or a Dagger thrown), breaks or lands as it would, and a torch's die is a fire die: a 6
/// sets the target alight, a 1 burns the torch out.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>What the hero holds in the off hand to attack with by the Od Hand card: a weapon or a torch (not half of a two-handed weapon), or null.</summary>
    public static CardInstance? OffHandItem(Hero hero) =>
        hero.Equipment.GetValueOrDefault(EquipSlot.OffHand) is { } off && off != hero.MainWeapon && (off.Definition.Type == CardType.Weapon || off.Definition.Torch)
            ? off
            : null;

    /// <summary>The monsters the active hero could attack with what's in their off hand now.</summary>
    public IReadOnlyList<Monster> GetOffHandTargets()
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0, OffHandAttacked: false } || OdHandDiceLeft < 1 || OffHandItem(ActiveHero) is not { } item)
        {
            return [];
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        return [.. room.Monsters.Where(monster => CanHeroAttack(hero, room, monster, item))];
    }

    /// <summary>Why the active hero can't attack with what's in their off hand (that monster, or anyone), or null.</summary>
    public string? CheckOffHandAttack(int? monsterId = null)
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 } turn)
        {
            return "Finish the roll first.";
        }

        if (OffHandItem(ActiveHero) is null)
        {
            return "You've nothing in your off hand to attack with.";
        }

        if (turn.OffHandAttacked)
        {
            return "You've already attacked with your off hand this turn.";
        }

        if (OdHandDiceLeft < 1)
        {
            return NoOdHandDice;
        }

        IReadOnlyList<Monster> targets = GetOffHandTargets();
        return monsterId is int id ? (targets.Any(monster => monster.Id == id) ? null : "That's out of reach of your off hand.")
            : targets.Count > 0 ? null : "There's nothing in reach of your off hand.";
    }

    private string? Handle(OffHandAttack command, List<GameEvent> events)
    {
        if ((CheckOffHandAttack(command.MonsterId) ?? CheckBankedPowerDice(ActiveHero, command.PowerDice)) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        Monster monster = room.FindMonster(command.MonsterId)!;
        CardInstance item = OffHandItem(hero)!;
        CardDefinition weapon = item.Definition;
        GridPosition at = monster.Tiles.First(tile => Reaches(hero, item, room.Layout, hero.Position, tile, out _));
        Reaches(hero, item, room.Layout, hero.Position, at, out bool thrown);
        int value = weapon.AttackBonus + weapon.Modifiers.Weapon;
        int ambush = weapon.Ambush && (monster.Stun.IsStunned || monster.Prone) ? value : 0;
        int bonus = hero.Focus + value + ambush;

        // One Od Hand die; a Dagger's strike is spent on it, so it doesn't add to a Rumble attack too.
        Turn.OdHandDiceUsed++;
        Turn.OffHandAttacked = true;
        Turn.OffHandStrikeUsed |= weapon.OffHandStrike;
        Reveal(hero, events);
        WeaponSpent? spent = UseWeapon(hero, room, new Wielding(item, true, true, bonus, 0, thrown), armed: true, monster.Position, events, strike: false);
        SpendBankedPowerDice(hero, command.PowerDice, events);
        bool torch = weapon.Torch;
        RollHeroAttack(hero, torch ? 0 : 1, bonus, [monster.Id], DamageSource.Weapon, events, command.PowerDice,
            stunOnSix: weapon.StunOnSix, weapon: weapon, typed: torch ? [(DieType.Fire, 1)] : null, torches: torch ? 1 : 0, spent: spent);
        return null;
    }

    /// <summary>A Stick in either hand that a fire would light, or null.</summary>
    public static CardInstance? StickInHand(Hero hero) =>
        new[] { EquipSlot.MainHand, EquipSlot.OffHand }.Select(slot => hero.Equipment.GetValueOrDefault(slot)).FirstOrDefault(card => card?.Definition.LightsAs is not null);

    /// <summary>
    /// Fire the hero could light a Stick at: one they hold (a lit torch, Hold Fire's flame) or are (burning),
    /// or one beside them: a lit brazier, burning floor, a burning or fiery creature, or a hero with a light.
    /// </summary>
    private bool FireAtHand(Hero hero, Room room)
    {
        bool Beside(GridPosition tile) => tile.ChebyshevDistanceTo(hero.Position) <= 1;
        return hero.TorchesHeld > 0 || hero.FireDice > 0
            || room.LitBraziers.Any(Beside) || room.Flames.Keys.Any(Beside)
            || room.Monsters.Any(monster => (monster.FireDice > 0 || monster.Definition.DieType == DieType.Fire) && Beside(monster.NearestTile(hero.Position)))
            || State.HeroesIn(room).Any(other => other != hero && (other.TorchesHeld > 0 || other.FireDice > 0) && Beside(other.Position));
    }

    /// <summary>Why the active hero can't light a Stick now, or null.</summary>
    public string? CheckLightStick()
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        if (StickInHand(ActiveHero) is null)
        {
            return "You've no Stick in hand to light.";
        }

        return FireAtHand(ActiveHero, ActiveRoom) ? null : "There's no fire beside you (or in your hands) to light it at.";
    }

    /// <summary>One Stick from the hand becomes a Lit Stick in that hand; the rest of the stack goes to the bag.</summary>
    private string? Handle(LightStick command, List<GameEvent> events)
    {
        if (CheckLightStick() is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        CardInstance stick = StickInHand(hero)!;
        EquipSlot slot = hero.SlotOf(stick)!.Value;
        TakeFromHero(hero, stick, 1);
        if (hero.SlotOf(stick) is not null)
        {
            hero.Remove(stick);
            GiveCards(hero, room, [stick], events);
        }

        var lit = new CardInstance(State.NextId(), Content.GetCard(stick.Definition.LightsAs!), broughtInByHeroId: null);
        hero.Equip(lit, slot);
        events.Add(new StickLit(hero.Id, lit.Id));
        return null;
    }
}
