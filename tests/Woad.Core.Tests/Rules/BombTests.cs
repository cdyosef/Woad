using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

public class BombTests
{
    private const string Open = """
        .........
        .........
        .........
        .........
        ....1....
        .........
        .........
        .........
        .........
        """;

    [Fact]
    public void AThrownBombLandsLitAndGoesOffAtTheEndOfTheThrowersNextTurn()
    {
        // The Dummy has no defense: all 4 dice get through, for a heart, a shove, a fall and a Powerful Stun.
        GameSession session = TestRun.Start("""
            .........
            ....d....
            .........
            .........
            ....1....
            .........
            .........
            .........
            .........
            """, new ScriptedDice(5, 5, 5, 5), cards: ["bomb"]);
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();

        var placed = session.Do(new UseCard(hero.Id, hero.Card("bomb").Id, new GridPosition(4, 2))).SingleOf<BombPlaced>();
        Assert.Equal((true, (int?)hero.Id), (placed.Lit, placed.OwnerId));
        Assert.Empty(session.Do(new EndTurn(hero.Id)).OfType<BombExploded>());

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Assert.Equal(new BlastHit(dummy.Id, 4), events.SingleOf<BlastHit>());
        Assert.Equal(new Shoved(hero.Id, dummy.Id, new GridPosition(4, 1), new GridPosition(4, 0)), events.SingleOf<Shoved>());
        Assert.Contains(new WentProne(dummy.Id, false), events);
        Assert.Equal(new Stunned(dummy.Id, 0, 1, true), events.SingleOf<Stunned>());
        Assert.Equal(2, dummy.Hearts);
        Assert.Empty(session.RoomById().Bombs);
    }

    [Fact]
    public void EachDieOverTheDefenseIsAHit()
    {
        // The Brute defends 3: of 5, 4, 3, 2 two dice get through, a heart and a shove but no fall.
        GameSession session = TestRun.Start("""
            .........
            .........
            ....B....
            .........
            ........1
            .........
            .........
            .........
            .........
            """, new ScriptedDice(5, 4, 3, 2, 3));
        Monster brute = session.OnlyMonster();
        session.DropBomb(new GridPosition(4, 3));

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(session.HeroInSeat().Id));

