using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Saves;
using Woad.Core.Tavern;
using Woad.Core.Woarld;

namespace Woad.Core.Tests.Saves;

public class RaidSaveTests
{
    private static readonly ContentCatalog Content = ContentCatalog.LoadDefault();
    private static readonly WoarldMap Map = WoarldMap.LoadDefault();

    private static Character Hero(string name, int gold = 0)
    {
        Character hero = Character.Create(name, "moss", new StatAllocation { Hearts = 2 }, Content, DateTimeOffset.UnixEpoch);
        hero.EarnGold(gold);
        return hero;
    }

    [Fact]
    public void AWoundedHeroStartsTheDungeonWounded()
    {
        HeroSetup wounded = Hero("Ash").ToHeroSetup() with { Hearts = 2 };
        var session = new GameSession(Content, DungeonGenerator.CreateRun(Content, "emerald_keep", [wounded, Hero("Bram").ToHeroSetup()], seed: 3));

        Assert.Equal(2, session.State.Heroes[0].Hearts);
        Assert.Equal(session.State.Heroes[1].MaxHearts, session.State.Heroes[1].Hearts);
    }

    [Fact]
    public void HeartsAreKeptBelowFullAndForgottenAtFull()
    {
        var raid = new RaidSave { Name = "Ash's Raid", Party = [Guid.NewGuid()] };
        Guid ash = raid.Party[0];

        Assert.Equal(5, raid.HeartsOf(ash, 5));
        raid.SetHearts(ash, 2, 5);
        Assert.Equal(2, raid.HeartsOf(ash, 5));
        Assert.Equal(2, raid.Heal(ash, 2, 5));
        Assert.Equal(1, raid.Heal(ash, 4, 5));
        Assert.Empty(raid.Hearts);
    }

    [Fact]
    public void SurvivorsKeepTheirWoundsAndTheFallenComeHomeOnOneHeart()
    {
        var raid = new RaidSave { Name = "Ash's Raid", Party = [Guid.NewGuid(), Guid.NewGuid()] };
        HeroRunResult Result(Guid id, HeroStatus status, int hearts) => new()
        {
            HeroId = 1, CharacterId = id, Name = "x", Seat = 0, Portrait = "", Status = status, Won = false, Kills = 0, Xp = 0, Gold = 0,
            DeepestFloor = 1, Hearts = hearts, Equipped = [], Belt = [], Backpack = [],
        };

        raid.ApplyRun([Result(raid.Party[0], HeroStatus.Escaped, 3), Result(raid.Party[1], HeroStatus.Dead, 0)], _ => 5);

        Assert.Equal(3, raid.HeartsOf(raid.Party[0], 5));
        Assert.Equal(RaidSave.HeartsAfterFalling, raid.HeartsOf(raid.Party[1], 5));
    }

    [Fact]
    public void ARoomHealsAHeartADay()
    {
        var raid = new RaidSave { Name = "Ash's Raid", Party = [Guid.NewGuid()] };
        Guid ash = raid.Party[0];
        raid.SetHearts(ash, 1, 5);
        raid.RentRoom(ash, day: 2);

        raid.HealRooms(4, _ => 5);
        Assert.Equal(3, raid.HeartsOf(ash, 5));
        raid.LeaveRoom(ash, day: 5, maxHearts: 5);
        Assert.Equal(4, raid.HeartsOf(ash, 5));
        Assert.Empty(raid.Rooms);
    }

    [Fact]
    public void RentingARoomCostsTwoGold()
    {
        var raid = new RaidSave { Name = "Ash's Raid", Party = [Guid.NewGuid()] };
        Character ash = Hero("Ash", 3), bram = Hero("Bram");

        Assert.True(TavernServices.RentRoom(raid, 1, ash, bram).Done);
        Assert.Equal(1, ash.Gold);
        Assert.Equal(1, raid.Rooms[bram.Id]);
        Assert.False(TavernServices.RentRoom(raid, 1, ash, ash).Done);
    }

