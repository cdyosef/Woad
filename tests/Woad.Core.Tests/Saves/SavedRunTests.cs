using Woad.Core.Bots;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Saves;

namespace Woad.Core.Tests.Saves;

/// <summary>
/// A saved run is its setup and the commands played. Written out, read back and replayed, it must
/// be the same run: same heroes, monsters and dice from there on.
/// </summary>
public class SavedRunTests
{
    private static readonly ContentCatalog Content = ContentCatalog.LoadDefault();

    [Theory]
    [InlineData("emerald_keep", 31UL, 150)]
    [InlineData("sunken_temple", 32UL, 300)]
    [InlineData("emerald_keep", 33UL, 0)]
    public void AReloadedRunPlaysOnExactlyLikeTheOriginal(string themeId, ulong seed, int movesBeforeSaving)
    {
        SavedRun save = NewSave(themeId, seed);
        var original = new GameSession(Content, save.CreateSetup(Content));
        original.Begin();
        Play(original, movesBeforeSaving, save);

        SavedRun loaded = SavedRun.FromJson(save.ToJson());
        GameSession restored = loaded.Restore(Content);

        Assert.Equal(Fingerprint(original), Fingerprint(restored));
        Assert.Equal(save.Commands, loaded.Commands);
        Assert.Equal((save.Round, save.Floor), (loaded.Round, loaded.Floor));
        // Same random streams from here: the next moves roll the same dice and play out the same.
        Assert.Equal(Play(original, 200), Play(restored, 200));
        Assert.Equal(Fingerprint(original), Fingerprint(restored));
    }

    [Fact]
    public void TheSaveKeepsThePartyAsSetUp()
    {
        SavedRun loaded = SavedRun.FromJson(NewSave("sunken_temple", 7).ToJson());

        Assert.Equal("sunken_temple", loaded.ThemeId);
        Assert.Equal(7UL, loaded.Seed);
        Assert.True(loaded.FriendlyFire);
        Assert.Equal(["Ash", "Bramble", "Cole"], loaded.Heroes.Select(hero => hero.Name));
        Assert.Equal(0, loaded.Heroes[1].OwnerSeat);
        Assert.Equal(new Appearance("ranger", "ember"), loaded.Heroes[0].Appearance);
        Assert.Equal(GameRules.StarterKit, loaded.Heroes[0].CardIds);
        Assert.NotNull(loaded.Heroes[2].CharacterId);
    }

    [Fact]
    public void AMoveTheGameNoLongerAllowsMakesTheSaveUnusable()
    {
        SavedRun save = NewSave("emerald_keep", 5);
        save.Commands.Add(new EndTurn(99));

        InvalidDataException problem = Assert.Throws<InvalidDataException>(() => SavedRun.FromJson(save.ToJson()).Restore(Content));
        Assert.Contains("move 1 of 1", problem.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"version": 999, "themeId": "emerald_keep", "seed": 1, "heroes": []}""")]
    [InlineData("""{"version": 1, "themeId": "emerald_keep", "seed": 1, "heroes": [], "commands": [{"type": "Teleport", "heroId": 1}]}""")]
    public void DamagedOrForeignSavesAreRefused(string json) =>
        Assert.Throws<InvalidDataException>(() => SavedRun.FromJson(json));

    [Fact]
    public void TheFileSavesLoadsAndIsRemoved()
    {
        string folder = Path.Combine(Path.GetTempPath(), "woad-saves-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = new SavedRunFile(Path.Combine(folder, "current-run.json"));
            Assert.Null(file.Load());

            SavedRun save = NewSave("emerald_keep", 9);
            save.Commands.Add(new EndTurn(1));
            file.Save(save);
            file.Save(save);
            Assert.Equal(save.Commands, file.Load()!.Commands);
            Assert.Single(Directory.GetFiles(folder));

            file.Delete();
            Assert.False(file.Exists);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>Two players, the first with a companion, as the party screen seats them.</summary>
    private static SavedRun NewSave(string themeId, ulong seed) => new()
    {
        ThemeId = themeId,
        Seed = seed,
        FriendlyFire = true,
        Heroes =
        [
            new HeroSetup("Ash", GameRules.HeroBaseStats with { Hearts = 8 }, GameRules.StarterKit, Guid.NewGuid(), "moss", Appearance: new Appearance("ranger", "ember")),
            new HeroSetup("Bramble", GameRules.HeroBaseStats, GameRules.StarterKit, Guid.NewGuid(), OwnerSeat: 0),
            new HeroSetup("Cole", GameRules.HeroBaseStats with { AttackDice = 2 }, GameRules.StarterKit, Guid.NewGuid()),
        ],
    };

    /// <summary>Bots play up to <paramref name="moves"/> commands, recording each in <paramref name="save"/>; returns what happened.</summary>
    private static List<string> Play(GameSession session, int moves, SavedRun? save = null)
    {
        var happened = new List<string>();
        for (int i = 0; i < moves && session.State.Phase == RunPhase.InProgress; i++)
        {
            GameCommand command = session.State.ActiveHero is { IsCompanion: true } ? CompanionBot.NextCommand(session) : SimpleBot.NextCommand(session);
            CommandResult result = session.Execute(command);
            Assert.True(result.IsAccepted, $"{command} was refused: {result.Error}");
            save?.Record(command, session.State);
            happened.Add(command.ToString());
            happened.AddRange(result.Events.Select(gameEvent => gameEvent.ToString()!));
        }

        return happened;
    }

    private static string Fingerprint(GameSession session)
    {
        GameState state = session.State;
        IEnumerable<string> heroes = state.Heroes.Select(hero =>
            $"{hero.Name} {hero.Status} {hero.RoomId}@{hero.Position} {hero.Hearts}/{hero.MaxHearts} gold {hero.Gold} cards {string.Join(",", hero.AllCards.Select(card => card.Definition.Id))}");
        IEnumerable<string> monsters = state.Rooms.Values.SelectMany(room => room.Monsters.Select(monster => $"{monster.Id} {monster.Definition.Id} {room.Id}@{monster.Position} {monster.Hearts}"));
        return $"{state.Phase} round {state.Round} active {state.ActiveHero?.Id}\n{string.Join("\n", heroes)}\n{string.Join("\n", monsters)}";
    }
}
