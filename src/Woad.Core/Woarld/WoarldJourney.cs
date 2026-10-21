using System.Text.Json;
using Woad.Core.Grid;
using Woad.Core.Randomness;

namespace Woad.Core.Woarld;

/// <summary>What a step on the map led to.</summary>
/// <param name="Error">Why the party couldn't step there, or null if it did.</param>
/// <param name="NewDay">The step began a new day.</param>
/// <param name="NightFell">The step brought night.</param>
/// <param name="Arrived">The place the leader stepped onto, if any.</param>
public sealed record StepResult(string? Error = null, bool NewDay = false, bool NightFell = false, WoarldPlace? Arrived = null)
{
    public bool Moved => Error is null;
}

/// <summary>What food and drink add to a hero's Rest roll: extra dice, and a bonus to the total.</summary>
public readonly record struct RestBoost(int Dice, int Bonus)
{
    public bool IsNone => Dice == 0 && Bonus == 0;
}

/// <summary>One hero's Rest roll: the dice rolled, the bonus added, and the hearts it heals.</summary>
public sealed record RestRoll(IReadOnlyList<int> Dice, int Hearts, int Bonus = 0)
{
    /// <summary>Most dice one Rest roll can chain into, so a lucky streak still ends.</summary>
    public const int MaxDice = 12;

    /// <summary>
    /// Rolls a d6; a 6 heals a heart and rolls again. Every 10 of the total heals a heart too.
    /// Food and drink can start the roll with <paramref name="extraDice"/> and add <paramref name="bonus"/> to the total.
    /// </summary>
    public static RestRoll Roll(IRandomSource random, int extraDice = 0, int bonus = 0)
    {
        var dice = new List<int>();
        int toRoll = 1 + Math.Max(0, extraDice);
        while (toRoll > 0 && dice.Count < MaxDice)
        {
            int die = random.NextInt(1, 7);
            dice.Add(die);
            toRoll--;
            if (die == 6)
            {
                toRoll++;
            }
        }

        return new RestRoll(dice, dice.Count(die => die == 6) + (dice.Sum() + Math.Max(0, bonus)) / 10, Math.Max(0, bonus));
    }
}

/// <summary>
/// A party's travels on a Woarld Map: where the leader stands and the line the others follow
/// in, how much of the map the clouds have cleared from, the time of day, and which dungeons
/// have been beaten (which opens others). Saved between sessions.
/// </summary>
public sealed class WoarldJourney
{
    public const int StepsPerDay = 1000;

    /// <summary>Steps of daylight; the rest of the day's steps are night.</summary>
    public const int DaylightSteps = 500;

    public const int RestSteps = 50;

    /// <summary>How far around the leader the clouds clear.</summary>
    public const int SightRadius = 3;

    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly bool[,] _revealed;
    private PartyLine _line;
    private readonly HashSet<string> _beaten = [];

    private WoarldJourney(WoarldMap map)
    {
        Map = map;
        _revealed = new bool[map.Width, map.Height];
        _line = new PartyLine(map.Start);
    }

    public WoarldMap Map { get; }

    public GridPosition Leader => _line.Leader;

    /// <summary>Where the followers stand, nearest the leader first.</summary>
    public IReadOnlyList<GridPosition> Trail => _line.Trail;

    public int Day { get; private set; } = 1;

    /// <summary>Steps walked today: daylight for the first <see cref="DaylightSteps"/>, then night.</summary>
    public int StepsToday { get; private set; }

    public int TotalSteps { get; private set; }

    public bool IsDay => StepsToday < DaylightSteps;

    /// <summary>Steps until night falls, or until the next day dawns at night.</summary>
    public int StepsUntilChange => IsDay ? DaylightSteps - StepsToday : StepsPerDay - StepsToday;

    /// <summary>The dungeons (theme ids) the party has beaten.</summary>
    public IReadOnlyCollection<string> Beaten => _beaten;

