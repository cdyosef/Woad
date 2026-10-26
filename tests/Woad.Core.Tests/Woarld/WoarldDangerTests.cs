using Woad.Core.Bots;
using Woad.Core.Characters;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;
using Woad.Core.Rules;
using Woad.Core.Tavern;
using Woad.Core.Tests.Support;
using Woad.Core.Woarld;

namespace Woad.Core.Tests.Woarld;

/// <summary>The Woarld Map's dangers: bands, ambushes and toll posts, and the fights out in the open.</summary>
public class WoarldDangerTests
{
    private static readonly ContentCatalog Content = ContentCatalog.LoadDefault();
    private static readonly WoarldMap Map = WoarldMap.LoadDefault();

    private static readonly HeroSetup Hero = new("Wren", GameRules.HeroBaseStats with { Hearts = 6 }, []);

    private static GameSession Fight(IReadOnlyList<string> monsters, bool monstersFirst = false, ScriptedDice? dice = null, Terrain terrain = Terrain.Grass)
    {
        RunSetup setup = Skirmish.CreateRun(Content, new SkirmishSpec("emerald_keep", terrain, monsters, monstersFirst, "Test"), [Hero], seed: 7);
        var session = new GameSession(Content, setup, new GameRandom(7) { Dice = dice ?? new ScriptedDice() });
        session.Begin();
        return session;
    }

    [Fact]
    public void BeatingTheBandEndsTheFightAndThePartyWalksOn()
    {
        GameSession session = Fight(["dire_wolf"], dice: new ScriptedDice(5, 1));
        Hero hero = session.State.Heroes[0];
        Monster wolf = session.State.Rooms[Skirmish.RoomId].Monsters.Single();
        wolf.Hearts = 1;
        hero.Position = wolf.Position with { X = wolf.Position.X - 1 };

        session.Execute(new AttackMonster(hero.Id, wolf.Id, UseWeapon: false));

        Assert.Equal(RunPhase.Ended, session.State.Phase);
        Assert.Equal(HeroStatus.Escaped, hero.Status);
    }

    [Fact]
    public void AnAmbushNobodySawComingGivesTheMonstersTheFirstTurn()
    {
        RunSetup setup = Skirmish.CreateRun(Content, new SkirmishSpec("emerald_keep", Terrain.Forest, ["dire_wolf"], true, "Test"), [Hero], seed: 7);
        var session = new GameSession(Content, setup);
        IReadOnlyList<GameEvent> events = session.Begin();

        Assert.IsType<EnemyPhaseStarted>(events.First(gameEvent => gameEvent is EnemyPhaseStarted or TurnStarted));
    }

    [Fact]
    public void ABridgeTrollFillsFourTilesAndRegeneratesUnlessBurning()
    {
        GameSession session = Fight(["bridge_troll"], terrain: Terrain.Bridge, dice: new ScriptedDice([.. Enumerable.Repeat(1, 40)]));
        Room field = session.State.Rooms[Skirmish.RoomId];
        Monster troll = field.Monsters.Single();
        Assert.All(troll.Tiles, tile => Assert.Same(troll, field.MonsterAt(tile)));
        Assert.Equal(4, troll.Tiles.Count());

        Hero hero = session.State.Heroes[0];
        hero.Position = new GridPosition(troll.Position.X - 1, troll.Position.Y + 1);
        Assert.Contains(troll, session.GetAttackableMonsters());
        Assert.Contains("too mighty", session.CheckShove(troll.Id));

        troll.Hearts = 5;
        IReadOnlyList<GameEvent> round = session.Execute(new EndTurn(hero.Id)).Events;
        Assert.Equal(6, round.SingleOf<MonsterRegenerated>().Hearts);
    }

    [Fact]
    public void TheBridgeTrollCanBeStolenFromAndShovedAtFromAnyOfItsTiles()
    {
        GameSession session = Fight(["bridge_troll"], terrain: Terrain.Bridge);
        Monster troll = session.State.Rooms[Skirmish.RoomId].Monsters.Single();
        Hero hero = session.State.Heroes[0];

        // Beside its bottom-right tile, well away from its top-left one.
        hero.Position = new GridPosition(troll.Position.X + 2, troll.Position.Y + 1);

        Assert.Null(session.CheckSteal(troll.Id));
        Assert.Contains("too mighty", session.CheckShove(troll.Id));
    }

