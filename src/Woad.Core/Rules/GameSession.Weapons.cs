using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Which weapon an attack is made with, and what it adds. Each turn a hero gets weapon strikes: one
/// from the main hand (two from a Rapier with nothing heavy in the off hand), and one more from an
/// off-hand Dagger. A strike adds that weapon's value (and its dice) to one attack. Any hero can strike
/// a monster right beside them; a weapon reaches further by its range: in sight (a bow), in a straight
/// line (a Spear), or thrown (a Dagger, which leaves the hand). A crossbow strikes whenever it's loaded,
/// and each shot unloads it.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>The weapon an attack is made with (null bare-handed), and what it adds if it has a strike left.</summary>
    private sealed record Wielding(CardInstance? Card, bool OffHand, bool HasStrike, int Bonus, int Dice, bool Thrown)
    {
        public static Wielding BareHanded { get; } = new(null, false, false, 0, 0, false);

        public CardDefinition? Weapon => Card?.Definition;
    }

    /// <summary>
    /// Whether a hero standing on <paramref name="from"/> could attack <paramref name="to"/> with anything
    /// they hold: right beside them, or as far as a weapon reaches. For the screens' targeting too.
    /// </summary>
    public static bool CanHeroReach(Hero hero, RoomLayout layout, GridPosition from, GridPosition to) =>
        Reaches(hero.MainWeapon, layout, from, to, out _) || Reaches(OffHandWeapon(hero), layout, from, to, out _);

    /// <summary>A weapon in the off hand that isn't the other half of a two-handed main weapon.</summary>
    private static CardInstance? OffHandWeapon(Hero hero) => hero.ThrownOffHand;

    /// <summary>Main-hand strikes a turn: two for a Rapier while the off hand is empty or light, otherwise one.</summary>
    private static int MainStrikes(Hero hero) =>
        hero.MainWeapon?.Definition is { TwinStrike: true }
        && (hero.Equipment.GetValueOrDefault(EquipSlot.OffHand) is not { } off || off.Definition.Light)
            ? 2
            : 1;

    /// <summary>A Dagger in each hand: each gets +1.</summary>
    private static int PairBonus(Hero hero) =>
        hero.MainWeapon?.Definition is { OffHandStrike: true } && OffHandWeapon(hero)?.Definition is { OffHandStrike: true } ? 1 : 0;

    /// <summary>
    /// Whether <paramref name="card"/> (null bare-handed) reaches from one tile to another: anything reaches
    /// right beside you; a weapon reaches its range, by its pattern, if it's loaded; a throwable one its
    /// throwing range (<paramref name="thrown"/>: it would leave the hand).
    /// </summary>
    private static bool Reaches(CardInstance? card, RoomLayout layout, GridPosition from, GridPosition to, out bool thrown)
    {
        thrown = false;
        if (Targeting.CanHeroAttack(layout, 1, from, to))
        {
            return true;
        }

        if (card?.Definition is not { } weapon)
        {
            return false;
        }

        AttackPattern pattern = weapon.Reach ? AttackPattern.Reach : AttackPattern.Ranged;
        if (!card.Unloaded && weapon.AttackRange > 1 && Targeting.CanAttack(layout, pattern, weapon.AttackRange, from, to))
        {
            return true;
        }

        thrown = weapon.ThrownRange > 0 && Targeting.CanAttack(layout, AttackPattern.Ranged, weapon.ThrownRange, from, to);
        return thrown;
    }

    /// <summary>
    /// What an attack on <paramref name="target"/> would be made with: the main hand if it reaches (and,
    /// for its bonus, has a strike left), else the off hand. A Dagger's bonus doubles against a stunned or
    /// prone monster.
    /// </summary>
    private Wielding Wield(Hero hero, Room room, GridPosition target, Monster? monster = null)
    {
        TurnState turn = Turn;
        CardInstance? main = hero.MainWeapon;
        CardInstance? off = OffHandWeapon(hero);
        Stats stats = hero.EffectiveStats;
        bool mainStrikeLeft = turn.StrikesUsed < MainStrikes(hero);
        bool mainReaches = Reaches(main, room.Layout, hero.Position, target, out bool mainThrown);
        bool offReaches = Reaches(off, room.Layout, hero.Position, target, out bool offThrown);

        int Ambush(CardDefinition weapon) =>
            weapon.Ambush && monster is not null && (monster.Stun.IsStunned || monster.Prone) ? weapon.AttackBonus + weapon.Modifiers.Weapon : 0;

        Wielding Main(bool strike)
        {
            CardDefinition? weapon = main?.Definition;
            int bonus = Math.Max(0, stats.Weapon) + (weapon?.AttackBonus ?? 0) + Versatile(hero)
                        + (weapon is { OffHandStrike: true } ? PairBonus(hero) : 0) + (weapon is null ? 0 : Ambush(weapon));
            return new Wielding(main, false, strike, strike ? bonus : 0, strike ? weapon?.Modifiers.WeaponDice ?? 0 : 0, mainThrown);
        }

        Wielding Off(bool strike)
        {
            CardDefinition weapon = off!.Definition;

            // An off-hand Dagger strikes on its own; a thrown off-hand weapon (the Boomerang) stands in for the main hand.
            int bonus = weapon.OffHandStrike
                ? weapon.AttackBonus + weapon.Modifiers.Weapon + PairBonus(hero) + Ambush(weapon)
                : Math.Max(0, stats.Weapon - (main?.Definition.Modifiers.Weapon ?? 0)) + weapon.Modifiers.Weapon + weapon.AttackBonus;
            return new Wielding(off, weapon.OffHandStrike, strike, strike ? bonus : 0, strike ? weapon.Modifiers.WeaponDice : 0, offThrown);
        }

        // A crossbow strikes whenever it's loaded; other weapons while a strike is left this turn.
        bool mainCanStrike = main?.Definition.Reload is not null ? !main.Unloaded : mainStrikeLeft;
        bool offCanStrike = off?.Definition is { } offWeapon && (offWeapon.OffHandStrike ? !turn.OffHandStrikeUsed : mainStrikeLeft);

        if (mainReaches && mainCanStrike && (main is not null || stats.Weapon > 0))
        {
            return Main(true);
        }

        if (offReaches && offCanStrike)
        {
            return Off(true);
        }

        // Nothing left to strike with: the attack goes in with the dice alone, by whatever reaches.
        return mainReaches ? Main(false) : offReaches ? Off(false) : Wielding.BareHanded;
    }

    /// <summary>
    /// Spends what an attack used: the strike (when its value went in), a crossbow's load, and a thrown
    /// Dagger, which lands at the target's feet (on the nearest free floor when it can't).
    /// </summary>
    private void UseWeapon(Hero hero, Room room, Wielding wielding, bool armed, GridPosition target, List<GameEvent> events)
    {
        if (armed && wielding.HasStrike && wielding.Weapon?.Reload is null)
        {
            if (wielding.OffHand)
            {
                Turn.OffHandStrikeUsed = true;
            }
            else
            {
                Turn.StrikesUsed++;
            }
        }

        if (armed && wielding.HasStrike && wielding.Card is { Definition.Reload: not null } crossbow)
        {
            crossbow.Unloaded = true;
        }

        if (wielding.Thrown && wielding.Card is { } dagger)
        {
            CardInstance thrown = TakeFromHero(hero, dagger, 1);
            GridPosition spot = room.Layout[target].IsWalkable() ? target : SafeTile(room, target);
            room.AddLoot(spot, [thrown]);
            events.Add(new DaggerThrown(hero.Id, thrown.Id, room.Id, spot));
        }
    }

    /// <summary>The weapon value the active hero's next strike would add (the main hand's, else an off-hand Dagger's); 0 with none left.</summary>
    public int WeaponBonusLeft => NextStrike()?.Bonus ?? 0;

    /// <summary>The weapon's own dice the active hero's next strike would add (the Longsword's d6).</summary>
    public int WeaponDiceLeft => NextStrike()?.Dice ?? 0;

    private (int Bonus, int Dice)? NextStrike()
    {
        if (State.Turn is not { } turn)
        {
            return null;
        }

        Hero hero = ActiveHero;
        Stats stats = hero.EffectiveStats;
        CardInstance? main = hero.MainWeapon;
        CardInstance? off = OffHandWeapon(hero);
        bool mainReady = main?.Definition.Reload is not null ? !main.Unloaded : turn.StrikesUsed < MainStrikes(hero);
        if (mainReady && (main is not null || stats.Weapon > 0))
        {
            CardDefinition? weapon = main?.Definition;
            return (Math.Max(0, stats.Weapon) + (weapon?.AttackBonus ?? 0) + Versatile(hero) + (weapon is { OffHandStrike: true } ? PairBonus(hero) : 0),
                    weapon?.Modifiers.WeaponDice ?? 0);
        }

        if (off?.Definition is { OffHandStrike: true } dagger && !turn.OffHandStrikeUsed)
        {
            return (dagger.AttackBonus + dagger.Modifiers.Weapon + PairBonus(hero), dagger.Modifiers.WeaponDice);
        }

        return null;
    }

    /// <summary>Why the active hero can't reload their crossbow now, or null.</summary>
    public string? CheckReload()
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        if (ActiveHero.MainWeapon is not { Definition.Reload: { } cost } crossbow)
        {
            return "You've nothing to reload.";
        }

        if (!crossbow.Unloaded)
        {
            return $"Your {crossbow.Definition.Name} is loaded.";
        }

        if (OdHandDiceLeft < cost.OdHand)
        {
            return $"Reloading takes {(cost.OdHand == 1 ? "an Od Hand die" : $"{cost.OdHand} Od Hand dice")}{(cost.Wend > 0 ? " and a Wend die" : "")}.";
        }

        return MoveDiceLeft < cost.Wend
            ? $"Reloading takes {(cost.Wend == 1 ? "a Wend die" : $"{cost.Wend} Wend dice")} too, and you've none left."
            : null;
    }

    private string? Handle(ReloadWeapon command, List<GameEvent> events)
    {
        if (CheckReload() is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        CardInstance crossbow = hero.MainWeapon!;
        ReloadCost cost = crossbow.Definition.Reload!;
        Turn.OdHandDiceUsed += cost.OdHand;
        Turn.MoveDiceUsed += cost.Wend;
        crossbow.Unloaded = false;
        events.Add(new WeaponReloaded(hero.Id, crossbow.Id));
        return null;
    }

    /// <summary>
    /// What a weapon does as its attack lands: a Great Axe's sixes cleave into more monsters beside the
    /// attacker, and a Maul's six shoves the target back, hit or miss.
    /// </summary>
    private void WeaponAftermath(Hero hero, Room room, PendingRoll attack, List<GameEvent> events)
    {
        if (attack.Weapon is not { } weapon)
        {
            return;
        }

        int sixes = attack.Roll.Faces.Count(face => face == GameRules.PowerDieFace);
        if (weapon.Cleave && sixes > 0)
        {
            List<Monster> beside = room.Monsters
                .Where(monster => !attack.TargetIds.Contains(monster.Id) && monster.NearestTile(hero.Position).ChebyshevDistanceTo(hero.Position) <= 1)
                .OrderBy(monster => monster.Hearts)
                .ThenBy(monster => monster.Id)
                .Take(sixes)
                .ToList();
            foreach (Monster monster in beside.Where(monster => room.FindMonster(monster.Id) is not null))
            {
                events.Add(new Cleaved(hero.Id, monster.Id));
                DamageMonster(hero, room, monster, attack.Total, DamageSource.Weapon, events, crush: weapon.Crush);
            }
        }

        if (weapon.ShoveOnSix && sixes > 0
            && attack.TargetIds.Select(room.FindMonster).FirstOrDefault(found => found is not null) is { Definition: { Boss: false, Miniboss: false } } target
            && ShoveDestination(room, hero, target.Position, target.Flies) is { } to)
        {
            PushMonster(hero, room, target, to, events);
        }
    }
}
