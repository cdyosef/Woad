using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Model;

namespace Woad.Core.Tests.Characters;

public sealed class JsonCharacterRepositoryTests : IDisposable
{
    private static readonly ContentCatalog Content = ContentCatalog.LoadDefault();

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "woad-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private JsonCharacterRepository Repository() => new(_folder, Content);

    private static Character NewCharacter(string name, int minutes = 0) =>
        Character.Create(name, "moss", new StatAllocation { Hearts = 1, Luck = 1 }, Content, new DateTimeOffset(2026, 10, 10, 12, minutes, 0, TimeSpan.Zero));

    [Fact]
    public async Task BlackHeartsAndDiseaseAreSavedUntilCured()
    {
        Character character = NewCharacter("Morgan");
        character.ApplyRunResult(new Woad.Core.Rules.HeroRunResult
        {
            HeroId = 1,
            CharacterId = character.Id,
            Name = character.Name,
            Seat = 0,
            Portrait = character.Portrait,
            Status = HeroStatus.Escaped,
            Won = false,
            Kills = 0,
            Xp = 0,
            Gold = 0,
            DeepestFloor = 1,
            Equipped = [.. character.Equipped],
            Belt = [.. character.Belt],
            Backpack = [.. character.Backpack],
            Lingering = new Dictionary<Condition, int> { [Condition.Nekrot] = 1, [Condition.BlackHeart] = 2 },
        });
        await Repository().SaveAsync(character);

        Character loaded = Assert.Single((await Repository().LoadAllAsync()).Characters);

        Assert.Equal(new Dictionary<Condition, int> { [Condition.Nekrot] = 1, [Condition.BlackHeart] = 2 }, loaded.Lingering);
        Assert.Equal(loaded.Lingering, loaded.ToHeroSetup().Lingering);
    }

    [Fact]
    public async Task CompanionsStayCompanionsAndOlderFilesLoadAsHeroes()
    {
        Character companion = Character.Create("Pip", "bone", StatAllocation.None, Content, DateTimeOffset.UnixEpoch, isCompanion: true);
        await Repository().SaveAsync(companion);
        Assert.True(Assert.Single((await Repository().LoadAllAsync()).Characters).IsCompanion);

        // Files from before companions have no flag at all.
        string path = Directory.GetFiles(_folder, "*.json").Single();
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"isCompanion\": true", "\"unused\": 0"));
        Assert.False(Assert.Single((await Repository().LoadAllAsync()).Characters).IsCompanion);
    }

    [Fact]
    public async Task LooksAreSavedAndOlderFilesGetTheDefaultLook()
    {
        var look = new Appearance("wolf_man", "teal", "bone", "", "russet");
        Character character = Character.Create("Fenn", "moss", StatAllocation.None, Content, DateTimeOffset.UnixEpoch, appearance: look);
        await Repository().SaveAsync(character);
        Assert.Equal(look, Assert.Single((await Repository().LoadAllAsync()).Characters).Appearance);

        // Files from before looks have no appearance at all.
        string path = Directory.GetFiles(_folder, "*.json").Single();
        string json = File.ReadAllText(path);
        int start = json.IndexOf("\"appearance\"", StringComparison.Ordinal);
        File.WriteAllText(path, json[..start].TrimEnd().TrimEnd(',') + "\n}");
        Assert.Equal(Appearance.Default, Assert.Single((await Repository().LoadAllAsync()).Characters).Appearance);
    }

    [Fact]
    public async Task AnEmptyOrMissingFolderHasNoCharacters()
    {
        CharacterLoadResult result = await Repository().LoadAllAsync();

        Assert.Empty(result.Characters);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public async Task SavedCharactersLoadBackExactly()
    {
        Character original = NewCharacter("Morgan");
        original.GainXp(12);
        await Repository().SaveAsync(original);

        Character loaded = Assert.Single((await Repository().LoadAllAsync()).Characters);

        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal(original.Name, loaded.Name);
        Assert.Equal(original.Portrait, loaded.Portrait);
        Assert.Equal(original.CreatedUtc, loaded.CreatedUtc);
        Assert.Equal(original.Xp, loaded.Xp);
        Assert.Equal(2, loaded.Level);
        Assert.Equal(original.Allocation, loaded.Allocation);
        Assert.Equal(original.UnspentPoints, loaded.UnspentPoints);
        Assert.Equal(original.Equipped, loaded.Equipped);
        Assert.Equal(original.Belt, loaded.Belt);
        Assert.Equal(original.Backpack, loaded.Backpack);
    }

    [Fact]
    public async Task SavingAgainOverwritesWithoutLeavingTemporaryFiles()
    {
        Character character = NewCharacter("Morgan");
        await Repository().SaveAsync(character);
        character.Rename("Morgana");
        await Repository().SaveAsync(character);

        Assert.Equal("Morgana", Assert.Single((await Repository().LoadAllAsync()).Characters).Name);
        Assert.Single(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task CharactersLoadOldestFirstAndCanBeDeleted()
    {
        Character newer = NewCharacter("Wren", minutes: 5);
        Character older = NewCharacter("Ash", minutes: 1);
        await Repository().SaveAsync(newer);
        await Repository().SaveAsync(older);

        Assert.Equal(["Ash", "Wren"], (await Repository().LoadAllAsync()).Characters.Select(character => character.Name));

        await Repository().DeleteAsync(older.Id);
        Assert.Equal(["Wren"], (await Repository().LoadAllAsync()).Characters.Select(character => character.Name));
    }

    [Fact]
    public async Task BrokenFilesAreReportedAndSkipped()
    {
        await Repository().SaveAsync(NewCharacter("Morgan"));
        await File.WriteAllTextAsync(Path.Combine(_folder, "broken.json"), "{ not json");

        CharacterLoadResult result = await Repository().LoadAllAsync();

        Assert.Single(result.Characters);
        Assert.Contains(result.Problems, problem => problem.StartsWith("broken.json"));
    }

    [Fact]
    public async Task CharactersSavedBeforeSkillsKeepTheirOdHandDieAndLearnTheOldSkills()
    {
        Character character = NewCharacter("Morgan");
        await Repository().SaveAsync(character);
        string path = Path.Combine(_folder, $"{character.Id:N}.json");
        string json = (await File.ReadAllTextAsync(path)).Replace($"\"schemaVersion\": {JsonCharacterRepository.SchemaVersion}", "\"schemaVersion\": 1");
        await File.WriteAllTextAsync(path, json);

        Character loaded = Assert.Single((await Repository().LoadAllAsync()).Characters);

        Assert.Equal(character.Allocation.OdHandDice + 1, loaded.Allocation.OdHandDice);
        Assert.Equal(Skills.OdHand | Skill.Shove, loaded.Allocation.Skills);
    }

    [Fact]
    public async Task CardsThatNoLongerExistAreDroppedWithAWarning()
    {
        Character character = NewCharacter("Morgan");
        await Repository().SaveAsync(character);
        string path = Path.Combine(_folder, $"{character.Id:N}.json");
        await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace("\"health_potion\"", "\"retired_card\""));

        CharacterLoadResult result = await Repository().LoadAllAsync();

        Assert.Equal(["torch", "lockpicks"], Assert.Single(result.Characters).Belt);
        Assert.Contains(result.Problems, problem => problem.Contains("retired_card"));
    }
}
