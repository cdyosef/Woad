using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Grid;
using Woad.Core.Randomness;
using Woad.Core.Rules;

namespace Woad.Core.Woarld;

/// <summary>What a fight out on the Woarld Map is: where it is, who's in it, and whether the monsters strike first.</summary>
/// <param name="ThemeId">The nearest dungeon: its art and its loot.</param>
/// <param name="Monsters">Monster ids, the band's strongest first.</param>
/// <param name="MonstersFirst">An ambush the party didn't see coming, or a failed escape: the monsters act before anyone.</param>
/// <param name="Kind">What the fight was, so its end can be settled on the map: a band (<paramref name="BandId"/>), an ambush or a toll post (at <paramref name="Spot"/>).</param>
public sealed record SkirmishSpec(string ThemeId, Terrain Terrain, IReadOnlyList<string> Monsters, bool MonstersFirst, string Title,
    EncounterKind Kind = EncounterKind.Band, int BandId = 0, GridPosition Spot = default);

/// <summary>
/// A fight out on the Woarld Map: one open field shaped by the ground it's on (trees in a forest, rocks
/// in the hills, a river under a bridge), the party along one side and the band along the other. It ends
/// when the band is beaten (the party walks on) or the party falls.
/// </summary>
public static class Skirmish
{
    public const string RoomId = "field";

    private const int Width = 11;
    private const int Height = 9;

    public static RunSetup CreateRun(ContentCatalog content, SkirmishSpec spec, IReadOnlyList<HeroSetup> heroes, ulong seed)
    {
        var random = new Pcg32(seed, 21);
        char[,] tiles = Field(spec.Terrain, random);
        string[] rows = [.. Enumerable.Range(0, Height).Select(y => new string([.. Enumerable.Range(0, Width).Select(x => tiles[x, y])]))];

        // The party's starts down the left, the band's spawns down the right.
        int[] lanes = [4, 2, 6, 3, 5, 1, 7];
        for (int i = 0; i < 4; i++)
        {
            char[] row = rows[lanes[i]].ToCharArray();
            row[1 + i / 2] = (char)('1' + i);
            rows[lanes[i]] = new string(row);
        }

        RoomTemplate template = RoomTemplate.Parse(rows, _ => null);
        List<MonsterDefinition> band = [.. spec.Monsters.Select(content.GetMonster)];
        var spawns = new List<MonsterSpawn>();
        var taken = new HashSet<GridPosition>();
        foreach (MonsterDefinition monster in band)
        {
            GridPosition? spot = Enumerable.Range(0, Height * 4)
                .Select(n => new GridPosition(Width - 2 - n / Height, lanes[n % Height % lanes.Length]))
                .Where(tile => template.Layout.Contains(tile))
                .Select(tile => (GridPosition?)tile)
                .FirstOrDefault(tile => Fits(template.Layout, monster, tile!.Value, taken));
            if (spot is not { } at)
            {
                continue;
            }

            spawns.Add(new MonsterSpawn(at, monster));
            foreach (GridPosition covered in Footprint(monster, at))
            {
                taken.Add(covered);
            }
        }

        template = template.WithMonsters(spawns);
        return new RunSetup
        {
            Seed = seed,
            ThemeId = spec.ThemeId,
            Heroes = heroes,
            Rooms = [new RoomSetup(RoomId, template)],
            StartRoomId = RoomId,
            Skirmish = true,
            MonstersFirst = spec.MonstersFirst,
        };
    }

    /// <summary>The tiles a monster takes up standing on <paramref name="at"/> (its top-left one, for a big one).</summary>
    public static IEnumerable<GridPosition> Footprint(MonsterDefinition monster, GridPosition at) =>
        from dx in Enumerable.Range(0, monster.Size)
        from dy in Enumerable.Range(0, monster.Size)
        select new GridPosition(at.X + dx, at.Y + dy);

    private static bool Fits(RoomLayout layout, MonsterDefinition monster, GridPosition at, HashSet<GridPosition> taken) =>
        Footprint(monster, at).All(tile => layout.Contains(tile) && !taken.Contains(tile)
            && (layout[tile].IsWalkable(monster.Flying) || (monster.Swimmer && layout[tile] == TileKind.DeepWater)));

    /// <summary>The ground: '.' open, '#' a tree or a boulder, ':' rough, '=' deep water.</summary>
    private static char[,] Field(Terrain terrain, IRandomSource random)
    {
        var tiles = new char[Width, Height];
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                tiles[x, y] = '.';
            }
        }

        void Scatter(char what, int count)
        {
            for (int placed = 0; placed < count; placed++)
            {
                // Clear of the party's and the band's edges, so nobody starts walled in.
                int x = random.NextInt(3, Width - 3), y = random.NextInt(0, Height);
                tiles[x, y] = what;
            }
        }

        switch (terrain)
        {
            case Terrain.Forest:
                Scatter('#', 12);
                break;
            case Terrain.Hills:
                Scatter(':', 10);
                Scatter('#', 4);
                break;
            case Terrain.Sand:
                Scatter(':', 6);
                break;
            case Terrain.Bridge:
                // A river along the top and bottom; the bridge between.
                for (int x = 0; x < Width; x++)
                {
                    tiles[x, 0] = tiles[x, Height - 1] = '=';
                }

                break;
            case Terrain.Grass:
                Scatter('#', 3);
                break;
        }

        return tiles;
    }
}
