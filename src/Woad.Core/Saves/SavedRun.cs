using System.Text.Json;
using System.Text.Json.Serialization;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Model;
using Woad.Core.Rules;

namespace Woad.Core.Saves;

/// <summary>
/// A run in progress, saved as what started it (the dungeon, party, seed and friendly fire) and
/// every command played since. The rules are deterministic for a seed, so replaying the commands
/// rebuilds the run exactly where it was left.
/// </summary>
public sealed class SavedRun
{
    /// <summary>The save format. A save from another version isn't loaded.</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase), new CommandConverter() },
    };

    public int Version { get; init; } = CurrentVersion;

    public required string ThemeId { get; init; }

    public required ulong Seed { get; init; }

    public bool FriendlyFire { get; init; }

    public required IReadOnlyList<HeroSetup> Heroes { get; init; }

    /// <summary>Every accepted command, in the order played.</summary>
    public List<GameCommand> Commands { get; init; } = [];

    /// <summary>The round the run had reached, for the Continue button.</summary>
    public int Round { get; set; } = 1;

    /// <summary>The deepest floor any hero had reached, for the Continue button.</summary>
    public int Floor { get; set; } = 1;

    public DateTimeOffset SavedUtc { get; set; }

    /// <summary>The run's starting point, generated the same way a new run is.</summary>
    public RunSetup CreateSetup(ContentCatalog content) =>
        DungeonGenerator.CreateRun(content, ThemeId, Heroes, Seed) with { FriendlyFire = FriendlyFire };

    /// <summary>Notes a command the rules accepted, and where the run stands after it.</summary>
    public void Record(GameCommand command, GameState state)
    {
        Commands.Add(command);
        Round = state.Round;
        Floor = state.Heroes.Max(hero => hero.DeepestFloor);
        SavedUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Rebuilds the run: a begun session with every saved command replayed.</summary>
    /// <exception cref="InvalidDataException">The save no longer fits the game (a command is refused).</exception>
    public GameSession Restore(ContentCatalog content)
    {
        if (!content.Themes.ContainsKey(ThemeId))
        {
            throw new InvalidDataException($"The dungeon \"{ThemeId}\" is gone.");
        }

        var session = new GameSession(content, CreateSetup(content));
        session.Begin();
        for (int i = 0; i < Commands.Count; i++)
        {
            CommandResult result = session.Execute(Commands[i]);
            if (!result.IsAccepted)
            {
                throw new InvalidDataException($"The save no longer fits the game: move {i + 1} of {Commands.Count} was refused ({result.Error}).");
            }
        }

        return session;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <exception cref="InvalidDataException">Not a save this version can read.</exception>
    public static SavedRun FromJson(string json)
    {
        SavedRun? run;
        try
        {
            run = JsonSerializer.Deserialize<SavedRun>(json, Options);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The save is damaged: {exception.Message}", exception);
        }

        if (run is null || run.Version != CurrentVersion)
        {
            throw new InvalidDataException("The save is from another version of the game.");
        }

        return run;
    }

    /// <summary>Writes a command as its type name and fields: <c>{ "type": "MoveHero", "heroId": 1, ... }</c>.</summary>
    private sealed class CommandConverter : JsonConverter<GameCommand>
    {
        private static readonly Dictionary<string, Type> Types = typeof(GameCommand).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(GameCommand)) && !type.IsAbstract)
            .ToDictionary(type => type.Name);

        public override GameCommand Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            if (!document.RootElement.TryGetProperty("type", out JsonElement name) || !Types.TryGetValue(name.GetString() ?? "", out Type? type))
            {
                throw new JsonException("A saved move has an unknown type.");
            }

            return (GameCommand?)document.RootElement.Deserialize(type, options) ?? throw new JsonException("A saved move is empty.");
        }

        public override void Write(Utf8JsonWriter writer, GameCommand value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("type", value.GetType().Name);
            foreach (JsonProperty field in JsonSerializer.SerializeToElement(value, value.GetType(), options).EnumerateObject())
            {
                field.WriteTo(writer);
            }

            writer.WriteEndObject();
        }
    }
}
