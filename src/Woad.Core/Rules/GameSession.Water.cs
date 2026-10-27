using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Swimmers and the things that live in the Sunken Temple. A swimmer moves through water as easily
/// as floor; one in deep water is submerged, reached only by blows from right beside it (or a
/// trident): ranged attacks, spells and blasts pass over it. Shells flip when shoved, and a flipped
/// shell gives no armor. Tongues and hooks pull a hero they hit to the monster's side.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>A swimmer in deep water, out of reach of anything but a blow from beside it.</summary>
    public bool IsSubmerged(Monster monster) =>
        State.Rooms.Values.FirstOrDefault(room => room.FindMonster(monster.Id) is not null) is { } room && Submerged(room, monster);

    private static bool Submerged(Room room, Monster monster) =>
        monster.Definition.Swimmer && !monster.Collapsed && room.Layout[monster.Position] == TileKind.DeepWater;

    /// <summary>A spell reaches a monster unless it's submerged and not right beside the caster.</summary>
    private static bool SpellReaches(Hero hero, Room room, Monster monster) =>
        !Submerged(room, monster) || monster.NearestTile(hero.Position).ChebyshevDistanceTo(hero.Position) <= 1;

    /// <summary>A monster's armor: none while a shelled one lies flipped on its back.</summary>
    private static int ArmorOf(Monster monster) =>
        monster.Definition.Shell && monster.Prone ? 0 : Math.Max(0, monster.Definition.Armor - monster.Conditions[Condition.Corroded]);

    /// <summary>What a step onto <paramref name="tile"/> costs a hero: water is no harder than floor in swimming gear.</summary>
    private static int HeroStepCost(Hero hero, Room room, GridPosition tile) =>
        (hero.Swims && InWater(room, tile) ? 1 : room.Layout[tile].MoveCost()) + (room.PlantsAgainst(tile, heroes: true) ? 1 : 0);

    /// <summary>
    /// Drags a hero straight toward the monster that hit them, tile by tile, until they're beside it
    /// or something is in the way. A pit or a trap on the way takes them as a shove would.
    /// </summary>
    private void Pull(Room room, Monster monster, Hero hero, List<GameEvent> events)
    {
        GridPosition at = monster.Position;
        if (hero.Position.X != at.X && hero.Position.Y != at.Y)
        {
            return;
        }

        int dx = Math.Sign(at.X - hero.Position.X), dy = Math.Sign(at.Y - hero.Position.Y);
        GridPosition to = hero.Position;
        while (true)
        {
            var next = new GridPosition(to.X + dx, to.Y + dy);
            if (next == at || IsOccupied(room, next))
            {
                break;
            }

            TileKind tile = room.Layout[next];
            if (!(tile.IsWalkable() || tile is TileKind.DeepWater or TileKind.Pit))
            {
                break;
            }

            to = next;
            if (tile == TileKind.Pit || room.TrapAt(next) is not null)
            {
                break;
            }
        }

        if (to == hero.Position)
        {
            return;
        }

        events.Add(new Pulled(monster.Id, hero.Id, hero.Position, to));
        PushHero(null, room, hero, to, events, announce: false);
    }
}
