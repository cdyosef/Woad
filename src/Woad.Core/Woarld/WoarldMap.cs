using System.Text.Json;
using System.Text.Json.Serialization;
using Woad.Core.Content;
using Woad.Core.Grid;

namespace Woad.Core.Woarld;

/// <summary>What a tile of the Woarld Map is. Mountains, water and Parts Unknown can't be walked.</summary>
public enum Terrain
{
    Grass,
    Forest,
    Hills,
    Mountain,
    Water,
    Road,
    Bridge,
    Sand,

    /// <summary>Parts Unknown: areas of the Woarld not made yet.</summary>
    Unknown,
}

public enum PlaceKind
{
    Dungeon,
    Tavern,
    Town,
}

/// <summary>A place on the map: a dungeon to raid, a tavern or a town.</summary>
public sealed record WoarldPlace
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public PlaceKind Kind { get; init; }

    /// <summary>The picture the map draws it with (the art is named place_{icon}).</summary>
    public string Icon { get; init; } = "";

    public int X { get; init; }

    public int Y { get; init; }

    /// <summary>A dungeon's theme (see <see cref="ThemeDefinition"/>).</summary>
    public string? ThemeId { get; init; }

    /// <summary>The dungeon (theme id) to beat before this one opens; null if it's open from the start.</summary>
    public string? UnlockedBy { get; init; }

    public string Description { get; init; } = "";

    [JsonIgnore]
    public GridPosition Position => new(X, Y);
}

/// <summary>Text written across the map, such as "Parts Unknown".</summary>
public sealed record WoarldLabel(int X, int Y, string Text);

/// <summary>
/// A Woarld Map: a grid of terrain, drawn in its data file as rows of characters (see
/// <see cref="Legend"/>), and the places on it.
/// </summary>
public sealed class WoarldMap
{
    private const string DefaultResource = "Woad.Core.Content.Data.Woarlds.the_woarld.json";

    /// <summary>The characters the data file draws terrain with.</summary>
    public static IReadOnlyDictionary<char, Terrain> Legend { get; } = new Dictionary<char, Terrain>
    {
        ['.'] = Terrain.Grass,
        ['f'] = Terrain.Forest,
        ['h'] = Terrain.Hills,
        ['^'] = Terrain.Mountain,
        ['~'] = Terrain.Water,
        ['='] = Terrain.Road,
        ['b'] = Terrain.Bridge,
        [':'] = Terrain.Sand,
        ['?'] = Terrain.Unknown,
    };

    private static readonly Lazy<WoarldMap> Default = new(() =>
    {
        using Stream stream = typeof(WoarldMap).Assembly.GetManifestResourceStream(DefaultResource)
            ?? throw new InvalidContentException([$"Missing embedded content file '{DefaultResource}'."]);
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), ContentCatalog.LoadDefault());
    });

    private readonly Terrain[,] _terrain;

    private WoarldMap(string id, string name, Terrain[,] terrain, GridPosition start, IReadOnlyList<WoarldPlace> places, IReadOnlyList<WoarldLabel> labels)
    {
        Id = id;
        Name = name;
        _terrain = terrain;
        Start = start;
        Places = places;
        Labels = labels;
    }

    public string Id { get; }

    public string Name { get; }

    public int Width => _terrain.GetLength(0);

    public int Height => _terrain.GetLength(1);

    /// <summary>Where a party first sets out from.</summary>
    public GridPosition Start { get; }

    public IReadOnlyList<WoarldPlace> Places { get; }

    public IReadOnlyList<WoarldLabel> Labels { get; }

    /// <summary>The first Woarld, the one every mode uses for now.</summary>
    public static WoarldMap LoadDefault() => Default.Value;

    public bool InBounds(GridPosition tile) => tile.X >= 0 && tile.Y >= 0 && tile.X < Width && tile.Y < Height;

    /// <summary>The terrain at a tile; outside the map is Parts Unknown.</summary>
    public Terrain TerrainAt(GridPosition tile) => InBounds(tile) ? _terrain[tile.X, tile.Y] : Terrain.Unknown;

    public bool IsWalkable(GridPosition tile) => TerrainAt(tile) is not (Terrain.Mountain or Terrain.Water or Terrain.Unknown);

    public WoarldPlace? PlaceAt(GridPosition tile) => Places.FirstOrDefault(place => place.Position == tile);

    /// <exception cref="InvalidContentException">The map's data doesn't make a playable map.</exception>
    public static WoarldMap Parse(string json, ContentCatalog content)
    {
        MapData data;
        try
        {
            data = JsonSerializer.Deserialize<MapData>(json, ContentJson.Options) ?? throw new JsonException("The file is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidContentException([$"Woarld map: {exception.Message}"]);
        }

        var errors = new List<string>();
        int width = data.Rows.Count > 0 ? data.Rows[0].Length : 0;
        var terrain = new Terrain[width, data.Rows.Count];
        for (int y = 0; y < data.Rows.Count; y++)
        {
            if (data.Rows[y].Length != width)
            {
                errors.Add($"Woarld map {data.Id}: row {y} is {data.Rows[y].Length} tiles wide, not {width}.");
                continue;
            }

            for (int x = 0; x < width; x++)
            {
                if (Legend.TryGetValue(data.Rows[y][x], out Terrain kind))
                {
                    terrain[x, y] = kind;
                }
                else
                {
                    errors.Add($"Woarld map {data.Id}: '{data.Rows[y][x]}' at ({x}, {y}) isn't in the legend.");
                }
            }
        }

        var map = new WoarldMap(data.Id, data.Name, terrain, new GridPosition(data.Start.X, data.Start.Y), data.Places, data.Labels);
        if (errors.Count == 0)
        {
            if (!map.IsWalkable(map.Start))
            {
                errors.Add($"Woarld map {data.Id}: the start ({map.Start.X}, {map.Start.Y}) can't be walked on.");
            }

            foreach (WoarldPlace place in data.Places)
            {
                if (!map.IsWalkable(place.Position))
                {
                    errors.Add($"Woarld map {data.Id}: {place.Name} sits on {map.TerrainAt(place.Position)}.");
                }

                if (place.Kind == PlaceKind.Dungeon && (place.ThemeId is null || !content.Themes.ContainsKey(place.ThemeId)))
                {
                    errors.Add($"Woarld map {data.Id}: {place.Name} has no dungeon theme \"{place.ThemeId}\".");
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidContentException(errors);
        }

        return map;
    }

    private sealed record MapData
    {
        public required string Id { get; init; }

        public required string Name { get; init; }

        public required PositionData Start { get; init; }

        /// <summary>A note on the characters, for whoever edits the file; the game uses <see cref="Legend"/>.</summary>
        public string Legend { get; init; } = "";

        public required IReadOnlyList<string> Rows { get; init; }

        public IReadOnlyList<WoarldPlace> Places { get; init; } = [];

        public IReadOnlyList<WoarldLabel> Labels { get; init; } = [];
    }

    private sealed record PositionData(int X, int Y);
}
