using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Randomness;

namespace Woad.Core.Woarld;

/// <summary>A band of monsters roaming the Woarld Map.</summary>
public sealed class Band
{
    public int Id { get; init; }

    public string Name { get; init; } = "";

    /// <summary>Monster ids, strongest first.</summary>
    public List<string> Monsters { get; init; } = [];

    public int X { get; set; }

    public int Y { get; set; }

    /// <summary>Out only by night; gone at dawn.</summary>
    public bool Nocturnal { get; init; }

    /// <summary>A wandering merchant this band has cornered, waiting to be saved; null if none.</summary>
    public string? Cornering { get; set; }

    /// <summary>Steps of time the cornered merchant has left before the band gets them.</summary>
    public int CorneredFor { get; set; }

    public GridPosition Position => new(X, Y);
}

/// <summary>A spot where something lies in wait, hidden until a Scout finds it (a Bridge Troll waits under a bridge).</summary>
public sealed class HiddenAmbush
{
    public int X { get; init; }

    public int Y { get; init; }

    public string Name { get; init; } = "";

    public List<string> Monsters { get; init; } = [];

    /// <summary>What a Scout or an ambush check has to reach to see it coming.</summary>
    public int Difficulty { get; init; }

    public bool Revealed { get; set; }

    public GridPosition Position => new(X, Y);
}

/// <summary>Guards (or bandits passing as guards) collecting a toll on a road or a bridge.</summary>
public sealed class TollPost
{
    public int X { get; init; }

    public int Y { get; init; }

    /// <summary>The truth: bandits posing as guards, or real guards of the Law.</summary>
    public bool Bandits { get; init; }

    /// <summary>The party has seen through them: they're known for what they are.</summary>
    public bool Known { get; set; }

    /// <summary>The party tried to suss them out already (once a post).</summary>
    public bool Sussed { get; set; }

    public GridPosition Position => new(X, Y);

    /// <summary>What the party sees: guards, unless they're known to be bandits.</summary>
    public bool ShowsAsBandits => Known && Bandits;
}

public enum EncounterKind
{
    /// <summary>A roaming band caught up with the party: Fight or Flee.</summary>
    Band,

    /// <summary>Something sprang out on the party: they fight, surprised unless they saw it coming.</summary>
    Ambush,

    /// <summary>A toll post: pay, turn back, fight, or suss them out.</summary>
    Toll,
}

/// <summary>What the party ran into, and whether the monsters have the jump on them.</summary>
public sealed record Encounter(EncounterKind Kind, string Title, string Text, Terrain Terrain, IReadOnlyList<string> Monsters, bool Surprised = false,
    Band? Band = null, HiddenAmbush? Ambush = null, TollPost? Toll = null);

/// <summary>
/// The dangers of the Woarld Map. Bands of monsters roam it (three by day, five by night), wandering
/// at random until the party (or a wandering merchant) comes within <see cref="HuntRange"/>, then
/// closing in; what they are depends on the ground they come from, or the nearest dungeon. Spots lie in
/// wait, hidden until a Scout finds them, and now and then something springs out of the night. Guards
/// (or bandits passing for them) hold toll posts on roads and bridges, and a Bridge Troll lurks under a
/// bridge or two. The further from the start and the darker the hour, the bigger the bands.
/// </summary>
public sealed class WoarldDangers
{
    public const int DayBands = 3;

    public const int NightBands = 5;

    /// <summary>A band this close goes for the party (or a merchant).</summary>
    public const int HuntRange = 5;

    /// <summary>Percent chance, each step of the way at night, that something springs out of the dark.</summary>
    public const int NightAmbushChance = 2;

    /// <summary>How far around the party a Scout looks.</summary>
    public const int ScoutRange = 6;

    /// <summary>A Scout gives this much to ambush checks for the rest of the day.</summary>
    public const int ScoutBonus = 2;

    /// <summary>Steps of time a Scout takes.</summary>
    public const int ScoutSteps = 50;