    [Fact]
    public void SleepingTheNightPassesADayAndHealsEveryone()
    {
        Character ash = Hero("Ash", 10), bram = Hero("Bram"), upstairs = Hero("Cole");
        var raid = new RaidSave { Name = "Ash's Raid", Party = [ash.Id, bram.Id] };
        int full = ash.MaxHearts(Content);
        raid.SetHearts(ash.Id, 2, full);
        raid.SetHearts(upstairs.Id, 2, full);
        raid.RentRoom(upstairs.Id, 1);
        WoarldJourney journey = WoarldJourney.Begin(Map, 2);

        ServiceResult night = TavernServices.SleepTheNight(raid, journey, ash, [ash, bram], _ => full);

        Assert.True(night.Done, night.Message);
        Assert.Equal(6, ash.Gold);
        Assert.Equal(2, journey.Day);
        Assert.Equal(3, raid.HeartsOf(ash.Id, full));
        Assert.Equal(full, raid.HeartsOf(bram.Id, full));
        Assert.Equal(3, raid.HeartsOf(upstairs.Id, full));
    }

    [Fact]
    public void MaxHeartsCountsGear()
    {
        Character ash = Hero("Ash");
        int bare = ash.MaxHearts(Content);

        Assert.Equal(GameRules.HeroBaseStats.Hearts + 2, bare);
    }

    [Fact]
    public void AJourneyResumesWhereARaidLeftIt()
    {
        WoarldJourney journey = WoarldJourney.Begin(Map, 3);
        journey.Step(Direction.East);
        journey.Step(Direction.East);
        journey.PassDay();
        JourneyPlace place = journey.Place;

        WoarldJourney shared = WoarldJourney.Begin(Map, 3);
        shared.Resume(place);

        Assert.Equal(place.Leader, shared.Leader);
        Assert.Equal(place.Trail, shared.Trail);
        Assert.Equal((2, 0, place.TotalSteps), (shared.Day, shared.StepsToday, shared.TotalSteps));
        Assert.Throws<ArgumentException>(() => shared.Resume(place with { Leader = new GridPosition(0, 0) }));
    }

    [Fact]
    public void RaidsAreSavedOneFileEachAndNamedForTheirLeader()
    {
        string folder = Path.Combine(Path.GetTempPath(), "woad-raids-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new RaidSaveStore(folder);
            Assert.Equal("Ash's Raid", store.NameFor("Ash"));
            var first = new RaidSave { Name = store.NameFor("Ash"), Party = [Guid.NewGuid(), Guid.NewGuid()], FriendlyFire = true, Location = RaidLocation.Map };
            first.Leader = first.Party[1];
            first.SetHearts(first.Party[0], 2, 5);
            first.RentRoom(Guid.NewGuid(), 3);
            first.Place = new JourneyPlace(new GridPosition(14, 15), [new GridPosition(14, 16)], 3, 120, 2120);
            store.Save(first);
            Assert.Equal("Ash's Raid (2)", store.NameFor("Ash"));
            store.Save(new RaidSave { Name = store.NameFor("Ash"), Party = [Guid.NewGuid()] });

            (IReadOnlyList<RaidSave> raids, IReadOnlyList<string> problems) = store.LoadAll();

            Assert.Empty(problems);
            Assert.Equal(["Ash's Raid (2)", "Ash's Raid"], raids.Select(raid => raid.Name));
            RaidSave loaded = raids[1];
            Assert.Equal(first.Party, loaded.Party);
            Assert.Equal(first.Leader, loaded.Leader);
            Assert.True(loaded.FriendlyFire);
            Assert.Equal(RaidLocation.Map, loaded.Location);
            Assert.Equal(2, loaded.HeartsOf(first.Party[0], 5));
            Assert.Single(loaded.Rooms);
            Assert.Equal(first.Place.Leader, loaded.Place!.Leader);
            Assert.Equal(first.Place.Trail, loaded.Place.Trail);

            store.RunFile(loaded).Save(new SavedRun { ThemeId = "emerald_keep", Seed = 1, Heroes = [] });
            store.Delete(loaded);
            Assert.Single(store.LoadAll().Raids);
            Assert.False(store.RunFile(loaded).Exists);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void ADamagedRaidIsReported()
    {
        Assert.Throws<InvalidDataException>(() => RaidSave.FromJson("{ nope"));
        Assert.Throws<InvalidDataException>(() => RaidSave.FromJson("""{ "version": 1, "name": "Empty", "party": [] }"""));
    }
}