    /// <summary>A new journey from the map's start, with the open dungeons' sites already known.</summary>
    public static WoarldJourney Begin(WoarldMap map, int partySize)
    {
        var journey = new WoarldJourney(map);
        journey.SetPartySize(partySize);
        journey.Reveal(map.Start, SightRadius);
        foreach (WoarldPlace place in map.Places.Where(journey.IsOpen))
        {
            journey.Reveal(place.Position, 1);
        }

        return journey;
    }

    public bool IsRevealed(GridPosition tile) => Map.InBounds(tile) && _revealed[tile.X, tile.Y];

    /// <summary>A place is open once the dungeon it waits on has been beaten.</summary>
    public bool IsOpen(WoarldPlace place) => place.UnlockedBy is null || _beaten.Contains(place.UnlockedBy);

    /// <summary>The place the leader stands on, if any.</summary>
    public WoarldPlace? PlaceHere => Map.PlaceAt(Leader);

    /// <summary>Puts the party at a place (a new raid sets out from the inn), the line gathered there.</summary>
    public void ReturnTo(GridPosition tile)
    {
        if (!Map.IsWalkable(tile))
        {
            throw new ArgumentException("The party can't stand there.", nameof(tile));
        }

        int size = Trail.Count + 1;
        _line = new PartyLine(tile);
        _line.SetSize(size);
        Reveal(tile, SightRadius);
    }

    /// <summary>Makes room in the line for this many heroes (the leader and their followers).</summary>
    public void SetPartySize(int size) => _line.SetSize(size);

    /// <summary>The leader steps one tile; everyone else steps into the place of the hero ahead.</summary>
    public StepResult Step(Direction direction)
    {
        GridPosition next = Leader.Step(direction);
        if (!Map.InBounds(next) || Map.TerrainAt(next) == Terrain.Unknown)
        {
            return new StepResult("Parts Unknown: nobody knows what lies that way yet.");
        }

        if (!Map.IsWalkable(next))
        {
            return new StepResult(Map.TerrainAt(next) == Terrain.Water ? "Water blocks the way. Find a bridge." : "The mountains are too steep to cross.");
        }

        _line.MoveTo(next);
        Reveal(next, SightRadius);
        (bool newDay, bool nightFell) = PassSteps(1);
        return new StepResult(NewDay: newDay, NightFell: nightFell, Arrived: Map.PlaceAt(next));
    }

    /// <summary>
    /// The shortest walk from the leader to a tile, over ground the clouds have cleared from: the
    /// steps to take in order (none if the leader is there). Null if there's no way.
    /// </summary>
    public IReadOnlyList<Direction>? PathTo(GridPosition target) =>
        IsRevealed(target) && Map.IsWalkable(target) ? PartyLine.PathTo(Leader, target, tile => IsRevealed(tile) && Map.IsWalkable(tile)) : null;

    /// <summary>
    /// The party rests for <see cref="RestSteps"/> steps: everyone gets a Rest roll, in party order.
    /// <paramref name="boosts"/> holds what food and drink add to each hero's roll (by party index).
    /// </summary>
    public (IReadOnlyList<RestRoll> Rolls, bool NewDay, bool NightFell) Rest(IRandomSource random, int partySize, IReadOnlyList<RestBoost>? boosts = null)
    {
        var rolls = Enumerable.Range(0, partySize)
            .Select(hero => boosts is not null && hero < boosts.Count ? RestRoll.Roll(random, boosts[hero].Dice, boosts[hero].Bonus) : RestRoll.Roll(random))
            .ToList();
        (bool newDay, bool nightFell) = PassSteps(RestSteps);
        return (rolls, newDay, nightFell);
    }

    /// <summary>Raiding a dungeon takes the rest of the day: the party comes out at dawn of the next.</summary>
    public void PassDay()
    {
        Day++;
        StepsToday = 0;
    }

    /// <summary>A dungeon was beaten: the places waiting on it open, and their sites are known.</summary>
    /// <returns>The places that just opened.</returns>
    public IReadOnlyList<WoarldPlace> MarkBeaten(string themeId)
    {
        var closed = Map.Places.Where(place => !IsOpen(place)).ToList();
        _beaten.Add(themeId);
        var opened = closed.Where(IsOpen).ToList();
        foreach (WoarldPlace place in opened)
        {
            Reveal(place.Position, 1);
        }

        return opened;
    }

