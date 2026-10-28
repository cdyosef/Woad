using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Ice, from Icicle Arrow cast at the floor: the total its Ice dice roll is how many patches the caster
/// lays (the tile aimed at first, then one a click, within the spell's range), each lasting a round per Ice
/// die rolled. Over water a patch freezes it over. Picking a patch someone of yours stands on gives them
/// Snowshoes for as long: they walk ice like floor. Anyone else stepping onto ice slides a tile further
/// the same way, and that's the end of their move.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Why the active hero can't lay a patch of their Icicle Arrow's ice on <paramref name="tile"/> now, or null.</summary>
    public string? CheckLayIce(GridPosition tile)
    {
        if (State.Turn is not { IceToLay: > 0, IceSpellId: { } spellId })
        {
            return "You've no ice to lay.";
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (!InSpellRange(Content.GetSpell(spellId), room, hero, tile))
        {
            return "That's out of range.";
        }

        if (room.IsIcy(tile))
        {
            return Shod(room, tile) is null ? "That's icy already." : null;
        }

        return CanFreeze(room, tile) ? null : "Ice won't lie there.";
    }

    /// <summary>The tiles the active hero could lay their ice on now.</summary>
    public IReadOnlyList<GridPosition> GetIceTargets() =>
        State.Turn is { IceToLay: > 0 } ? [.. ActiveRoom.Positions.Where(tile => CheckLayIce(tile) is null)] : [];

    private string? Handle(LayIce command, List<GameEvent> events)
    {
        if (CheckLayIce(command.Tile) is { } error)
        {
            return error;
        }

        Room room = ActiveRoom;
        Turn.IceToLay--;
        if (room.IsIcy(command.Tile))
        {
            Hero shod = Shod(room, command.Tile)!;
            shod.SnowshoesRounds = Math.Max(shod.SnowshoesRounds, Turn.IceRounds);
            events.Add(new BoonGained(shod.Id, Boon.Snowshoes, shod.SnowshoesRounds, 0));
        }
        else
        {
            Freeze(room, [command.Tile], Turn.IceRounds, events);
        }

        return null;
    }

    /// <summary>The caster or an ally standing on the tile, to be given Snowshoes.</summary>
    private Hero? Shod(Room room, GridPosition tile) =>
        State.ActiveHeroesIn(room).FirstOrDefault(other => other.Position == tile);

    /// <summary>Ground ice can lie on: open floor, rubble, stairs and water (which it freezes over).</summary>
    private static bool CanFreeze(Room room, GridPosition tile) =>
        room.Layout.Contains(tile) && (room.Layout[tile].IsWalkable() || room.Layout[tile] == TileKind.DeepWater);

    private static void Freeze(Room room, IReadOnlyList<GridPosition> tiles, int rounds, List<GameEvent> events)
    {
        foreach (GridPosition tile in tiles)
        {
            room.Freeze(tile, rounds);
        }

        events.Add(new IceLaid(room.Id, tiles, rounds));
    }

    /// <summary>Icicle Arrow at bare floor: its dice roll how much ice there is, the first patch where it's aimed.</summary>
    private void CastIce(Hero hero, Room room, SpellDefinition spell, GridPosition aim, int dice, List<GameEvent> events)
    {
        (int[] faces, DieType[] types) = RollTypedDice([(spell.DieType, dice)]);
        var roll = new DiceRoll(faces);
        events.Add(new SpellRolled(hero.Id, roll, types));
        Freeze(room, [aim], faces.Length, events);
        Turn.IceRounds = faces.Length;
        Turn.IceToLay = roll.Total - 1;
        Turn.IceSpellId = spell.Id;
    }

    /// <summary>
    /// Where a walk along <paramref name="path"/> from <paramref name="start"/> ends on ice: at the first
    /// patch (for anyone without Snowshoes), and the tile beyond it they slide on to, if there's room.
    /// Null if it doesn't cross ice.
    /// </summary>
    private (int Index, GridPosition? Slide)? SlipOnIce(Room room, GridPosition start, IReadOnlyList<GridPosition> path, bool flying)
    {
        int index = path.ToList().FindIndex(room.IsIcy);
        if (flying || index < 0)
        {
            return null;
        }

        GridPosition from = index == 0 ? start : path[index - 1], on = path[index];
        var beyond = new GridPosition(on.X + Math.Sign(on.X - from.X), on.Y + Math.Sign(on.Y - from.Y));
        bool open = room.Layout.Contains(beyond) && !IsOccupied(room, beyond)
                    && (room.Layout[beyond].IsWalkable() || room.Layout[beyond] is TileKind.Pit or TileKind.DeepWater);
        return (index, open ? beyond : null);
    }

    /// <summary>At the start of each round the ice melts a little, and boons wear off.</summary>
    private void WearOffMagic(List<GameEvent> events)
    {
        foreach (Room room in State.Rooms.Values.Where(room => room.Ice.Count > 0))
        {
            if (room.Melt() is { Count: > 0 } melted)
            {
                events.Add(new IceMelted(room.Id, melted));
            }
        }

        foreach (Hero hero in State.Heroes)
        {
            if (hero.SnowshoesRounds > 0 && --hero.SnowshoesRounds == 0)
            {
                events.Add(new BoonEnded(hero.Id, Boon.Snowshoes));
            }

            if (hero.ShiningRounds > 0 && --hero.ShiningRounds == 0)
            {
                hero.ShiningDefense = 0;
                events.Add(new BoonEnded(hero.Id, Boon.ShiningLight));
            }

            if (hero.ShadowboundRounds > 0 && --hero.ShadowboundRounds == 0)
            {
                events.Add(new BoonEnded(hero.Id, Boon.Shadowbound));
            }
        }
    }

    /// <summary>A Shadowbound hero who attacks, casts or uses an item is seen again.</summary>
    private static void Reveal(Hero hero, List<GameEvent> events)
    {
        if (hero.ShadowboundRounds > 0)
        {
            hero.ShadowboundRounds = 0;
            events.Add(new BoonEnded(hero.Id, Boon.Shadowbound));
        }
    }
}