        Assert.Equal(new BlastHit(brute.Id, 2), events.SingleOf<BlastHit>());
        Assert.Equal(new GridPosition(4, 1), events.SingleOf<Shoved>().To);
        Assert.Equal((1, false), (brute.Hearts, brute.Prone));
        Assert.Empty(events.OfType<WentProne>());
    }

    [Fact]
    public void WhoeverStandsOnTheBombIsThrownAnyWayAndBlastedTwice()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            ....B....
            .........
            ........1
            .........
            .........
            .........
            .........
            """, new ScriptedDice(1, 1, 1, 1, 3, 5, 5, 1, 1, 3));
        Monster brute = session.OnlyMonster();
        session.DropBomb(brute.Position);

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(session.HeroInSeat().Id));

        // The first roll doesn't get past its defense of 3; the second does twice: a heart, and a throw.
        Assert.Equal([0, 2], events.OfType<BlastHit>().Select(hit => hit.Hits));
        Assert.Equal(1, brute.Hearts);
        Assert.Equal(new GridPosition(4, 2), events.SingleOf<Shoved>().From);
    }

    [Fact]
    public void BlastsCatchHeroesEvenWithFriendlyFireOff()
    {
        // 5, 5 beat the hero's defense of 1: a heart and a shove away from the bomb.
        GameSession session = TestRun.Start(Open, new ScriptedDice(5, 5, 1, 1, 1));
        Hero hero = session.HeroInSeat();
        session.DropBomb(new GridPosition(4, 3));

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Assert.Equal(new BlastHit(hero.Id, 2), events.SingleOf<BlastHit>());
        Assert.Equal((3, new GridPosition(4, 5)), (hero.Hearts, hero.Position));
    }

    [Fact]
    public void ABlastSetsOffOtherBombsAndBreaksCrackedWalls()
    {
        var features = new RoomFeatures { Caches = new Dictionary<GridPosition, CacheSetup> { [new(1, 0)] = new(0, Keys: 1, BehindWall: true) } };
        GameSession session = TestRun.Start([new RoomSetup("A", TestRun.Room("""
            .X.......
            .........
            .........
            .........
            ........1
            .........
            .........
            .........
            .........
            """), Features: features)], new ScriptedDice(1, 1, 1, 1, 1, 1, 1, 1));
        session.DropBomb(new GridPosition(2, 2));
        session.DropBomb(new GridPosition(1, 1), lit: false);

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(session.HeroInSeat().Id));

        Assert.Equal(2, events.OfType<BombExploded>().Count());
        Assert.Equal(TileKind.Floor, session.RoomById().Layout[new GridPosition(1, 0)]);
        Assert.Equal(1, session.RoomById().KeysAt(new GridPosition(1, 0)));
    }

    [Fact]
    public void ABlowToABombMaySetItOffTwelvePercentADieAndTwoAPointOfBonus()
    {
        // A bow (+1) from 3 away: 14%. A 50 doesn't set it off.
        GameSession session = TestRun.Start(Open, new ScriptedDice(50), cards: ["bow"]);
        Hero hero = session.HeroInSeat();
        Bomb bomb = session.DropBomb(new GridPosition(4, 1), fuseTurns: 2);

        Assert.Equal(new BombStruck(hero.Id, bomb.Id, 14, false), session.Do(new AttackBomb(hero.Id, bomb.Id)).SingleOf<BombStruck>());
        Assert.Contains("used all your Rumble", session.Refuse(new AttackBomb(hero.Id, bomb.Id)));
    }

    [Fact]
    public void ABombThatGoesOffWhenStruckBlastsAtOnce()
    {
        GameSession session = TestRun.Start(Open, new ScriptedDice(13, 1, 1, 1, 1), cards: ["bow"]);
        Hero hero = session.HeroInSeat();
        Bomb bomb = session.DropBomb(new GridPosition(4, 1), fuseTurns: 2);

        IReadOnlyList<GameEvent> events = session.Do(new AttackBomb(hero.Id, bomb.Id));

        Assert.True(events.SingleOf<BombStruck>().Exploded);
        Assert.Single(events.OfType<BombExploded>());
    }

    [Fact]
    public void ShovingABombRollsItAwayWithoutSettingItOffAndWaterPutsItOut()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            ....,....
            .........
            ....1....
            .........
            .........
            .........
            """);
        Hero hero = session.HeroInSeat();
        Bomb bomb = session.DropBomb(new GridPosition(4, 4), fuseTurns: 2);

        IReadOnlyList<GameEvent> events = session.Do(new Shove(hero.Id, bomb.Id));

        Assert.Equal(new BombMoved("A", bomb.Id, new GridPosition(4, 4), new GridPosition(4, 3)), events.SingleOf<BombMoved>());
        Assert.Equal(new BombDefused("A", bomb.Id, true), events.SingleOf<BombDefused>());
        Assert.False(bomb.Lit);
    }

    [Fact]
    public void DefusingABombTakesTenOrItGoesOff()
    {
        Stats deft = GameRules.HeroBaseStats with { OdHandDice = 3 };
        GameSession good = TestRun.Start(Open, new ScriptedDice(4, 4, 3), stats: deft);
        Bomb bomb = good.DropBomb(new GridPosition(4, 3), fuseTurns: 2);
        Assert.Contains(good.Do(new Disarm(good.HeroInSeat().Id, bomb.Position)), e => e is BombDefused { Doused: false });
        Assert.False(bomb.Lit);

        GameSession bad = TestRun.Start(Open, new ScriptedDice(1, 1, 1, 1, 1, 1, 1, 1), stats: deft);
        Bomb other = bad.DropBomb(new GridPosition(4, 3), fuseTurns: 2);
        IReadOnlyList<GameEvent> events = bad.Do(new Disarm(bad.HeroInSeat().Id, other.Position));
        Assert.Contains(events, e => e is CheckFailed);
        Assert.Single(events.OfType<BombExploded>());
    }

    [Fact]
    public void AnUnlitBombIsPickedUpAsABombCard()
    {
        GameSession session = TestRun.Start(Open);
        Hero hero = session.HeroInSeat();
        session.DropBomb(hero.Position, lit: false);

        session.Do(new PickUpLoot(hero.Id));

        Assert.Equal(["bomb"], hero.Backpack.Select(card => card.Definition.Id));
        Assert.Empty(session.RoomById().Bombs);
    }

    [Fact]
    public void ABombTrapPopsOutALitBombAndDisarmedAnUnlitOne()
    {
        var features = new RoomFeatures { Traps = new Dictionary<GridPosition, TrapSetup> { [new(5, 4)] = new(TrapKind.BombTrap, 3), [new(3, 4)] = new(TrapKind.BombTrap, 3) } };
        GameSession session = TestRun.Start([new RoomSetup("A", TestRun.Room(Open), Features: features)], new ScriptedDice(5, 3));
        Hero hero = session.HeroInSeat();
        session.RoomById().TrapAt(new GridPosition(3, 4))!.Revealed = true;

        Assert.Contains(session.Do(new Disarm(hero.Id, new GridPosition(3, 4))), e => e is BombPlaced { Lit: false });
        session.Do(new RollMovement(hero.Id));
        IReadOnlyList<GameEvent> events = session.Do(new MoveHero(hero.Id, new GridPosition(5, 4)));

        BombPlaced placed = events.SingleOf<BombPlaced>();
        Assert.Equal(new BombPlaced("A", placed.BombId, new GridPosition(5, 4), true, hero.Id), placed);
        Assert.Empty(events.OfType<Damaged>());
        Assert.Equal(2, session.RoomById().BombAt(new GridPosition(5, 4))!.FuseTurns);
    }

    [Fact]
    public void FireSetsABombOffAtOnce()
    {
        GameSession session = TestRun.Start(Open, new ScriptedDice(1, 1, 1, 1), cards: ["firebolt"]);
        Hero hero = session.HeroInSeat();
        Bomb bomb = session.DropBomb(new GridPosition(4, 1), fuseTurns: 2);

        Assert.Single(session.Do(new UseCard(hero.Id, hero.Card("firebolt").Id, bomb.Position)).OfType<BombExploded>());
    }

    [Fact]
    public void BonesBlastedApartStayDown()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            ....k....
            .........
            ........1
            .........
            .........
            .........
            .........
            """, new ScriptedDice(5, 1, 1, 1));
        Monster bones = session.OnlyMonster();
        bones.Hearts = 1;
        session.DropBomb(new GridPosition(4, 3));

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(session.HeroInSeat().Id));

        Assert.Single(events.OfType<MonsterDefeated>());
        Assert.Empty(events.OfType<MonsterCollapsed>());
    }
}
