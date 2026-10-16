using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Randomness;

namespace Woad.Core.Dungeon;

/// <summary>
/// Flooded themes (the Sunken Temple): built rooms get pools, shallow at the rim and deep in the
/// middle, some with a monster's spawn point under the water; the boss arena is a great pool around
/// the boss. Pools never cut a room up for those who walk, nor cover doorways, starts, stairs, the exit,
/// or the floor beside chests and pots.
/// </summary>
public static partial class DungeonGenerator
{
    private static RoomTemplate Flood(RoomTemplate template, RoomRole role, IReadOnlyDictionary<Direction, GridPosition> doorTiles, IRandomSource random)
    {
        // Hand-made layouts with water of their own, arrival rooms and vaults are left as drawn.
        if (role is RoomRole.Arrival or RoomRole.Vault
            || template.TilesOf(TileKind.ShallowWater).Any() || template.TilesOf(TileKind.DeepWater).Any())
        {
            return template;
        }

        HashSet<GridPosition> kept = [.. DoorAreas(template, doorTiles), .. template.HeroStarts.Values,
            .. template.Monsters.Select(spawn => spawn.Position),
            .. template.TilesOf(TileKind.Chest).Concat(template.TilesOf(TileKind.Pot)).SelectMany(tile => tile.OrthogonalNeighbors()),
            .. template.TilesOf(TileKind.StairsDown).Concat(template.TilesOf(TileKind.StairsUp)).Concat(template.TilesOf(TileKind.Exit)).SelectMany(tile => tile.SurroundingTiles())];

        if (role == RoomRole.Boss && template.BossSpawn is { } boss)
        {
            return Pool(template, boss, Math.Min(template.Layout.Width, template.Layout.Height) / 4.0, kept, spawnInside: false) ?? template;
        }

        int area = template.Layout.Positions.Count(tile => template.Layout[tile].IsWalkable());
        int pools = random.NextInt(1, 2 + area / 50);
        for (int i = 0, tries = 0; i < pools && tries < 12; tries++)
        {
            List<GridPosition> centers = template.TilesOf(TileKind.Floor)
                .Where(tile => !kept.Contains(tile) && tile.X > 1 && tile.Y > 1 && tile.X < template.Layout.Width - 2 && tile.Y < template.Layout.Height - 2)
                .ToList();
            if (centers.Count == 0)
            {
                break;
            }

            double radius = random.NextInt(0, 3) == 0 ? 2.3 : 1.5;
            if (Pool(template, random.Pick(centers), radius, kept, spawnInside: random.CoinFlip()) is { } flooded)
            {
                template = flooded;
                i++;
            }
        }

        return template;
    }

    /// <summary>
    /// A pool around <paramref name="center"/>: deep within <paramref name="radius"/> - 1, shallow out to
    /// <paramref name="radius"/>, on plain floor that isn't kept clear. Null if it would cut the room up.
    /// </summary>
    private static RoomTemplate? Pool(RoomTemplate template, GridPosition center, double radius, HashSet<GridPosition> kept, bool spawnInside)
    {
        RoomTemplate pool = template;
        foreach (GridPosition tile in template.Layout.Positions)
        {
            double distance = Math.Sqrt(Math.Pow(tile.X - center.X, 2) + Math.Pow(tile.Y - center.Y, 2));
            if (distance > radius || kept.Contains(tile) || template.Layout[tile] is not (TileKind.Floor or TileKind.Rubble))
            {
                continue;
            }

            pool = pool.WithTile(tile, distance <= radius - 1 ? TileKind.DeepWater : TileKind.ShallowWater);
        }

        if (pool.Layout[center] == TileKind.DeepWater && spawnInside && !pool.SpawnPoints.Contains(center))
        {
            pool = pool.WithSpawnPoints([.. pool.SpawnPoints, center]);
        }

        return Connected(pool) ? pool : null;
    }

    /// <summary>Whether a monster may be placed on this tile: a water-only swimmer in water, only flyers and swimmers in deep water.</summary>
    private static bool CanSpawnOn(MonsterDefinition monster, TileKind tile) =>
        monster.WaterOnly
            ? tile is TileKind.ShallowWater or TileKind.DeepWater
            : tile.IsWalkable(monster.Flying) || (monster.Swimmer && tile == TileKind.DeepWater);
}