    /// <summary>Steps of time a cornered merchant holds out.</summary>
    public const int CorneredSteps = 100;

    /// <summary>Gold each hero pays at a toll post.</summary>
    public const int TollPerHero = 2;

    public List<Band> Bands { get; init; } = [];

    public List<HiddenAmbush> Ambushes { get; init; } = [];

    public List<TollPost> Tolls { get; init; } = [];

    /// <summary>The day the party last scouted: ambush checks get <see cref="ScoutBonus"/> until it ends.</summary>
    public int ScoutedDay { get; set; }

    public int NextBandId { get; set; } = 1;

    /// <summary>Nothing stirs: no bands, ambushes or tolls trouble the party (for tests that need a quiet road).</summary>
    public bool Calm { get; set; }

    /// <summary>The toll posts and hidden ambushes of a new Woarld (bands come as the days go by).</summary>
    public static WoarldDangers Create(WoarldMap map, ContentCatalog content, ulong seed)
    {
        var random = new Pcg32(seed, 31);
        var dangers = new WoarldDangers();
        List<GridPosition> tiles = AllTiles(map).Where(tile => map.IsWalkable(tile) && map.PlaceAt(tile) is null && tile.ChebyshevDistanceTo(map.Start) > 3).ToList();

        // A troll under a bridge or two; toll posts on other bridges and along the roads.
        List<GridPosition> bridges = tiles.Where(tile => map.TerrainAt(tile) == Terrain.Bridge).ToList();
        random.Shuffle(bridges);
        foreach (GridPosition bridge in bridges.Take(Math.Min(2, bridges.Count / 3 + 1)))
        {
            dangers.Ambushes.Add(new HiddenAmbush { X = bridge.X, Y = bridge.Y, Name = "a Bridge Troll", Monsters = ["bridge_troll"], Difficulty = 14 });
        }

        List<GridPosition> tollSpots = [.. bridges.Skip(2).Take(2), .. tiles.Where(tile => map.TerrainAt(tile) == Terrain.Road).OrderBy(_ => random.NextInt(0, 1_000_000)).Take(2)];
        foreach (GridPosition spot in tollSpots.Where(spot => dangers.Ambushes.All(ambush => ambush.Position.ChebyshevDistanceTo(spot) > 1)).Take(3))
        {
            dangers.Tolls.Add(new TollPost { X = spot.X, Y = spot.Y, Bandits = random.CoinFlip() });
        }

        // Spots lying in wait in the wilds: what lurks there depends on the ground.
        List<GridPosition> wilds = tiles.Where(tile => map.TerrainAt(tile) is Terrain.Forest or Terrain.Hills or Terrain.Grass or Terrain.Sand).ToList();
        random.Shuffle(wilds);
        foreach (GridPosition spot in wilds.Where(spot => dangers.Tolls.All(toll => toll.Position.ChebyshevDistanceTo(spot) > 2)).Take(12))
        {
            int strength = Strength(map, spot, night: false, partySize: 1);
            (string name, List<string> monsters) = Roving(map, content, spot, strength, random);
            dangers.Ambushes.Add(new HiddenAmbush { X = spot.X, Y = spot.Y, Name = name, Monsters = monsters, Difficulty = 8 + strength });
        }

        return dangers;
    }

    public HiddenAmbush? AmbushAt(GridPosition tile) => Ambushes.FirstOrDefault(ambush => ambush.Position == tile);

    public TollPost? TollAt(GridPosition tile) => Tolls.FirstOrDefault(toll => toll.Position == tile);

    /// <summary>A band standing on or right beside a tile.</summary>
    public Band? BandBeside(GridPosition tile) => Bands.FirstOrDefault(band => band.Position.ChebyshevDistanceTo(tile) <= 1);

