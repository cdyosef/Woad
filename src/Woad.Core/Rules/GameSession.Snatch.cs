using Woad.Core.Commands;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Leviathan Jr.'s snatch: a 6 on its claws that hits seizes the hero, who's pulled into the deep water beside it and
/// under. Each turn after, holding them, it swims off through the water as far as it can from everyone else, dragging
/// them along behind it and under with it. Under, they hold their breath (Blue Hearts). They break free with a Rumble
/// roll beating its claws' total (every Rumble die they've left), or when it's hurt, stunned, frozen or knocked flat.
/// </summary>
public sealed partial class GameSession
{
    private void Snatch(Room room, Monster monster, Hero hero, int grip, List<GameEvent> events)
    {
        hero.GrabbedBy = monster.Id;
        hero.GripTotal = grip;
        events.Add(new Snatched(monster.Id, hero.Id, grip));
        PullUnder(room, monster, hero, events);
    }

    /// <summary>Into the deep water beside it, and under: the hero's taken to the free deep tile beside it nearest them.</summary>
    private void PullUnder(Room room, Monster monster, Hero hero, List<GameEvent> events)
    {
        // Into the deep beside it if it can (else into any water beside it: its claws reach 2, and it holds what's beside it).
        if ((room.Layout[hero.Position] != TileKind.DeepWater || hero.Position.ManhattanDistanceTo(monster.Position) > 1)
            && monster.Position.OrthogonalNeighbors()
                .Where(tile => room.Layout.Contains(tile) && InWater(room, tile) && !IsOccupied(room, tile))
                .OrderBy(tile => room.Layout[tile] == TileKind.DeepWater ? 0 : 1).ThenBy(tile => tile.ManhattanDistanceTo(hero.Position)).ThenBy(tile => tile.Y).ThenBy(tile => tile.X)
                .Select(tile => (GridPosition?)tile).FirstOrDefault() is { } deep)
        {
            GridPosition from = hero.Position;
            hero.Position = deep;
            events.Add(new DraggedUnder(monster.Id, hero.Id, from, deep));
        }

        if (room.Layout[hero.Position] == TileKind.DeepWater && !hero.HeldUnder)
        {
            // The first time it drags someone down, it has something to say about it first.
            if (monster.Definition.Speech is { DragLine: { } line } speech && !monster.SaidDragLine)
            {
                monster.SaidDragLine = true;
                events.Add(new MonsterSpoke(monster.Id, speech.Language, line));
            }

            hero.HeldUnder = true;
            events.Add(new DraggedUnder(monster.Id, hero.Id, hero.Position, hero.Position));
        }
    }

    /// <summary>
    /// Holding someone, it swims off with them instead of closing in: through the water, up to its moves, to the deep
    /// tile furthest from everyone else, the one it holds dragged along behind it. True if it held someone.
    /// </summary>
    private bool DragHeld(Room room, Monster monster, IReadOnlyList<Hero> standing, List<GameEvent> events)
    {
        if (!monster.Definition.Snatches || State.Heroes.FirstOrDefault(hero => hero.GrabbedBy == monster.Id && HeldBy(hero) == monster) is not { } held)
        {
            return false;
        }

        bool Swimmable(GridPosition tile) => tile == monster.Position
            || (room.Layout.Contains(tile) && room.Layout[tile] is TileKind.DeepWater or TileKind.ShallowWater && (tile == held.Position || !IsOccupied(room, tile)));
        PathSearch search = PathSearch.Run(monster.Position, Swimmable, canPass: _ => true);
        List<Hero> others = [.. standing.Where(hero => hero != held)];
        int Away(GridPosition tile) => others.Count == 0 ? 0 : others.Min(hero => hero.Position.ChebyshevDistanceTo(tile));
        GridPosition? best = search.Distances.Keys
            .Where(tile => tile != held.Position && search.Distances[tile] <= monster.MovesThisTurn && room.Layout[tile] == TileKind.DeepWater)
            .OrderByDescending(Away).ThenBy(tile => search.Distances[tile]).ThenBy(tile => tile.Y).ThenBy(tile => tile.X)
            .Select(tile => (GridPosition?)tile).FirstOrDefault();
        if (best is { } end && end != monster.Position && Away(end) > Away(monster.Position))
        {
            List<GridPosition> path = [.. search.PathTo(end)];
            GridPosition trail = path.Count > 1 ? path[^2] : monster.Position;
            monster.Position = end;
            events.Add(new MonsterMoved(monster.Id, path));
            if (trail != held.Position)
            {
                GridPosition from = held.Position;
                held.Position = trail;
                events.Add(new DraggedUnder(monster.Id, held.Id, from, trail));
            }
        }

        PullUnder(room, monster, held, events);
        return true;
    }

    /// <summary>Why the active hero can't try to break free of a snatch now, or null if they can.</summary>
    public string? CheckBreakFree()
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0, KeelingOver: false })
        {
            return "Finish the roll first.";
        }

        Hero hero = ActiveHero;
        if (hero.GripTotal <= 0 || HeldBy(hero) is null)
        {
            return "Nothing has hold of you.";
        }

        return AttackDiceLeft < 1 ? "You've no Rumble left to struggle with this turn." : null;
    }

    private string? Handle(BreakFree command, List<GameEvent> events)
    {
        if (CheckBreakFree() is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        Monster holder = HeldBy(hero)!;
        int dice = AttackDiceLeft;
        Turn.AttackDiceUsed += dice;
        DiceRoll roll = _dice.Roll(dice);
        bool free = roll.Total > hero.GripTotal;
        events.Add(new StruggledFree(hero.Id, holder.Id, roll, hero.GripTotal, free));
        if (free)
        {
            hero.GrabbedBy = null;
            hero.HeldUnder = false;
            hero.GripTotal = 0;
        }

        return null;
    }
}