    public string ToJson()
    {
        var rows = new List<string>();
        for (int y = 0; y < Map.Height; y++)
        {
            rows.Add(new string([.. Enumerable.Range(0, Map.Width).Select(x => _revealed[x, y] ? '#' : '.')]));
        }

        return JsonSerializer.Serialize(new SaveData
        {
            Version = CurrentVersion,
            WoarldId = Map.Id,
            Leader = Leader,
            Trail = [.. Trail],
            Day = Day,
            StepsToday = StepsToday,
            TotalSteps = TotalSteps,
            Beaten = [.. _beaten.Order(StringComparer.Ordinal)],
            Revealed = rows,
        }, Options);
    }

    /// <exception cref="InvalidDataException">Not a journey on this map that this version can read.</exception>
    public static WoarldJourney FromJson(string json, WoarldMap map)
    {
        SaveData data;
        try
        {
            data = JsonSerializer.Deserialize<SaveData>(json, Options) ?? throw new JsonException("The file is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The Woarld save is damaged: {exception.Message}", exception);
        }

        if (data.Version != CurrentVersion || data.WoarldId != map.Id)
        {
            throw new InvalidDataException("The Woarld save is from another version of the game.");
        }

        if (!map.IsWalkable(data.Leader) || data.Trail.Any(tile => !map.IsWalkable(tile)) || data.Revealed.Count != map.Height || data.Revealed.Any(row => row.Length != map.Width))
        {
            throw new InvalidDataException("The Woarld save doesn't fit the map.");
        }

        var journey = new WoarldJourney(map)
        {
            Day = Math.Max(1, data.Day),
            StepsToday = Math.Clamp(data.StepsToday, 0, StepsPerDay - 1),
            TotalSteps = Math.Max(0, data.TotalSteps),
        };
        journey._line = PartyLine.Restore(data.Leader, data.Trail);
        journey._beaten.UnionWith(data.Beaten);
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                journey._revealed[x, y] = data.Revealed[y][x] == '#';
            }
        }

        return journey;
    }

    /// <summary>Clears every cloud (tests check the whole map this way).</summary>
    internal void RevealAll()
    {
        for (int y = 0; y < Map.Height; y++)
        {
            for (int x = 0; x < Map.Width; x++)
            {
                _revealed[x, y] = true;
            }
        }
    }

    /// <summary>Clears the clouds within <paramref name="radius"/> of a tile (exploring, or a rumor of what's there).</summary>
    public void Reveal(GridPosition center, int radius)
    {
        for (int y = center.Y - radius; y <= center.Y + radius; y++)
        {
            for (int x = center.X - radius; x <= center.X + radius; x++)
            {
                var tile = new GridPosition(x, y);
                // A rough circle: the square's corners stay clouded.
                if (Map.InBounds(tile) && (x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y) <= radius * radius + 1)
                {
                    _revealed[x, y] = true;
                }
            }
        }
    }

    private (bool NewDay, bool NightFell) PassSteps(int steps)
    {
        bool wasDay = IsDay;
        int dayBefore = Day;
        TotalSteps += steps;
        StepsToday += steps;
        while (StepsToday >= StepsPerDay)
        {
            StepsToday -= StepsPerDay;
            Day++;
        }

        return (Day > dayBefore, wasDay && !IsDay && Day == dayBefore);
    }

    private sealed record SaveData
    {
        public int Version { get; init; }

        public string WoarldId { get; init; } = "";

        public GridPosition Leader { get; init; }

        public List<GridPosition> Trail { get; init; } = [];

        public int Day { get; init; }

        public int StepsToday { get; init; }

        public int TotalSteps { get; init; }

        public List<string> Beaten { get; init; } = [];

        public List<string> Revealed { get; init; } = [];
    }
}