    /// <summary>
    /// How strong the Woarld's dangers are here: more the further from the start, at night, and for a
    /// bigger party.
    /// </summary>
    public static int Strength(WoarldMap map, GridPosition at, bool night, int partySize) =>
        at.ChebyshevDistanceTo(map.Start) / 20 + (night ? 1 : 0) + partySize / 3;

    /// <summary>
    /// What roams (or lurks) here: near a dungeon, what comes out of it; otherwise what the ground
    /// breeds, with bandits on the roads. Bigger for more <paramref name="strength"/>.
    /// </summary>
    public static (string Name, List<string> Monsters) Roving(WoarldMap map, ContentCatalog content, GridPosition at, int strength, IRandomSource random)
    {
        WoarldPlace? dungeon = map.Places.Where(place => place.Kind == PlaceKind.Dungeon && place.ThemeId is not null)
            .OrderBy(place => place.Position.ChebyshevDistanceTo(at))
            .FirstOrDefault(place => place.Position.ChebyshevDistanceTo(at) <= 12);
        if (dungeon is not null && random.CoinFlip())
        {
            ThemeDefinition theme = content.GetTheme(dungeon.ThemeId!);
            List<string> roster = [.. theme.Roster.Where(entry => entry.MinFloor <= 1 + strength / 2)
                .Select(entry => entry.Monster)
                .Where(id => content.GetMonster(id) is { WaterOnly: false, Boss: false, Miniboss: false })];
            if (roster.Count > 0)
            {
                List<string> pack = [.. Enumerable.Range(0, 2 + strength).Select(_ => random.Pick(roster))];
                return ($"monsters out of {dungeon.Name}", pack);
            }
        }

        return map.TerrainAt(at) switch
        {
            Terrain.Forest => ("a pack of Dire Wolves", [.. Enumerable.Repeat("dire_wolf", 2 + strength)]),
            Terrain.Hills => ("Goblin Raiders", [.. Enumerable.Repeat("goblin_raider", 2 + strength)]),
            Terrain.Sand => ("Giant Scorpions", [.. Enumerable.Repeat("giant_scorpion", 1 + (strength + 1) / 2)]),
            Terrain.Road => ("Bandits", [.. Enumerable.Range(0, 2 + strength).Select(i => i % 3 == 2 ? "bandit_archer" : "bandit")]),
            _ => ("Wild Boars", [.. Enumerable.Repeat("wild_boar", 1 + strength)]),
        };
    }

