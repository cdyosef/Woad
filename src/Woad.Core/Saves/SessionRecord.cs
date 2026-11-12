using System.Text.Json;
using System.Text.Json.Serialization;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Rules;

namespace Woad.Core.Saves;

/// <summary>What else happened at the table, beside the moves.</summary>
public enum SessionNoteKind
{
    /// <summary>Someone said something, as a hero (<see cref="SessionNote.HeroId"/>), in a language.</summary>
    Chat,

    /// <summary>Someone sat down at the table.</summary>
    Joined,

    /// <summary>Someone left the table.</summary>
    Left,

    /// <summary>Someone back at the table took up their heroes again.</summary>
    Rejoined,

    /// <summary>Someone came to watch.</summary>
    Watching,
}

/// <summary>Something that happened at the table, after <see cref="AfterMove"/> of the run's moves.</summary>
public sealed record SessionNote(int AfterMove, DateTimeOffset Utc, SessionNoteKind Kind, string Name, int? HeroId = null, string Text = "", string Language = "");

/// <summary>Who made a move, and when.</summary>
public sealed record SessionMove(string Player, DateTimeOffset Utc);

/// <summary>
/// A whole session at the table, to watch again: the run (its start and every move, a <see cref="SavedRun"/>), who made
/// each move and when, and what else happened (chat, players coming and going). The rules are deterministic, so a
/// replay plays the moves through them again; it only plays on the version that made it (<see cref="GameVersion"/>).
/// </summary>
public sealed class SessionRecord
{
    public const int CurrentFormat = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = false,
    };

    public int Format { get; init; } = CurrentFormat;

    /// <summary>The game's version and rules build that played it.</summary>
    public required string GameVersion { get; init; }

    public DateTimeOffset Started { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Who was at the table when it began, the host first.</summary>
    public List<string> Players { get; init; } = [];

    /// <summary>The run: its start and every move (as a saved run's text).</summary>
    public required string Run { get; set; }

    /// <summary>For each move of the run, in order: who made it, and when.</summary>
    public List<SessionMove> Moves { get; init; } = [];

    public List<SessionNote> Notes { get; init; } = [];

    /// <summary>Notes a move the host accepted, the run as it stands after it, and who made the move.</summary>
    public void Record(SavedRun run, string player)
    {
        Run = run.ToJson();
        Moves.Add(new SessionMove(player, DateTimeOffset.UtcNow));
    }

    public void Note(SessionNoteKind kind, string name, int moves, int? heroId = null, string text = "", string language = "") =>
        Notes.Add(new SessionNote(moves, DateTimeOffset.UtcNow, kind, name, heroId, text, language));

    /// <summary>The run's start and moves.</summary>
    public SavedRun RunSoFar() => SavedRun.FromJson(Run);

    /// <summary>The moves, to play one by one.</summary>
    public IReadOnlyList<GameCommand> Commands() => RunSoFar().Commands;

    /// <summary>A fresh run, played up to (not including) move <paramref name="moves"/>: to watch from there.</summary>
    /// <exception cref="InvalidDataException">The replay doesn't fit this version's rules.</exception>
    public GameSession PlayTo(ContentCatalog content, int moves)
    {
        SavedRun all = RunSoFar();
        var part = new SavedRun
        {
            ThemeId = all.ThemeId,
            Seed = all.Seed,
            FriendlyFire = all.FriendlyFire,
            Skirmish = all.Skirmish,
            MerchantGuildLevel = all.MerchantGuildLevel,
            UniquesClaimed = all.UniquesClaimed,
            RitualRounds = all.RitualRounds,
            Cursed = all.Cursed,
            Heroes = all.Heroes,
            Commands = [.. all.Commands.Take(Math.Clamp(moves, 0, all.Commands.Count))],
        };
        return part.Restore(content);
    }

    /// <summary>The first move played in each round: to jump to a round (round number to the move it starts at).</summary>
    public IReadOnlyDictionary<int, int> RoundStarts(ContentCatalog content)
    {
        SavedRun all = RunSoFar();
        GameSession session = new SavedRun
        {
            ThemeId = all.ThemeId, Seed = all.Seed, FriendlyFire = all.FriendlyFire, Skirmish = all.Skirmish, MerchantGuildLevel = all.MerchantGuildLevel,
            UniquesClaimed = all.UniquesClaimed, RitualRounds = all.RitualRounds, Cursed = all.Cursed, Heroes = all.Heroes,
        }.Restore(content);
        var starts = new Dictionary<int, int> { [session.State.Round] = 0 };
        for (int i = 0; i < all.Commands.Count; i++)
        {
            session.Execute(all.Commands[i]);
            starts.TryAdd(session.State.Round, i + 1);
        }

        return starts;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <exception cref="InvalidDataException">Not a replay this version can read.</exception>
    public static SessionRecord FromJson(string json)
    {
        try
        {
            SessionRecord? record = JsonSerializer.Deserialize<SessionRecord>(json, Options);
            return record is { Format: CurrentFormat } ? record : throw new InvalidDataException("The replay is from another version of the game.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The replay is damaged: {exception.Message}", exception);
        }
    }
}
