using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Events;
using Woad.Core.Model;
using Woad.Core.Randomness;
using Woad.Core.Rules;

namespace Woad.Core.Tests.Support;

/// <summary>Builds runs from room templates for rules tests. Dice are scripted; unscripted rolls fail.</summary>
internal static class TestRun
{
    public const ulong Seed = 42;

    public static RoomTemplate Room(string text) => RoomTemplate.Parse(text, TestContent.Catalog);

    /// <summary>A begun run in a single room "A".</summary>
    /// <param name="owners">Per seat: null for a player, or the seat of the player a companion belongs to.</param>
    public static GameSession Start(string room, ScriptedDice? dice = null, int heroes = 1, Stats? stats = null, string[]? cards = null, ScriptedDice? loot = null, int?[]? owners = null, bool friendlyFire = false) =>
        Start([new RoomSetup("A", Room(room))], dice, heroes, stats, cards, loot, owners: owners, friendlyFire: friendlyFire);

    /// <summary>A begun run starting in the first room. Every hero gets the same stats and cards.</summary>
    /// <param name="loot">Scripts the loot stream (pots, drops, deck shuffles); seeded when omitted.</param>
    public static GameSession Start(IReadOnlyList<RoomSetup> rooms, ScriptedDice? dice = null, int heroes = 1, Stats? stats = null, string[]? cards = null, ScriptedDice? loot = null, IReadOnlyList<PassageSetup>? passages = null, int?[]? owners = null, bool friendlyFire = false)
    {
        GameSession session = Create(rooms, dice, heroes, stats, cards, loot, passages, owners, friendlyFire);
        session.Begin();
        return session;
    }

    public static GameSession Create(IReadOnlyList<RoomSetup> rooms, ScriptedDice? dice = null, int heroes = 1, Stats? stats = null, string[]? cards = null, ScriptedDice? loot = null, IReadOnlyList<PassageSetup>? passages = null, int?[]? owners = null, bool friendlyFire = false)
    {
        var setup = new RunSetup
        {
            Seed = Seed,
            ThemeId = TestContent.ThemeId,
            Heroes = Enumerable.Range(1, heroes)
                .Select(number => new HeroSetup($"Hero {number}", stats ?? GameRules.HeroBaseStats, cards ?? [], OwnerSeat: owners?[number - 1]))
                .ToList(),
            Rooms = rooms,
            StartRoomId = rooms[0].Id,
            Passages = passages ?? [],
            FriendlyFire = friendlyFire,
        };

        var random = loot is null
            ? new GameRandom(Seed) { Dice = dice ?? new ScriptedDice() }
            : new GameRandom(Seed) { Dice = dice ?? new ScriptedDice(), Loot = loot };
        return new GameSession(TestContent.Catalog, setup, random);
    }
}

internal static class SessionTestExtensions
{
    /// <summary>Executes a command that must be accepted and returns its events.</summary>
    public static IReadOnlyList<GameEvent> Do(this GameSession session, GameCommand command)
    {
        CommandResult result = session.Execute(command);
        Assert.True(result.IsAccepted, $"{command} was refused: {result.Error}");
        return result.Events;
    }

    /// <summary>Executes a command that must be refused and returns the reason.</summary>
    public static string Refuse(this GameSession session, GameCommand command)
    {
        CommandResult result = session.Execute(command);
        Assert.False(result.IsAccepted, $"{command} should have been refused.");
        Assert.Empty(result.Events);
        return result.Error!;
    }

    public static Hero HeroInSeat(this GameSession session, int seat = 0) => session.State.Heroes[seat];

    public static Room RoomById(this GameSession session, string id = "A") => session.State.Rooms[id];

    public static Monster OnlyMonster(this GameSession session, string roomId = "A") =>
        Assert.Single(session.State.Rooms[roomId].Monsters);

    public static CardInstance Card(this Hero hero, string definitionId) =>
        hero.AllCards.First(card => card.Definition.Id == definitionId);

    /// <summary>Ends turns until the round changes (or the run ends) and returns every event.</summary>
    public static IReadOnlyList<GameEvent> EndRound(this GameSession session)
    {
        var events = new List<GameEvent>();
        int round = session.State.Round;
        while (session.State.Phase == RunPhase.InProgress && session.State.Round == round)
        {
            events.AddRange(session.Do(new EndTurn(session.State.ActiveHero!.Id)));
        }

        return events;
    }

    public static T SingleOf<T>(this IEnumerable<GameEvent> events) where T : GameEvent =>
        Assert.Single(events.OfType<T>());
}