    /// <summary>
    /// After the party steps: bands come and go to match the hour, roam, close in on the party (or a
    /// merchant) when near, and a cornered merchant's time runs on. Returns a band that caught up with
    /// the party, or something that sprang out of the night, or what lay in wait on the tile stepped
    /// onto, or the toll post there; null if nothing happened.
    /// </summary>
    /// <param name="merchants">The wandering merchants out today, by name and where they stand.</param>
    /// <param name="cornered">Merchants whose time ran out (a band got them).</param>
    public Encounter? AfterStep(WoarldJourney journey, ContentCatalog content, int partySize, int steps, IReadOnlyList<(string Name, GridPosition Tile)> merchants,
        IRandomSource random, List<string> cornered)
    {
        if (Calm)
        {
            return null;
        }

        WoarldMap map = journey.Map;
        GridPosition party = journey.Leader;
        bool night = !journey.IsDay;

        // Dawn sends the night's bands home.
        if (!night)
        {
            Bands.RemoveAll(band => band.Nocturnal && band.Cornering is null);
        }

        // Bands enough for the hour, coming in from out of sight.
        int target = night ? NightBands : DayBands;
        while (Bands.Count < target)
        {
            List<GridPosition> far = AllTiles(map).Where(tile => map.IsWalkable(tile) && map.PlaceAt(tile) is null && !journey.IsRevealed(tile)
                                                                 && tile.ChebyshevDistanceTo(party) is >= 10 and <= 25).ToList();
            if (far.Count == 0)
            {
                break;
            }

            GridPosition spot = random.Pick(far);
            (string name, List<string> monsters) = Roving(map, content, spot, Strength(map, spot, night, partySize), random);
            Bands.Add(new Band { Id = NextBandId++, Name = name, Monsters = monsters, X = spot.X, Y = spot.Y, Nocturnal = night });
        }

        foreach (Band band in Bands.ToList())
        {
            if (band.Cornering is not null)
            {
                band.CorneredFor -= steps;
                if (band.CorneredFor <= 0)
                {
                    cornered.Add(band.Cornering);
                    band.Cornering = null;
                }

                continue;
            }

            (string Name, GridPosition Tile)? prey = merchants
                .Where(merchant => merchant.Tile.ChebyshevDistanceTo(band.Position) <= HuntRange)
                .Select(merchant => ((string, GridPosition)?)merchant)
                .FirstOrDefault();
            GridPosition? goal = band.Position.ChebyshevDistanceTo(party) <= HuntRange ? party : prey?.Tile;
            GridPosition next = goal is { } toward ? StepToward(map, band.Position, toward) : Wander(map, band.Position, random);
            band.X = next.X;
            band.Y = next.Y;
            if (prey is { } merchant && goal == merchant.Tile && band.Position.ChebyshevDistanceTo(merchant.Tile) <= 1)
            {
                band.Cornering = merchant.Name;
                band.CorneredFor = CorneredSteps;
            }
        }

        if (TollAt(party) is { } toll)
        {
            return new Encounter(EncounterKind.Toll, toll.ShowsAsBandits ? "Bandits at a toll post" : "A toll post",
                toll.ShowsAsBandits
                    ? "The bandits posing as guards block the way, hands out for a toll."
                    : $"Guards block the way: \"Toll's {TollPerHero} gold a head to pass, by order of the Law.\"",
                map.TerrainAt(party), [], Toll: toll);
        }

        if (AmbushAt(party) is { } ambush)
        {
            return new Encounter(EncounterKind.Ambush, $"Ambush: {ambush.Name}!", $"{Capitalized(ambush.Name)} springs out!", map.TerrainAt(party), ambush.Monsters, Ambush: ambush);
        }

        if (Bands.FirstOrDefault(band => band.Cornering is null && band.Position.ChebyshevDistanceTo(party) <= 1) is { } caught)
        {
            return new Encounter(EncounterKind.Band, Capitalized(caught.Name), $"{Capitalized(caught.Name)} catch up with the party!", map.TerrainAt(party), caught.Monsters, Band: caught);
        }

        if (night && random.NextInt(0, 100) < NightAmbushChance)
        {
            int strength = Strength(map, party, night: true, partySize);
            (string name, List<string> monsters) = Roving(map, content, party, strength, random);
            var sudden = new HiddenAmbush { X = party.X, Y = party.Y, Name = name, Monsters = monsters, Difficulty = 9 + strength };
            return new Encounter(EncounterKind.Ambush, $"Ambush: {name}!", $"Out of the dark, {name}!", map.TerrainAt(party), monsters, Ambush: sudden);
        }

        return null;
    }

    /// <summary>
    /// A party check: the best of the party's dice for it, plus a die for each other hero (up to three
    /// more). <paramref name="dice"/> holds each hero's dice.
    /// </summary>
    public static int PartyRoll(IReadOnlyList<int> dice, IRandomSource random, out IReadOnlyList<int> faces)
    {
        int count = dice.Count == 0 ? 0 : dice.Max() + Math.Min(3, dice.Count - 1);
        int[] rolled = [.. Enumerable.Range(0, count).Select(_ => random.NextInt(1, 7))];
        faces = rolled;
        return rolled.Sum();
    }

