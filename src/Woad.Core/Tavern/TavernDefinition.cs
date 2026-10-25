using System.Text.Json;
using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Woarld;

namespace Woad.Core.Tavern;

/// <summary>What a tavern's folk do for the party when spoken to.</summary>
public enum NpcRole
{
    /// <summary>Food and drink, rounds for the party and the bar.</summary>
    Bartender,

    /// <summary>Buys and sells gear, and keeps what's sold to them.</summary>
    Merchant,

    /// <summary>Companions for hire; better ones as the party's reputation grows.</summary>
    Recruiter,

    /// <summary>Rooms, and swapping party members with the roster.</summary>
    Innkeeper,

    /// <summary>Dice games for gold.</summary>
    Gambler,

    /// <summary>Just a customer, with rumors.</summary>
    Patron,

    /// <summary>Craven the Cowherder: honors the cowardly with the Coward's Cloak.</summary>
    Cowherder,
}

/// <summary>Something an NPC has heard. Some are true; a rumor with <see cref="Reveal"/> clears the clouds from a spot on the map.</summary>
public sealed record Rumor(string Text, RumorReveal? Reveal = null);

public sealed record RumorReveal(int X, int Y, int Radius = 2);

public sealed record TavernNpc
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public NpcRole Role { get; init; }

    public int X { get; init; }

    public int Y { get; init; }

    public Appearance Appearance { get; init; } = Appearance.Default;

    public string Greeting { get; init; } = "";

    public IReadOnlyList<Rumor> Rumors { get; init; } = [];

    public GridPosition Position => new(X, Y);
}

/// <summary>Food or drink from the bar: eaten now for the next Rest, or bought To-Go for one later.</summary>
public sealed record MenuItem
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>"food" or "drink".</summary>
    public string Kind { get; init; } = "food";

    public int Price { get; init; }

    /// <summary>Extra dice on the next Rest roll.</summary>
    public int RestDice { get; init; }

    /// <summary>Added to the next Rest roll's total.</summary>
    public int RestBonus { get; init; }

    public string Text { get; init; } = "";

    public RestBoost Boost => new(RestDice, RestBonus);
}

/// <summary>
/// A tavern: its floor plan (drawn in rows of characters, see <see cref="Legend"/>), its folk,
/// the bar's menu, the merchant's first stock and the names of folk who can be hired.
/// </summary>
public sealed record TavernDefinition
{
    private const string ResourcePrefix = "Woad.Core.Content.Data.Taverns.";

    /// <summary>
    /// # wall, . planks, B bar, K barrel, F hearth, S stairs, s stool, T table, G dice table,
    /// d desk, C stall, R rug, D the door out. Planks, rugs and the door can be walked on.
    /// </summary>
    public const string Legend = "#.BKFSsTGdCRD";

    /// <summary>Counters NPCs serve across: the party can talk to someone with one of these between them.</summary>
    private const string Counters = "BCdG";

    private static readonly Lazy<TavernDefinition> Default = new(() => Load("waylay_inn.json"));

    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The Woarld Map place this tavern is.</summary>
    public required string PlaceId { get; init; }

    public required IReadOnlyList<string> Rows { get; init; }

    public required GridPosition Start { get; init; }

    public IReadOnlyList<TavernNpc> Npcs { get; init; } = [];

    public IReadOnlyList<MenuItem> Menu { get; init; } = [];

    /// <summary>The price of a round for everyone in the bar.</summary>
    public int RoundForTheBar { get; init; } = 10;

    /// <summary>The merchant's stock before anyone sells them anything.</summary>
    public IReadOnlyList<string> Stock { get; init; } = [];

    public IReadOnlyList<string> RecruitNames { get; init; } = [];

    public int Width => Rows.Count > 0 ? Rows[0].Length : 0;

    public int Height => Rows.Count;

    /// <summary>The Waylay Inn.</summary>
    public static TavernDefinition LoadDefault() => Default.Value;

    public char TileAt(GridPosition tile) =>
        tile.Y >= 0 && tile.Y < Height && tile.X >= 0 && tile.X < Width ? Rows[tile.Y][tile.X] : '#';