    [Fact]
    public void AHiredCompanionGoesAfterTheBridgeTroll()
    {
        Character owner = Character.Create("Owner", "frost", StatAllocation.None, Content, DateTimeOffset.UtcNow);
        owner.EarnGold(10);
        var offer = new RecruitOffer("Hired", new Appearance("knight"), "blood", new StatAllocation { Hearts = 4, Luck = 2 }, [], Level: 1, Price: 10);
        Character recruit = TavernServices.Hire(owner, offer, Content, DateTimeOffset.UtcNow).Recruit!;
        RunSetup setup = Skirmish.CreateRun(Content, new SkirmishSpec("emerald_keep", Terrain.Bridge, ["bridge_troll"], false, "Test"),
            [owner.ToHeroSetup() with { BaseStats = owner.BaseStats with { Hearts = 40 } }, recruit.ToHeroSetup(0)], seed: 1);
        var session = new GameSession(Content, setup, new GameRandom(1));
        session.Begin();
        Hero player = session.State.Heroes[0], companion = session.State.Heroes[1];
        session.Execute(new OrderCompanion(player.Id, companion.Id, CompanionOrder.Fight));

        bool attacked = false;
        for (int i = 0; i < 200 && !attacked && session.State.Phase == RunPhase.InProgress; i++)
        {
            Hero active = session.State.ActiveHero!;
            GameCommand command = active.IsCompanion ? CompanionBot.NextCommand(session) : new EndTurn(active.Id);
            attacked = command is AttackMonster;
            Assert.True(session.Execute(command).IsAccepted, $"{command} was refused");
        }

        Assert.True(attacked, "the companion never attacked the troll");
    }

    [Fact]
    public void RetreatingBeatsTheChaseAndLeavesTheFieldWithoutWinningIt()
    {
        // The hero's Od Hand 5 against the wolf's chase: two dice (1, 1) and 3.
        GameSession session = Fight(["dire_wolf"], dice: new ScriptedDice(5, 1, 1));
        Hero hero = session.State.Heroes[0];

        IReadOnlyList<GameEvent> events = session.Execute(new Retreat(hero.Id)).Events;

        Assert.Contains(new HeroRetreated(hero.Id), events);
        Assert.Equal(RunPhase.Ended, session.State.Phase);
        Assert.Empty(session.State.WinnerIds);
        Assert.True(RunResults.For(session.State).Single().Retreated);
    }

    [Fact]
    public void AFailedRetreatBoltsAwayFromTheMonstersAndFallsFlat()
    {
        // Od Hand 1 against 5 + 5 + 3; then the Wend die (4) carries the hero off.
        GameSession session = Fight(["dire_wolf"], dice: new ScriptedDice(1, 5, 5, 4));
        Hero hero = session.State.Heroes[0];
        Monster wolf = session.State.Rooms[Skirmish.RoomId].Monsters.Single();
        int before = hero.Position.ManhattanDistanceTo(wolf.Position);

        IReadOnlyList<GameEvent> events = session.Execute(new Retreat(hero.Id)).Events;

        Assert.Single(events.OfType<Routed>());
        Assert.True(hero.Prone);
        Assert.True(hero.Position.ManhattanDistanceTo(wolf.Position) > before);
        Assert.Equal(RunPhase.InProgress, session.State.Phase);
    }

