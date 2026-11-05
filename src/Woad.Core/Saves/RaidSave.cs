using System.Text.Json;
using System.Text.Json.Serialization;
using Woad.Core.Grid;
using Woad.Core.Rules;
using Woad.Core.Woarld;

namespace Woad.Core.Saves;

/// <summary>Where a raid was left: in the inn, or out on the Woarld Map.</summary>
public enum RaidLocation
{
    Inn,
    Map,
}

/// <summary>
/// One Raiding Party, saved as "[leader]'s Raid": who's in it and who leads, everyone's hearts
/// (they carry from one dungeon to the next), who rests in a room at the inn, the Party Chest, and where
/// and when the party is on the shared Woarld. Characters' own coins and gear are saved with the characters.
/// </summary>
public sealed class RaidSave
{
    /// <summary>A hero who falls in a dungeon is carried home, and starts again on this many hearts.</summary>
    public const int HeartsAfterFalling = 1;

    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = true,
    };

    public int Version { get; init; } = CurrentVersion;

    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>"[leader]'s Raid", named when the raid begins.</summary>
    public required string Name { get; init; }

    public DateTimeOffset SavedUtc { get; set; }

    /// <summary>The party's characters, in seat order.</summary>
    public List<Guid> Party { get; init; } = [];

    public Guid Leader { get; set; }

    public bool FriendlyFire { get; set; }

    /// <summary>Hearts each character has left; a character not listed is at full health.</summary>
    public Dictionary<Guid, int> Hearts { get; init; } = [];

    /// <summary>Characters resting in a room at the inn, and the day their healing was last counted.</summary>
    public Dictionary<Guid, int> Rooms { get; init; } = [];

    /// <summary>Characters bedded down free in the pig sty out back, and the day their rest was last counted.</summary>
    public Dictionary<Guid, int> Sty { get; init; } = [];

    /// <summary>The cards and coins the whole party shares (missing in raids saved before it: empty).</summary>
    public PartyChest Chest { get; init; } = new();

    public RaidLocation Location { get; set; } = RaidLocation.Inn;

    /// <summary>Where and when the party is on the Woarld Map; null until they first step out.</summary>
    public JourneyPlace? Place { get; set; }

    /// <summary>A character's hearts now, out of <paramref name="maxHearts"/>.</summary>
    public int HeartsOf(Guid character, int maxHearts) =>
        Math.Clamp(Hearts.TryGetValue(character, out int hearts) ? hearts : maxHearts, 1, Math.Max(1, maxHearts));

    /// <summary>Heals a character by up to <paramref name="amount"/> hearts; returns how many they gained.</summary>
    public int Heal(Guid character, int amount, int maxHearts)
    {
        int before = HeartsOf(character, maxHearts);
        int after = Math.Min(maxHearts, before + Math.Max(0, amount));
        SetHearts(character, after, maxHearts);
        return after - before;
    }

    public void SetHearts(Guid character, int hearts, int maxHearts)
    {
        if (hearts >= maxHearts)
        {
            Hearts.Remove(character);
        }
        else
        {
            Hearts[character] = Math.Max(1, hearts);
        }
    }

    /// <summary>A dungeon's toll: survivors keep the hearts they came out with; the fallen are carried home on <see cref="HeartsAfterFalling"/>.</summary>
    public void ApplyRun(IEnumerable<HeroRunResult> results, Func<Guid, int> maxHearts)
    {
        foreach (HeroRunResult result in results)
        {
            if (result.CharacterId is Guid character)
            {
                SetHearts(character, result.Status == Model.HeroStatus.Dead ? HeartsAfterFalling : result.Hearts, maxHearts(character));
            }
        }
    }

    /// <summary>A character goes up to a room: each night there they heal a heart, and make a Rest roll.</summary>
    public void RentRoom(Guid character, int day)
    {
        Sty.Remove(character);
        Rooms[character] = day;
    }

    /// <summary>A character beds down in the pig sty: each night there, a Rest roll and nothing more.</summary>
    public void SleepInSty(Guid character, int day)
    {
        Rooms.Remove(character);
        Sty[character] = day;
    }

    /// <summary>Counts the nights that have passed for everyone in a room or the sty.</summary>
    public void HealRooms(int day, Func<Guid, int> maxHearts)
    {
        foreach ((Guid character, int since) in Rooms.ToList())
        {
            Rest(character, since, day, room: true, maxHearts(character));
            Rooms[character] = Math.Max(since, day);
        }

        foreach ((Guid character, int since) in Sty.ToList())
        {
            Rest(character, since, day, room: false, maxHearts(character));
            Sty[character] = Math.Max(since, day);
        }
    }

    /// <summary>A character leaves their room or the sty (to rejoin the party), healed for the nights they rested.</summary>
    public void LeaveRoom(Guid character, int day, int maxHearts)
    {
        if (Rooms.Remove(character, out int since))
        {
            Rest(character, since, day, room: true, maxHearts);
        }

        if (Sty.Remove(character, out int sinceSty))
        {
            Rest(character, sinceSty, day, room: false, maxHearts);
        }
    }

    /// <summary>
    /// The nights from <paramref name="since"/> to <paramref name="day"/>: each a Rest roll, and in a room a
    /// heart besides. The rolls are fixed by who rests and the night, so counting them again changes nothing.
    /// </summary>
    private void Rest(Guid character, int since, int day, bool room, int maxHearts)
    {
        byte[] bytes = character.ToByteArray();
        ulong who = BitConverter.ToUInt64(bytes, 0) ^ BitConverter.ToUInt64(bytes, 8);
        for (int night = since + 1; night <= day; night++)
        {
            RestRoll roll = RestRoll.Roll(new Randomness.Pcg32(who ^ ((ulong)night * 0x9E3779B97F4A7C15UL), sequence: 21));
            Heal(character, (room ? 1 : 0) + roll.Hearts, maxHearts);
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <exception cref="InvalidDataException">Not a raid this version can read.</exception>
    public static RaidSave FromJson(string json)
    {
        RaidSave? raid;
        try
        {
            raid = JsonSerializer.Deserialize<RaidSave>(json, Options);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The raid is damaged: {exception.Message}", exception);
        }

        if (raid is null || raid.Version != CurrentVersion || raid.Party.Count == 0)
        {
            throw new InvalidDataException("The raid is from another version of the game.");
        }

        return raid;
    }
}

/// <summary>The saved raids, a file each in one folder (with the dungeon a raid left half-played beside it).</summary>
public sealed class RaidSaveStore(string folder)
{
    public string Folder { get; } = folder;

    /// <summary>Every raid that can be read, the most recently played first, and the files that couldn't.</summary>
    public (IReadOnlyList<RaidSave> Raids, IReadOnlyList<string> Problems) LoadAll()
    {
        var raids = new List<RaidSave>();
        var problems = new List<string>();
        if (!Directory.Exists(Folder))
        {
            return (raids, problems);
        }

        foreach (string file in Directory.GetFiles(Folder, "*.raid.json"))
        {
            try
            {
                raids.Add(RaidSave.FromJson(File.ReadAllText(file)));
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException)
            {
                problems.Add($"{Path.GetFileName(file)}: {exception.Message}");
            }
        }

        return ([.. raids.OrderByDescending(raid => raid.SavedUtc)], problems);
    }

    /// <summary>"[leader]'s Raid", numbered if the leader already has one.</summary>
    public string NameFor(string leader)
    {
        string name = $"{leader}'s Raid";
        HashSet<string> taken = [.. LoadAll().Raids.Select(raid => raid.Name)];
        for (int number = 2; taken.Contains(name); number++)
        {
            name = $"{leader}'s Raid ({number})";
        }

        return name;
    }

    public void Save(RaidSave raid)
    {
        raid.SavedUtc = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(Folder);
        string path = PathOf(raid);
        File.WriteAllText(path + ".tmp", raid.ToJson());
        File.Move(path + ".tmp", path, overwrite: true);
    }

    public void Delete(RaidSave raid)
    {
        File.Delete(PathOf(raid));
        RunFile(raid).Delete();
    }

    /// <summary>The dungeon the raid is in the middle of, saved as it's played.</summary>
    public SavedRunFile RunFile(RaidSave raid) => new(Path.Combine(Folder, $"{raid.Id:N}.run.json"));

    private string PathOf(RaidSave raid) => Path.Combine(Folder, $"{raid.Id:N}.raid.json");
}