    public TavernNpc? NpcAt(GridPosition tile) => Npcs.FirstOrDefault(npc => npc.Position == tile);

    /// <summary>Open floor nobody's standing on.</summary>
    public bool IsWalkable(GridPosition tile) => TileAt(tile) is '.' or 'R' or 'D' && NpcAt(tile) is null;

    public bool IsDoor(GridPosition tile) => TileAt(tile) == 'D';

    /// <summary>
    /// The NPC the party can talk to from <paramref name="tile"/>: one standing next to it
    /// (diagonals too), or two tiles straight across a counter. The nearest wins.
    /// </summary>
    public TavernNpc? NpcInReach(GridPosition tile)
    {
        TavernNpc? near = Npcs.FirstOrDefault(npc => npc.Position.ChebyshevDistanceTo(tile) == 1);
        if (near is not null)
        {
            return near;
        }

        return Npcs.FirstOrDefault(npc =>
            (npc.X == tile.X && Math.Abs(npc.Y - tile.Y) == 2 || npc.Y == tile.Y && Math.Abs(npc.X - tile.X) == 2)
            && Counters.Contains(TileAt(new GridPosition((npc.X + tile.X) / 2, (npc.Y + tile.Y) / 2))));
    }

    /// <summary>Where to stand to talk to an NPC: the nearest open tile from which they're in reach, or null.</summary>
    public GridPosition? TalkingSpot(TavernNpc npc, GridPosition from)
    {
        IEnumerable<GridPosition> spots = Enumerable.Range(npc.Y - 2, 5)
            .SelectMany(y => Enumerable.Range(npc.X - 2, 5).Select(x => new GridPosition(x, y)))
            .Where(spot => IsWalkable(spot) && NpcInReach(spot) == npc);
        return spots
            .Select(spot => (spot, path: PartyLine.PathTo(from, spot, IsWalkable)))
            .Where(option => option.path is not null)
            .OrderBy(option => option.path!.Count)
            .Select(option => (GridPosition?)option.spot)
            .FirstOrDefault();
    }

    public static TavernDefinition Load(string file)
    {
        using Stream stream = typeof(TavernDefinition).Assembly.GetManifestResourceStream(ResourcePrefix + file)
            ?? throw new InvalidContentException([$"Missing embedded content file '{ResourcePrefix + file}'."]);
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), ContentCatalog.LoadDefault());
    }

    /// <exception cref="InvalidContentException">The tavern's data doesn't make a playable tavern.</exception>
    public static TavernDefinition Parse(string json, ContentCatalog content)
    {
        TavernDefinition tavern;
        try
        {
            tavern = JsonSerializer.Deserialize<TavernDefinition>(json, ContentJson.Options) ?? throw new JsonException("The file is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidContentException([$"Tavern: {exception.Message}"]);
        }

        var errors = new List<string>();
        for (int y = 0; y < tavern.Height; y++)
        {
            if (tavern.Rows[y].Length != tavern.Width)
            {
                errors.Add($"Tavern {tavern.Id}: row {y} is {tavern.Rows[y].Length} tiles wide, not {tavern.Width}.");
            }
            else if (tavern.Rows[y].FirstOrDefault(tile => !Legend.Contains(tile)) is var odd and not '\0')
            {
                errors.Add($"Tavern {tavern.Id}: '{odd}' in row {y} isn't in the legend.");
            }
        }

        if (!tavern.IsWalkable(tavern.Start))
        {
            errors.Add($"Tavern {tavern.Id}: the start can't be walked on.");
        }

        foreach (TavernNpc npc in tavern.Npcs)
        {
            if (tavern.TileAt(npc.Position) is not ('.' or 'R' or 's'))
            {
                errors.Add($"Tavern {tavern.Id}: {npc.Name} stands on '{tavern.TileAt(npc.Position)}'.");
            }
        }

        errors.AddRange(tavern.Stock.Where(card => content.FindCard(card) is null).Select(card => $"Tavern {tavern.Id}: the merchant stocks unknown card '{card}'."));
        if (errors.Count > 0)
        {
            throw new InvalidContentException(errors);
        }

        return tavern;
    }
}
