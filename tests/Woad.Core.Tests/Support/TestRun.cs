using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;
using Woad.Core.Rules;

namespace Woad.Core.Tests.Support;

/// <summary>Builds runs from room templates for rules tests. Dice are scripted; unscripted rolls fail.</summary>
internal static class TestRun
{
    public const ulong Seed = 42;

    public static RoomTemplate Room(string text) => RoomTemplate.Parse(text, TestContent.Catalog);

    /// <summary>
    /// The heroes' stats unless a test says otherwise: the game's starting stats, but with 4 hearts, so
    /// rules tests don't shift when the starting hearts are retuned.
    /// </summary>
    public static Stats HeroStats { get; } = GameRules.HeroBaseStats with { Hearts = 4, OdHandDice = 1 };

    /// <summary>A begun run in a single room "A".</summary>
    /// <param name="owners">Per seat: null for a player, or the seat of the player a companion belongs to.</param>
    /// <param name="skills">What every hero knows: everything, unless a test says otherwise.</param>
    public static GameSession Start(string room, ScriptedDice? dice = null, int heroes = 1, Stats? stats = null, string[]? cards = null, ScriptedDice? loot = null, int?[]? owners = null, bool friendlyFire = false, Skill skills = Skills.All) =>
        Start([new RoomSetup("A", Room(room))], dice, heroes, stats, cards, loot, owners: owners, friendlyFire: friendlyFire, skills: skills);

    /// <summary>A begun run starting in the first room. Every hero gets the same stats and cards.</summary>
    /// <param name="loot">Scripts the loot stream (pots, drops, deck shuffles); seeded when omitted.</param>
    public static GameSession Start(IReadOnlyList<RoomSetup> rooms, ScriptedDice? dice = null, int heroes = 1, Stats? stats = null, string[]? cards = null, ScriptedDice? loot = null, IReadOnlyList<PassageSetup>? passages = null, int?[]? owners = null, bool friendlyFire = false, Skill skills = Skills.All)
    {
        GameSession session = Create(rooms, dice, heroes, stats, cards, loot, passages, owners, friendlyFire, skills);
        session.Begin();
        return session;
    }

    public static GameSession Create(IReadOnlyList<RoomSetup> rooms, ScriptedDice? dice = null, int heroes = 1, Stats? stats = null, string[]? cards = null, ScriptedDice? loot = null, IReadOnlyList<PassageSetup>? passages = null, int?[]? owners = null, bool friendlyFire = false, Skill skills = Skills.All)
    {
        var setup = new RunSetup
        {
            Seed = Seed,
            ThemeId = TestContent.ThemeId,
            Heroes = Enumerable.Range(1, heroes)
                .Select(number => new HeroSetup($"Hero {number}", stats ?? HeroStats, cards ?? [], OwnerSeat: owners?[number - 1], Skills: skills))
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

    /// <summary>Ends turns (rolling the Keel Die for anyone Keeling Over) until the round changes (or the run ends) and returns every event.</summary>
    public static IReadOnlyList<GameEvent> EndRound(this GameSession session)
    {
        var events = new List<GameEvent>();
        int round = session.State.Round;
        while (session.State.Phase == RunPhase.InProgress && session.State.Round == round)
        {
            int heroId = session.State.ActiveHero!.Id;
            events.AddRange(session.Do(session.State.Turn!.KeelingOver ? new RollKeel(heroId) : new EndTurn(heroId)));
        }

        return events;
    }

    /// <summary>Drops a bomb in a room (lit, owned by <paramref name="ownerId"/>: it goes off at the end of their current turn by default).</summary>
    public static Bomb DropBomb(this GameSession session, GridPosition tile, bool lit = true, int? ownerId = null, int fuseTurns = 1, string roomId = "A")
    {
        var bomb = new Bomb(session.State.NextId(), tile, lit, lit ? ownerId ?? session.State.ActiveHero!.Id : null, lit ? fuseTurns : 0);
        session.State.Rooms[roomId].AddBomb(bomb);
        return bomb;
    }

    public static T SingleOf<T>(this IEnumerable<GameEvent> events) where T : GameEvent =>
        Assert.Single(events.OfType<T>());
}
