using Woad.Core.Bots;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Model;
using Woad.Core.Rules;
using Xunit.Abstractions;

namespace Woad.Core.Tests.Bots;

/// <summary>
/// Bots play whole generated dungeons. Every command they send must be accepted, so this catches
/// rule-engine crashes and contradictions between what the queries offer and what commands allow.
/// </summary>
public class BotPlaythroughTests(ITestOutputHelper output)
{
    private const int CommandLimit = 6000;

    private static readonly ContentCatalog Content = ContentCatalog.LoadDefault();

    [Theory]
    [InlineData(21UL, 1)]
    [InlineData(22UL, 2)]
    [InlineData(23UL, 4)]
    public void SturdyBotsClearEveryFloorBeatTheBossAndEscape(ulong seed, int partySize)
    {
        Stats sturdy = GameRules.HeroBaseStats with { Hearts = 40, AttackDice = 3, DefenseDice = 3, OdHandDice = 1 };
        GameSession session = Play(seed, partySize, sturdy);

        Assert.Equal(RunPhase.Ended, session.State.Phase);
        Assert.Contains(session.State.Heroes, hero => hero.Status == HeroStatus.Escaped);
        Assert.NotEmpty(session.State.WinnerIds);
        Assert.All(session.State.Heroes.Where(hero => hero.Status == HeroStatus.Escaped), hero => Assert.Equal(3, hero.DeepestFloor));
    }

    [Theory]
    [InlineData(11UL, 1)]
    [InlineData(12UL, 2)]
    [InlineData(13UL, 3)]
    [InlineData(14UL, 4)]
    [InlineData(15UL, 2)]
    [InlineData(16UL, 4)]
    public void BotsPlayGeneratedDungeonsWithoutRefusedCommands(ulong seed, int partySize)
    {
        GameSession session = Play(seed, partySize, GameRules.HeroBaseStats with { Hearts = 6, OdHandDice = 1 });

        Assert.True(session.State.Heroes.Max(hero => hero.DeepestFloor) >= 2 || session.State.Phase == RunPhase.Ended,
            "the bots should either get past the first floor or have the run end");
    }

    /// <summary>Lets bots play a generated run until it ends or the command limit is hit; every command must be accepted.</summary>
    private GameSession Play(ulong seed, int partySize, Stats stats)
    {
        var heroes = Enumerable.Range(1, partySize)
            .Select(number => new HeroSetup($"Bot {number}", stats, GameRules.StarterKit))
            .ToList();
        var session = new GameSession(Content, DungeonGenerator.CreateRun(Content, "emerald_keep", heroes, seed));
        session.Begin();

        int commands = 0;
        while (session.State.Phase == RunPhase.InProgress && commands < CommandLimit)
        {
            GameCommand command = SimpleBot.NextCommand(session);
            CommandResult result = session.Execute(command);
            Assert.True(result.IsAccepted, $"Command {commands} {command} was refused: {result.Error}");
            commands++;
        }

        output.WriteLine($"seed {seed}, {partySize} heroes: {session.State.Phase} after {commands} commands, round {session.State.Round}; " +
            string.Join("; ", session.State.Heroes.Select(hero => $"{hero.Name} {hero.Status} floor {hero.DeepestFloor} kills {hero.Kills}")));
        foreach (Hero hero in session.State.Heroes.Where(hero => hero.IsActive))
        {
            Room room = session.State.RoomOf(hero);
            output.WriteLine($"  {hero.Name} in {room.Id} ({room.Role}{(room.IsSealed ? ", sealed" : "")}) at {hero.Position}; " +
                $"monsters: {string.Join(", ", room.Monsters.Select(monster => $"{monster.Definition.Id}@{monster.Position}"))}; " +
                $"doors: {string.Join(", ", room.Doors.Select(door => $"{door.Key}>{door.Value}{(session.State.Rooms[door.Value].Explored ? "" : "?")}"))}");
        }

        return session;
    }
}
