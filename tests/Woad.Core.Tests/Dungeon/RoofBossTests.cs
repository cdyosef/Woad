using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Saves;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Dungeon;

/// <summary>The Emerald Keep's roof: Baron Herkullos on his beetle, his Grasshopper Knights, and his helm.</summary>
public class RoofBossTests
{
    private static readonly ContentCatalog Content = ContentCatalog.LoadDefault();

    private static HeroSetup[] Party(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new HeroSetup($"Hero{i}", GameRules.HeroBaseStats with { Hearts = 40, AttackDice = 3 }, GameRules.StarterKit))];

    /// <summary>A run begun on the roof itself.</summary>
    private static GameSession OnTheRoof(int heroes = 1, IReadOnlyList<string>? claimed = null)
    {
        RunSetup run = DungeonGenerator.CreateRun(Content, "emerald_keep", Party(heroes), seed: 42);
        var session = new GameSession(Content, run with { StartRoomId = DungeonGenerator.RoofId, UniquesClaimed = claimed ?? [] });
        session.Begin();
        return session;
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(4, 4)]
    public void TheBaronHoldsTheRoofWithHisGrasshopperKnights(int heroes, int knights)
    {
        RoomSetup roof = DungeonGenerator.CreateRun(Content, "emerald_keep", Party(heroes), seed: 42).Rooms.Single(room => room.Id == DungeonGenerator.RoofId);

        Assert.Single(roof.Template.Monsters, spawn => spawn.Monster.Id == "baron_herkullos");
        Assert.Equal(knights, roof.Template.Monsters.Count(spawn => spawn.Monster.Id == "grasshopper_knight"));
        Assert.All(roof.Template.Monsters, spawn => Assert.Equal(TileKind.Floor, roof.Template.Layout[spawn.Position]));
        MonsterDefinition baron = Content.GetMonster("baron_herkullos");
        Assert.True(baron is { Flying: true, Size: 2, CarriesOff: true, KnocksBack: true });
        Assert.Contains(baron.Carries, carried => carried.Card == "emerald_baron_helm");
    }

    [Fact]
    public void TheBeetleCarriesAHeroOffToTheOpenAirAndTheyFallToTheCourtyard()
    {
        GameSession session = OnTheRoof();
        Room roof = session.State.Rooms[DungeonGenerator.RoofId];
        Hero hero = session.State.Heroes[0];
        Monster baron = roof.Monsters.Single(monster => monster.Definition.Id == "baron_herkullos");
        foreach (Monster other in roof.Monsters.Where(monster => monster != baron).ToList())
        {
            roof.RemoveMonster(other);
        }

        // Four tiles in from the edge: the beetle reaches the open air with tiles to spare.
        hero.Position = new GridPosition(1, 4);
        var events = new List<GameEvent>();
        session.CarryOff(roof, baron, hero, events);

        CarriedOff carried = Assert.Single(events.OfType<CarriedOff>());
        Assert.Equal(TileKind.Pit, roof.Layout[carried.Path[^1]]);
        Assert.Equal(session.State.Rooms.Values.Single(room => room.Floor == 1 && room.Level == 0 && room.Role == RoomRole.Arrival).Id, hero.RoomId);
        Assert.Contains(events, gameEvent => gameEvent is HeroFell);
    }

    [Fact]
    public void FarFromTheEdgeTheBeetleDropsThemShortOfIt()
    {
        GameSession session = OnTheRoof();
        Room roof = session.State.Rooms[DungeonGenerator.RoofId];
        Hero hero = session.State.Heroes[0];
        Monster baron = roof.Monsters.Single(monster => monster.Definition.Id == "baron_herkullos");
        hero.Position = roof.Layout.Center with { X = roof.Layout.Center.X + 1 };
        GridPosition start = hero.Position;

        var events = new List<GameEvent>();
        session.CarryOff(roof, baron, hero, events);

        Assert.Equal(DungeonGenerator.RoofId, hero.RoomId);
        Assert.Equal(GameSession.CarryOffReach, Assert.Single(events.OfType<CarriedOff>()).Path.Count);
        Assert.True(hero.Position.ManhattanDistanceTo(start) > 0);
    }

    [Fact]
    public void TheHelmAddsAShoveDieAndMakesShovesCountTwice()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            ..1d.....
            .........
            .........
            .........
            .........
            .........
            """, new ScriptedDice(5, 5, 5, 5), stats: GameRules.HeroBaseStats with { AttackDice = 3 }, cards: ["baron_helm"]);
        Hero hero = session.HeroInSeat();
        Assert.True(hero.DoublePush && hero.PushDice == 1);
        Monster monster = session.OnlyMonster();

        Assert.Equal(4, session.Do(new Shove(hero.Id, monster.Id, Dice: 3)).SingleOf<AttackRolled>().Roll.Faces.Count);
        if (session.State.Turn!.PendingRoll is not null)
        {
            session.Do(new ConfirmAttack(hero.Id));
        }

        Assert.Equal(new GridPosition(5, 3), monster.Position);
    }

    [Fact]
    public void AClaimedUniqueDropsItsStrongboxInstead()
    {
        var raid = new RaidSave { Name = "Ash's Raid", Party = [Guid.NewGuid()] };
        HeroRunResult escaped = new()
        {
            HeroId = 1, CharacterId = Guid.NewGuid(), Name = "Ash", Seat = 0, Portrait = "", Status = HeroStatus.Escaped, Won = true, Kills = 0, Xp = 0, Coins = 0, DeepestFloor = 6,
            Equipped = ["emerald_baron_helm"], Belt = [], Backpack = [],
        };

        raid.ClaimUniques([escaped], Content);

        Assert.Equal(["emerald_baron_helm"], raid.UniquesClaimed);
        Assert.Equal("barons_strongbox", Content.GetCard("emerald_baron_helm").UniqueElse);
        Assert.Equal(120, Content.GetCard("barons_strongbox").Value);
    }
}
