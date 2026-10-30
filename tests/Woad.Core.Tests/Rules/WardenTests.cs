using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>The Iron Warden: its arc swing and cleave, its spin, its chain and the shackles.</summary>
public class WardenTests
{
    private static readonly ContentCatalog Content = ContentCatalog.LoadDefault();

    private static GameSession Start(string room, params int[] dice)
    {
        var setup = new RunSetup
        {
            Seed = 1,
            ThemeId = "emerald_keep",
            Heroes = [new HeroSetup("Hero", GameRules.HeroBaseStats with { Hearts = 40, OdHandDice = 1 }, [])],
            Rooms = [new RoomSetup("A", RoomTemplate.Parse(room, Content))],
            StartRoomId = "A",
        };
        var session = new GameSession(Content, setup, new GameRandom(1) { Dice = new ScriptedDice(dice) });
        session.Begin();
        return session;
    }

    [Theory]
    [InlineData(1, 0, "(5, 4) (5, 3) (5, 5)")]
    [InlineData(0, -1, "(4, 3) (3, 3) (5, 3)")]
    [InlineData(1, 1, "(5, 5) (5, 4) (4, 5)")]
    public void TheArcIsTheTileAheadAndTheTwoBesideIt(int dx, int dy, string tiles)
    {
        var from = new GridPosition(4, 4);

        IReadOnlyList<GridPosition> arc = GameSession.ArcTiles(from, new GridPosition(4 + dx * 2, 4 + dy * 2));

        Assert.Equal(tiles, string.Join(" ", arc));
    }

    [Fact]
    public void TheArcStrikesEveryoneInItMonstersToo()
    {
        // The Warden at (4,3) swings south at the hero (4,4), the slime at (3,4) in the arc too: 5 + 5 + 1.
        GameSession session = Start("""
            .........
            .........
            .........
            ....W....
            ...s1....
            .........
            .........
            .........
            .........
            """, [5, 5, .. Enumerable.Repeat(1, 40)]);
        Hero hero = session.HeroInSeat();
        Monster slime = session.RoomById().Monsters.Single(monster => monster.Definition.Id == "slime");

        IReadOnlyList<GameEvent> events = session.EndRound();

        AttackRolled swing = events.OfType<AttackRolled>().First();
        Assert.Equal(11, swing.Total);
        Assert.Equal([hero.Id, slime.Id], swing.TargetIds);
        Assert.Contains(events, gameEvent => gameEvent is Damaged { Amount: > 0 } damaged && damaged.TargetId == slime.Id);
        // Then the spin, at the hero again.
        Assert.Equal(2, events.OfType<AttackRolled>().Count());
    }

    [Fact]
    public void TheChainDragsAHeroToTheWardenShackled()
    {
        // The hero is 4 tiles south: the chain (5, 5, 5) hits, and drags them up beside the Warden.
        GameSession session = Start("""
            .........
            ....W....
            .........
            .........
            .........
            ....1....
            .........
            .........
            .........
            """, [5, 5, 5, .. Enumerable.Repeat(1, 40)]);
        Hero hero = session.HeroInSeat();
        Monster warden = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.True(events.OfType<AttackRolled>().First().Chain);
        Assert.Equal(1, hero.Position.ChebyshevDistanceTo(warden.Position));
        Assert.Equal(9, hero.Shackled);
        Assert.Equal(0, session.MoveDiceLeft);
        Assert.Contains("shackles", session.Refuse(new Leap(hero.Id, hero.Position with { Y = hero.Position.Y + 2 })));
    }

    [Fact]
    public void AKeyOrAPickedLockFreesAShackledHero()
    {
        GameSession session = Start("""
            .........
            .........
            .........
            .........
            ....1....
            .........
            .........
            .........
            .........
            """, 6, 4);
        Hero hero = session.HeroInSeat();
        hero.Shackled = 9;

        // Od Hand: one die (6), then its Power Die (4): 10 against 9.
        session.Do(new PickLock(hero.Id, hero.Position));
        IReadOnlyList<GameEvent> events = session.Do(new AddPowerDie(hero.Id));
        Assert.Contains(new HeroUnshackled(hero.Id, hero.Id, Picked: true), events);
        Assert.Null(hero.Shackled);

        hero.Shackled = 9;
        hero.Keys = 1;
        session.Do(new Unshackle(hero.Id, hero.Id));
        Assert.Null(hero.Shackled);
        Assert.Equal(0, hero.Keys);
    }
}