    /// <summary>
    /// Scouting: a party Od Hand check. Every hidden ambush within <see cref="ScoutRange"/> it reaches is
    /// found, and ambush checks get <see cref="ScoutBonus"/> for the rest of the day.
    /// </summary>
    public IReadOnlyList<HiddenAmbush> Scout(WoarldJourney journey, IReadOnlyList<int> odHandDice, IRandomSource random, out int total)
    {
        total = PartyRoll(odHandDice, random, out _);
        ScoutedDay = journey.Day;
        var found = new List<HiddenAmbush>();
        foreach (HiddenAmbush ambush in Ambushes.Where(ambush => !ambush.Revealed && ambush.Position.ChebyshevDistanceTo(journey.Leader) <= ScoutRange))
        {
            if (total >= ambush.Difficulty)
            {
                ambush.Revealed = true;
                journey.Reveal(ambush.Position, 0);
                found.Add(ambush);
            }
        }

        return found;
    }

    /// <summary>
    /// An ambush: the party's Od Hand check (with today's Scout bonus) against how well it was hidden.
    /// True if they saw it coming. A spot the party already found can't take them by surprise.
    /// </summary>
    public bool SeesItComing(HiddenAmbush ambush, int day, IReadOnlyList<int> odHandDice, IRandomSource random, out int total)
    {
        total = PartyRoll(odHandDice, random, out _) + (ScoutedDay == day ? ScoutBonus : 0);
        return ambush.Revealed || total >= ambush.Difficulty;
    }

    /// <summary>Fleeing a band: the party's Wend check against the band's chase (two dice, and one more for each monster past the first).</summary>
    public static bool Flees(Band band, IReadOnlyList<int> wendDice, IRandomSource random, out int total, out int chase)
    {
        total = PartyRoll(wendDice, random, out _);
        chase = Enumerable.Range(0, 2 + Math.Max(0, band.Monsters.Count - 1)).Sum(_ => random.NextInt(1, 7));
        return total > chase;
    }

    /// <summary>
    /// Suss Out at a toll post: each hero's own Od Hand roll against the captain's hidden Convince roll:
    /// 5 and a die for real guards, 5 and three dice for bandits. If anyone matches it, the truth is out.
    /// </summary>
    public static bool SussOut(TollPost toll, IReadOnlyList<int> odHandDice, IRandomSource random)
    {
        if (toll.Sussed)
        {
            return toll.Known;
        }

        toll.Sussed = true;
        int convince = 5 + Enumerable.Range(0, toll.Bandits ? 3 : 1).Sum(_ => random.NextInt(1, 7));
        toll.Known = odHandDice.Any(dice => Enumerable.Range(0, dice).Sum(_ => random.NextInt(1, 7)) >= convince);
        return toll.Known;
    }

    /// <summary>A toll post's company: a Captain with spearmen and an archer, guards or (known) bandits.</summary>
    public static IReadOnlyList<string> TollCompany(TollPost toll) =>
        toll.ShowsAsBandits
            ? ["bandit_captain", "bandit_spearman", "bandit_spearman", "bandit_archer"]
            : ["guard_captain", "spear_guard", "spear_guard", "archer_guard"];

    private static GridPosition StepToward(WoarldMap map, GridPosition from, GridPosition to)
    {
        GridPosition best = from;
        foreach (Direction direction in Enum.GetValues<Direction>())
        {
            GridPosition next = from.Step(direction);
            if (map.IsWalkable(next) && map.PlaceAt(next) is null && next.ManhattanDistanceTo(to) < best.ManhattanDistanceTo(to))
            {
                best = next;
            }
        }

        return best;
    }

    private static GridPosition Wander(WoarldMap map, GridPosition from, IRandomSource random)
    {
        if (random.CoinFlip())
        {
            return from;
        }

        GridPosition next = from.Step((Direction)random.NextInt(0, 4));
        return map.IsWalkable(next) && map.PlaceAt(next) is null ? next : from;
    }

    private static IEnumerable<GridPosition> AllTiles(WoarldMap map) =>
        Enumerable.Range(0, map.Width).SelectMany(x => Enumerable.Range(0, map.Height).Select(y => new GridPosition(x, y)));

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