    [Fact]
    public void ThereIsNoRetreatingInADungeon()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1....
            .........
            .........
            .........
            ......m..
            """);

        Assert.Contains("dungeon", session.CheckRetreat());
    }

    [Fact]
    public void ACaptainsMaceStunsOnAHitAndOnceMoreForEachSixOnItsDie()
    {
        // Against an unarmored hero the Captain splits its two dice into two attacks. The first: a 3, the mace's
        // die a 6 (rolling a 2 straight in), +2: 13 against the hero's 1. Then 1s all round.
        GameSession session = Fight(["guard_captain"], dice: new ScriptedDice([3, 6, 2, .. Enumerable.Repeat(1, 20)]));
        Hero hero = session.State.Heroes[0];
        Monster captain = session.State.Rooms[Skirmish.RoomId].Monsters.Single();
        hero.Position = captain.Position with { X = captain.Position.X - 1 };

        IReadOnlyList<GameEvent> events = session.Execute(new EndTurn(hero.Id)).Events;

        Assert.Equal(2, events.OfType<Stunned>().First().Stacks);
    }

    [Fact]
    public void TheWoarldHasTollPostsAndHiddenAmbushesAndBandsComeWithTheHour()
    {
        var dangers = WoarldDangers.Create(Map, Content, 1);
        Assert.NotEmpty(dangers.Tolls);
        Assert.Contains(dangers.Ambushes, ambush => ambush.Monsters.SequenceEqual(["bridge_troll"]));
        Assert.All(dangers.Ambushes, ambush => Assert.False(ambush.Revealed));

        WoarldJourney journey = WoarldJourney.Begin(Map, 1);
        dangers.AfterStep(journey, Content, 1, 10, [], new Pcg32(1, 1), []);
        Assert.Equal(WoarldDangers.DayBands, dangers.Bands.Count);
    }

    [Fact]
    public void ScoutingFindsWhatItsTotalReaches()
    {
        var dangers = new WoarldDangers();
        WoarldJourney journey = WoarldJourney.Begin(Map, 1);
        GridPosition near = journey.Leader with { X = journey.Leader.X + 2 };
        dangers.Ambushes.Add(new HiddenAmbush { X = near.X, Y = near.Y, Name = "wolves", Monsters = ["dire_wolf"], Difficulty = 5 });
        dangers.Ambushes.Add(new HiddenAmbush { X = near.X, Y = near.Y + 1, Name = "goblins", Monsters = ["goblin_raider"], Difficulty = 20 });

        // One hero with one Od Hand die, rolling a 6.
        IReadOnlyList<HiddenAmbush> found = dangers.Scout(journey, [1], new ScriptedDice(6), out int total);

        Assert.Equal(6, total);
        Assert.Equal(["wolves"], found.Select(ambush => ambush.Name));
        Assert.Equal(journey.Day, dangers.ScoutedDay);
    }

    [Fact]
    public void SussingOutATollSeesThroughRealGuardsMoreEasilyThanBandits()
    {
        // Real guards: 5 and a 1. A hero rolling a 6 sees it.
        var guards = new TollPost { Bandits = false };
        Assert.True(WoarldDangers.SussOut(guards, [1], new ScriptedDice(1, 6)));

        // Bandits: 5 and three dice (3, 3, 3): 14. Two heroes' 6s don't come close.
        var bandits = new TollPost { Bandits = true };
        Assert.False(WoarldDangers.SussOut(bandits, [1, 1], new ScriptedDice(3, 3, 3, 6, 6)));
        Assert.True(bandits.Sussed);
        Assert.Equal(["guard_captain", "spear_guard", "spear_guard", "archer_guard"], WoarldDangers.TollCompany(bandits));

        bandits.Known = true;
        Assert.Equal("bandit_captain", WoarldDangers.TollCompany(bandits)[0]);
    }

    [Fact]
    public void TheCowardsCloakTurnsFightingDiceToRunningDiceSlowsWhatsBesideYouAndIsGone()
    {
        RunSetup setup = Skirmish.CreateRun(Content, new SkirmishSpec("emerald_keep", Terrain.Grass, ["dire_wolf"], false, "Test"),
            [Hero with { CardIds = ["cowards_cloak"] }], seed: 7);
        var session = new GameSession(Content, setup);
        session.Begin();
        Hero hero = session.State.Heroes[0];
        Monster wolf = session.State.Rooms[Skirmish.RoomId].Monsters.Single();
        hero.Position = wolf.Position with { X = wolf.Position.X - 1 };
        CardInstance cloak = hero.Card("cowards_cloak");
        int wend = session.MoveDiceLeft, fleeing = session.AttackDiceLeft + session.OdHandDiceLeft;

        IReadOnlyList<GameEvent> events = session.Execute(new UseCard(hero.Id, cloak.Id)).Events;

        Assert.Equal(wend + fleeing, session.MoveDiceLeft);
        Assert.Equal((0, 0), (session.AttackDiceLeft, session.OdHandDiceLeft));
        Assert.True(wolf.Slowed);
        Assert.Equal([wolf.Id], events.SingleOf<CowardsFlight>().SlowedMonsterIds);
        Assert.Null(hero.FindCard(cloak.Id));
    }

    [Fact]
    public void CravenTheCowherderGivesTheCloakToAProvenCowardOnce()
    {
        Character coward = Character.Create("Wren", "frost", StatAllocation.None, Content, DateTimeOffset.UnixEpoch);
        Assert.False(TavernServices.CowardsCloak(coward, Content).Done);

        coward.ChangeStanding(cowards: TavernServices.CowardsForTheCloak);
        Assert.True(TavernServices.CowardsCloak(coward, Content).Done);
        Assert.Contains("cowards_cloak", coward.Backpack.Concat(coward.Equipped));
        Assert.False(TavernServices.CowardsCloak(coward, Content).Done);
    }

    [Fact]
    public void FleeingIsTheWendCheckAgainstTheBandsChase()
    {
        var band = new Band { Monsters = ["dire_wolf", "dire_wolf"] };
        // Two heroes, one Wend die each: two dice (6, 6) against the band's three (1, 1, 1).
        Assert.True(WoarldDangers.Flees(band, [1, 1], new ScriptedDice(6, 6, 1, 1, 1), out int total, out int chase));
        Assert.Equal((12, 3), (total, chase));
    }
}
