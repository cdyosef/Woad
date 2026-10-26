using System.Text.Json;
using Woad.Core.Content;
using Woad.Core.Model;

namespace Woad.Core.Characters;

/// <summary>
/// Saves each character as <c>{id}.json</c> in a folder. Writes go to a temporary file first, so
/// a crash mid-save never leaves a half-written character behind.
/// </summary>
public sealed class JsonCharacterRepository(string folderPath, ContentCatalog content) : ICharacterRepository
{
    /// <summary>Bump when the file shape changes, and teach the loader to migrate older files.</summary>
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        RespectNullableAnnotations = true,
    };

    public string FolderPath { get; } = folderPath;

    public async Task<CharacterLoadResult> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        var characters = new List<Character>();
        var problems = new List<string>();
        if (!Directory.Exists(FolderPath))
        {
            return new CharacterLoadResult(characters, problems);
        }

        foreach (string path in Directory.EnumerateFiles(FolderPath, "*.json").Order(StringComparer.Ordinal))
        {
            string fileName = Path.GetFileName(path);
            try
            {
                string json = await File.ReadAllTextAsync(path, cancellationToken);
                CharacterFile file = JsonSerializer.Deserialize<CharacterFile>(json, Options)
                    ?? throw new JsonException("The file is empty.");
                if (file.SchemaVersion != SchemaVersion)
                {
                    problems.Add($"{fileName}: saved by an unknown version ({file.SchemaVersion}).");
                    continue;
                }

                characters.Add(FromFile(file, fileName, problems));
            }
            catch (Exception exception) when (exception is JsonException or IOException or ArgumentException)
            {
                problems.Add($"{fileName}: {exception.Message}");
            }
        }

        return new CharacterLoadResult(characters.OrderBy(character => character.CreatedUtc).ToList(), problems);
    }

    public async Task SaveAsync(Character character, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(FolderPath);
        string path = PathFor(character.Id);
        string temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(ToFile(character), Options), cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    public Task DeleteAsync(Guid characterId, CancellationToken cancellationToken = default)
    {
        File.Delete(PathFor(characterId));
        return Task.CompletedTask;
    }

    private string PathFor(Guid characterId) => Path.Combine(FolderPath, $"{characterId:N}.json");

    private static CharacterFile ToFile(Character character) => new()
    {
        SchemaVersion = SchemaVersion,
        Id = character.Id,
        Name = character.Name,
        Portrait = character.Portrait,
        CreatedUtc = character.CreatedUtc,
        Xp = character.Xp,
        Allocation = character.Allocation,
        UnspentPoints = character.UnspentPoints,
        Equipped = [.. character.Equipped],
        Belt = [.. character.Belt],
        Backpack = [.. character.Backpack],
        Gold = character.Gold,
        RunsPlayed = character.RunsPlayed,
        Wins = character.Wins,
        IsCompanion = character.IsCompanion,
        Appearance = character.Appearance,
        GuildStanding = character.GuildStanding,
        LawStanding = character.LawStanding,
        CowardStanding = character.CowardStanding,
        HasCowardsCloak = character.HasCowardsCloak,
        PowerDice = character.PowerDice,
        Policy = character.Policy,
    };

    /// <summary>Rebuilds a character, dropping cards that no longer exist in the game's content.</summary>
    private Character FromFile(CharacterFile file, string fileName, List<string> problems)
    {
        if (Character.CheckName(file.Name) is { } nameError)
        {
            throw new ArgumentException(nameError);
        }

        List<string> Known(IEnumerable<string> cardIds)
        {
            var known = new List<string>();
            foreach (string cardId in cardIds)
            {
                if (content.Cards.ContainsKey(cardId))
                {
                    known.Add(cardId);
                }
                else
                {
                    problems.Add($"{fileName}: dropped unknown card '{cardId}' from {file.Name}.");
                }
            }

            return known;
        }

        return new Character(
            file.Id, file.Name, file.Portrait, file.CreatedUtc, Math.Max(0, file.Xp), file.Allocation,
            Math.Max(0, file.UnspentPoints), Known(file.Equipped), Known(file.Belt), Known(file.Backpack),
            Math.Max(0, file.Gold), Math.Max(0, file.RunsPlayed), Math.Max(0, file.Wins), file.IsCompanion, file.Appearance, file.GuildStanding, file.LawStanding, file.CowardStanding, file.HasCowardsCloak, file.PowerDice, file.Policy);
    }

    /// <summary>The on-disk shape of a character, kept separate so the model can change without breaking saves.</summary>
    private sealed record CharacterFile
    {
        public int SchemaVersion { get; init; }

        public Guid Id { get; init; }

        public string Name { get; init; } = "";

        public string Portrait { get; init; } = "";

        public DateTimeOffset CreatedUtc { get; init; }

        public int Xp { get; init; }

        public StatAllocation Allocation { get; init; } = StatAllocation.None;

        public int UnspentPoints { get; init; }

        public List<string> Equipped { get; init; } = [];

        public List<string> Belt { get; init; } = [];

        public List<string> Backpack { get; init; } = [];

        public int Gold { get; init; }

        public int RunsPlayed { get; init; }

        public int Wins { get; init; }

        /// <summary>Missing in files saved before companions existed, which were all heroes.</summary>
        public bool IsCompanion { get; init; }

        /// <summary>Missing in files saved before looks existed: those get the default look.</summary>
        public Appearance? Appearance { get; init; }

        /// <summary>Missing in files saved before the Merchant Guild: 0.</summary>
        public int GuildStanding { get; init; }

        public int LawStanding { get; init; }

        public int CowardStanding { get; init; }

        /// <summary>Missing in files saved before Power Dice carried over: none banked.</summary>
        public int PowerDice { get; init; }

        /// <summary>Missing in files saved before companion policies: the default.</summary>
        public CompanionPolicy? Policy { get; init; }

        public bool HasCowardsCloak { get; init; }
    }
}
