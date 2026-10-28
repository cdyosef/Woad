using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Light and darkness. In a dark room a tile is in darkness unless it's within
/// <see cref="GameRules.LightRadius"/> tiles of a light: a held torch, burning floor, a burning or fiery
/// creature, a lit brazier, or someone under Shining Light. Ranged attacks on monsters in darkness are
/// harder, and Shadowbind bites harder there. Braziers light from any fire beside them, or fire aimed at them.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Whether a tile is in darkness: in a dark room, out of reach of every light.</summary>
    public bool InDarkness(Room room, GridPosition tile) =>
        room.Dark && !LightsIn(room).Any(light => light.ChebyshevDistanceTo(tile) <= GameRules.LightRadius);

    /// <summary>Where the light in a room comes from: fire, lit braziers and Shining Light.</summary>
    public IEnumerable<GridPosition> LightsIn(Room room) =>
        FiresIn(room)
            .Concat(room.LitBraziers)
            .Concat(State.HeroesIn(room).Where(hero => hero.ShiningRounds > 0).Select(hero => hero.Position));

    /// <summary>The fire in a room: held torches, burning floor, and burning or fiery creatures.</summary>
    private IEnumerable<GridPosition> FiresIn(Room room) =>
        State.HeroesIn(room).Where(hero => hero.TorchesHeld > 0 || hero.FireDice > 0).Select(hero => hero.Position)
            .Concat(room.Flames.Keys)
            .Concat(room.Monsters.Where(monster => monster.FireDice > 0 || monster.Definition.DieType == DieType.Fire).SelectMany(monster => monster.Tiles));

    /// <summary>After every command: an unlit brazier with fire beside it catches.</summary>
    private void LightBraziers(List<GameEvent> events)
    {
        foreach (Room room in State.Rooms.Values)
        {
            List<GridPosition> unlit = [.. room.TilesOfKind(TileKind.Brazier).Where(tile => !room.LitBraziers.Contains(tile))];
            if (unlit.Count == 0)
            {
                continue;
            }

            List<GridPosition> fires = [.. FiresIn(room)];
            foreach (GridPosition brazier in unlit.Where(tile => fires.Any(fire => fire.ChebyshevDistanceTo(tile) <= 1)))
            {
                Kindle(room, brazier, events);
            }
        }
    }

    /// <summary>Lights a brazier.</summary>
    private static void Kindle(Room room, GridPosition brazier, List<GameEvent> events)
    {
        if (room.LightBrazier(brazier))
        {
            events.Add(new BrazierLit(room.Id, brazier));
        }
    }

    /// <summary>An unlit brazier on the tile, for fire to be aimed at.</summary>
    private static bool UnlitBrazierAt(Room room, GridPosition tile) =>
        room.Layout.Contains(tile) && room.Layout[tile] == TileKind.Brazier && !room.LitBraziers.Contains(tile);
}
