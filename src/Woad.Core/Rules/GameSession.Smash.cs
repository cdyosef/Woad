using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>What can be smashed: a door, a crate, a cracked wall, a wall.</summary>
public enum BreakableKind
{
    Door,
    Crate,
    CrackedWall,
    Wall,
}

/// <summary>
/// Something that can be smashed, as it stands: how hard a blow must be to leave a break mark on it, how many
/// marks break it, how many it has, and whether it's wood (a Crowbar's help, and its proof against breaking).
/// </summary>
public sealed record Breakable(BreakableKind Kind, GridPosition Tile, int Toughness, int Needed, int Breaks, bool Wooden, Direction? Door = null)
{
    public string Name => Kind switch
    {
        BreakableKind.Door => "door",
        BreakableKind.Crate => "crate",
        BreakableKind.CrackedWall => "cracked wall",
        _ => "wall",
    };
}

/// <summary>
/// Smashing things: a Rumble attack (a weapon, or bare hands) on a door, a crate or a wall beside you. A blow that
/// reaches its toughness leaves a break mark; enough marks break it: a plain door takes 3 (a locked one more), a
/// crate 2, a cracked wall 5, and a wall 10 to crack (and then 5 more). A blow that falls short puts a break
/// token on the weapon (its value down 1; past 0 it breaks), unless it's proof against it (a Crowbar on wood).
/// The Smashing skill adds a Rumble die to any blow at something breakable; a Crowbar adds a die against wood
/// and can carry an Od Hand die into a smash. A door smashed in sets off any trap on it. A crate spills what's
/// in it, and a wall broken through sometimes something hidden in it.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>What's breakable on the tile (a door, a crate or a wall), or null.</summary>
    public Breakable? BreakableAt(Room room, GridPosition tile)
    {
        if (!room.Layout.Contains(tile))
        {
            return null;
        }

        if (room.Passages.FirstOrDefault(door => room.DoorTile(door.Key) == tile && !door.Value.IsHidden) is { Value: { } passage } found && !passage.IsOpen)
        {
            if (passage.IsBlocked)
            {
                return new Breakable(BreakableKind.CrackedWall, tile, GameRules.CrackedWallToughness, GameRules.CrackedWallBreaks, passage.Breaks, false, found.Key);
            }

            return passage.NeedsSpecialKey ? null
                : new Breakable(BreakableKind.Door, tile, GameRules.DoorToughness + (passage.IsLocked ? 1 : 0), GameRules.DoorBreaks + (passage.IsLocked ? passage.Difficulty / 4 : 0), passage.Breaks, true, found.Key);
        }

        int breaks = room.BreaksAt(tile);
        return room.Layout[tile] switch
        {
            TileKind.Crate => new Breakable(BreakableKind.Crate, tile, GameRules.CrateToughness, GameRules.CrateBreaks, breaks, true),
            TileKind.CrackedWall => new Breakable(BreakableKind.CrackedWall, tile, GameRules.CrackedWallToughness, GameRules.CrackedWallBreaks, breaks, false),
            TileKind.Wall => new Breakable(BreakableKind.Wall, tile, GameRules.WallToughness, GameRules.WallBreaks, breaks, false),
            _ => null,
        };
    }

    /// <summary>Whether the hero could swing at the tile: beside it (a door, from its doorway or beside it).</summary>
    private static bool CanSmashFrom(GridPosition from, GridPosition tile) => from.ChebyshevDistanceTo(tile) <= 1 && from != tile || from == tile;

    /// <summary>Why the active hero can't smash what's on <paramref name="target"/> now (or anything, with no target), or null.</summary>
    public string? CheckSmash(GridPosition? target = null)
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        if (AttackDiceLeft < 1)
        {
            return NoRumbleDice;
        }

        if (ActiveHero.IsCarrying)
        {
            return "Your arms are full.";
        }

        if (target is not { } tile)
        {
            return GetSmashTargets().Count > 0 ? null : "There's nothing beside you to smash.";
        }

        Room room = ActiveRoom;
        if (BreakableAt(room, tile) is not { } breakable)
        {
            return room.Passages.Values.Any(door => door.NeedsSpecialKey && door.IsLocked && room.Passages.Any(other => other.Value == door && room.DoorTile(other.Key) == tile))
                ? "That door won't break: it takes its own key." : "There's nothing there to smash.";
        }

        return CanSmashFrom(ActiveHero.Position, breakable.Tile) && (breakable.Door is not null || ActiveHero.Position != tile) ? null : "Stand beside it first.";
    }

    /// <summary>What the active hero could smash from where they stand.</summary>
    public IReadOnlyList<GridPosition> GetSmashTargets()
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 } || AttackDiceLeft < 1)
        {
            return [];
        }

        Room room = ActiveRoom;
        GridPosition at = ActiveHero.Position;
        return [.. room.Positions.Where(tile => tile.ChebyshevDistanceTo(at) <= 1 && BreakableAt(room, tile) is { } breakable && (breakable.Door is not null || tile != at))];
    }

    private string? Handle(Smash command, List<GameEvent> events)
    {
        if (CheckSmash(command.Target) is { } error)
        {
            return error;
        }

        int left = AttackDiceLeft;
        int dice = command.Dice ?? left;
        if (dice < 1 || dice > left)
        {
            return $"Choose 1 to {left} Rumble dice.";
        }

        Hero hero = ActiveHero;
        if (CheckBankedPowerDice(hero, command.PowerDice) is { } powerError)
        {
            return powerError;
        }

        Room room = ActiveRoom;
        Breakable target = BreakableAt(room, command.Target)!;
        Wielding wielding = Wield(hero, room, command.Target, only: HandWeapon(hero, command.WeaponId));
        bool armed = command.UseWeapon && wielding.HasStrike;
        int bonus = armed ? wielding.Bonus : 0;

        // The dice: the Rumble dice chosen, the weapon's own, the Smashing skill's, a Crowbar's on wood, and an Od Hand die a Crowbar carries in.
        CardInstance? crowbar = new[] { EquipSlot.MainHand, EquipSlot.OffHand }.Select(hand => hero.Equipment.GetValueOrDefault(hand)).FirstOrDefault(card => card?.Definition.OdHandSmash == true);
        int extra = (armed ? wielding.Dice : 0) + (hero.Knows(Skill.Smash) ? 1 : 0)
                    + (target.Wooden ? new[] { EquipSlot.MainHand, EquipSlot.OffHand }.Select(hand => hero.Equipment.GetValueOrDefault(hand)).OfType<CardInstance>().Distinct().Sum(card => card.Definition.WoodDice) : 0);
        bool odHand = crowbar is not null && OdHandDiceLeft > 0;
        Turn.AttackDiceUsed += dice;
        if (odHand)
        {
            Turn.OdHandDiceUsed++;
        }

        WeaponSpent? spent = UseWeapon(hero, room, wielding, armed, command.Target, events);
        SpendBankedPowerDice(hero, command.PowerDice, events);
        Reveal(hero, events);
        DiceRoll roll = _dice.Roll(dice + command.PowerDice + (odHand ? 1 : 0));
        (int[] bonusFaces, _) = RollBonusDice(extra);
        foreach (int face in bonusFaces)
        {
            roll = roll.With(face);
        }

        int total = roll.Total + bonus - AttackPenalty(hero.Conditions);
        bool success = total >= target.Toughness;
        int breaks = target.Breaks + (success ? 1 : 0);
        events.Add(new SmashRolled(hero.Id, room.Id, command.Target, roll, bonus, total, target.Toughness, success, breaks, target.Needed, target.Name));
        SettleRough(hero, room, spent, roll, events);

        if (!success)
        {
            BreakToken(hero, wielding.Card is { } card && armed && !(target.Wooden && card.Definition.WoodProof) ? card : null, events);
            return null;
        }

        if (breaks < target.Needed)
        {
            SetBreaks(room, target, breaks);
            return null;
        }

        BreakOpen(hero, room, target, events);
        return null;
    }

    /// <summary>A Rock or Club spent on the blow lands or breaks as the dice fall, as on any attack.</summary>
    private void SettleRough(Hero hero, Room room, WeaponSpent? spent, DiceRoll roll, List<GameEvent> events)
    {
        if (spent is null)
        {
            return;
        }

        if (spent.Flung is { } flung && roll.Faces.Contains(GameRules.PowerDieFace))
        {
            GridPosition spot = SafeTile(room, hero.Position);
            room.AddLoot(spot, [flung]);
            events.Add(new StoneLanded(hero.Id, flung.Id, room.Id, spot));
        }
        else if (spent.Flung is not null)
        {
            events.Add(new WeaponBroke(hero.Id, spent.NamedBy));
        }

        if (spent.Brittle is { } club && roll.Faces.Contains(1) && hero.FindCard(club.Id) is not null)
        {
            TakeFromHero(hero, club, 1);
            events.Add(new WeaponBroke(hero.Id, club.Id));
        }
    }

    /// <summary>A blow that fell short: a break token on the weapon, its value down 1; past nothing, it breaks.</summary>
    private void BreakToken(Hero hero, CardInstance? weapon, List<GameEvent> events)
    {
        if (weapon is null || hero.FindCard(weapon.Id) is null || weapon.Definition.BreaksOnUse)
        {
            return;
        }

        weapon.Wear++;
        events.Add(new WeaponWorn(hero.Id, weapon.Id, weapon.Wear));
        if (weapon.Wear > weapon.Definition.AttackBonus + Math.Max(0, weapon.Definition.Modifiers.Weapon))
        {
            TakeFromHero(hero, weapon, weapon.Count);
            events.Add(new WeaponBroke(hero.Id, weapon.Id));
        }
    }

    private static void SetBreaks(Room room, Breakable target, int breaks)
    {
        if (target.Door is { } side)
        {
            room.PassageOn(side)!.Breaks = breaks;
        }
        else
        {
            room.SetBreaks(target.Tile, breaks);
        }
    }

    /// <summary>Enough break marks: it bursts. A door's trap goes off on whoever broke it; a crate spills; a wall cracks, or breaks through.</summary>
    private void BreakOpen(Hero hero, Room room, Breakable target, List<GameEvent> events)
    {
        events.Add(new SmashedOpen(hero.Id, room.Id, target.Tile, target.Name));
        switch (target.Kind)
        {
            case BreakableKind.Door:
                Passage door = room.PassageOn(target.Door!.Value)!;
                door.IsLocked = false;
                door.Breaks = 0;
                door.IsBroken = true;
                if (door.Trap is { } trap)
                {
                    door.Trap = null;
                    SpringTrap(hero, room, trap, target.Tile, events);
                }

                break;

            case BreakableKind.Crate:
                room.SetBreaks(target.Tile, 0);
                room.Layout.Set(target.Tile, TileKind.Floor);
                SpillCrate(room, target.Tile, events);
                break;

            case BreakableKind.Wall:
                // Ten marks crack it; five more break it through.
                room.SetBreaks(target.Tile, 0);
                room.Layout.Set(target.Tile, TileKind.CrackedWall);
                events.Add(new WallCracked(room.Id, target.Tile));
                break;

            default:
                room.SetBreaks(target.Tile, 0);
                if (target.Door is { } side)
                {
                    room.PassageOn(side)!.Breaks = 0;
                }

                bool hidden = target.Door is null && !room.Caches.ContainsKey(target.Tile) && _random.Loot.NextInt(0, 100) < GameRules.WallFindChance;
                BreakWalls(room, [(target.Tile, target.Door)], events);
                if (hidden)
                {
                    DropLoot(room, target.Tile, new CardInstance(State.NextId(), State.LootDeck.Draw(room.Floor), broughtInByHeroId: null), events);
                }

                break;
        }
    }

    /// <summary>A crate broken open: a card from the loot deck, or some coins, or nothing but splinters.</summary>
    private void SpillCrate(Room room, GridPosition tile, List<GameEvent> events)
    {
        int roll = _random.Loot.NextInt(0, 100);
        if (roll < GameRules.CrateCardChance)
        {
            DropLoot(room, tile, new CardInstance(State.NextId(), State.LootDeck.Draw(room.Floor), broughtInByHeroId: null), events);
        }
        else if (roll < GameRules.CrateCardChance + GameRules.CrateCoinChance)
        {
            int coins = Money.Silver(_random.Loot.NextInt(1, 7));
            room.AddCoins(tile, coins);
            events.Add(new GoldAppeared(room.Id, tile, coins));
        }
    }
}
