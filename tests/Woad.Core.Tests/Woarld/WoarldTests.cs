using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Tests.Support;
using Woad.Core.Woarld;

namespace Woad.Core.Tests.Woarld;

public class WoarldTests
{
    private static readonly WoarldMap Map = WoarldMap.LoadDefault();

    private static WoarldPlace Place(string id) => Map.Places.Single(place => place.Id == id);

    [Fact]
    public void TheWoarldHasTheInnAndBothDungeons()
    {
        Assert.Equal(["waylay_inn", "emerald_keep", "sunken_temple"], Map.Places.Select(place => place.Id));
        Assert.Equal(Place("waylay_inn").Position, Map.Start);
        Assert.Equal(PlaceKind.Tavern, Place("waylay_inn").Kind);
        Assert.Equal("emerald_keep", Place("sunken_temple").UnlockedBy);
    }

    [Fact]
    public void EveryPlaceCanBeWalkedToFromTheStart()
    {
        WoarldJourney journey = WoarldJourney.Begin(Map, 1);
        journey.RevealAll();

        Assert.All(Map.Places, place => Assert.NotNull(journey.PathTo(place.Position)));
    }

    [Fact]
    public void OnlyTheKeepIsOpenAtFirstAndBeatingItOpensTheTemple()
    {
        WoarldJourney journey = WoarldJourney.Begin(Map, 2);

        Assert.True(journey.IsOpen(Place("emerald_keep")));
        Assert.False(journey.IsOpen(Place("sunken_temple")));
        Assert.True(journey.IsRevealed(Place("emerald_keep").Position), "the open dungeon's site is known from the start");
        Assert.False(journey.IsRevealed(Place("sunken_temple").Position), "the sealed temple starts under the clouds");

        Assert.Equal([Place("sunken_temple")], journey.MarkBeaten("emerald_keep"));
        Assert.True(journey.IsOpen(Place("sunken_temple")));
        Assert.True(journey.IsRevealed(Place("sunken_temple").Position), "opening a place clears the clouds from it");
        Assert.Empty(journey.MarkBeaten("emerald_keep"));
    }

    [Fact]
    public void ThePartyFollowsTheLeaderInALine()
    {
        WoarldJourney journey = WoarldJourney.Begin(Map, 3);
        GridPosition start = Map.Start;
        Assert.Equal([start, start], journey.Trail);

        Assert.True(journey.Step(Direction.East).Moved);
        Assert.True(journey.Step(Direction.East).Moved);
        Assert.True(journey.Step(Direction.East).Moved);

        Assert.Equal(start with { X = start.X + 3 }, journey.Leader);
        Assert.Equal([start with { X = start.X + 2 }, start with { X = start.X + 1 }], journey.Trail);
        Assert.Equal(3, journey.TotalSteps);
    }

    [Fact]
    public void WaterMountainsAndPartsUnknownBlockTheWay()
    {
        WoarldJourney journey = WoarldJourney.Begin(Map, 1);
        // North from the inn to the lake's eastern shore.
        foreach (Direction step in new[] { Direction.North, Direction.North, Direction.North, Direction.West })
        {
            Assert.True(journey.Step(step).Moved);
        }

        StepResult intoTheLake = journey.Step(Direction.West);
        Assert.False(intoTheLake.Moved);
        Assert.Contains("Water", intoTheLake.Error);
        Assert.Equal(new GridPosition(13, 12), journey.Leader);
        Assert.Equal(4, journey.TotalSteps);
    }

    [Fact]
    public void WalkingClearsTheClouds()
    {
        WoarldJourney journey = WoarldJourney.Begin(Map, 1);
        var ahead = new GridPosition(Map.Start.X + 5, Map.Start.Y);
        Assert.False(journey.IsRevealed(ahead));
        Assert.Null(journey.PathTo(ahead));

        journey.Step(Direction.East);
        journey.Step(Direction.East);

        Assert.True(journey.IsRevealed(ahead));
        Assert.Equal([Direction.East, Direction.East, Direction.East], journey.PathTo(ahead));
    }

    [Fact]
    public void DaysAreFiveHundredStepsOfDaylightThenFiveHundredOfNight()
    {
        WoarldJourney journey = WoarldJourney.Begin(Map, 1);
        Assert.True(journey.IsDay);
        Assert.Equal(500, journey.StepsUntilChange);

        StepResult step = default!;
        for (int i = 0; i < 500; i++)
        {
            step = journey.Step(i % 2 == 0 ? Direction.East : Direction.West);
        }

        Assert.True(step.NightFell);
        Assert.False(journey.IsDay);
        Assert.Equal(1, journey.Day);

        for (int i = 0; i < 500; i++)
        {
            step = journey.Step(i % 2 == 0 ? Direction.East : Direction.West);
        }

        Assert.True(step.NewDay);
        Assert.Equal(2, journey.Day);
        Assert.True(journey.IsDay);
    }

    [Fact]
    public void RaidingADungeonTakesTheRestOfTheDay()
    {
        WoarldJourney journey = WoarldJourney.Begin(Map, 1);
        journey.Step(Direction.East);

        journey.PassDay();

        Assert.Equal((2, 0), (journey.Day, journey.StepsToday));
    }

    [Fact]
    public void ARestRollHealsOnSixesAndForEveryTen()
    {
        // 6 heals and rolls again; 6 again; then 5: 17 in all, so 2 for the sixes and 1 for the ten.
        Assert.Equal(new[] { 6, 6, 5 }, RestRoll.Roll(new ScriptedDice(6, 6, 5)).Dice);
        Assert.Equal(3, RestRoll.Roll(new ScriptedDice(6, 6, 5)).Hearts);
        Assert.Equal(0, RestRoll.Roll(new ScriptedDice(4)).Hearts);
        // Food's extra die: 5 + 5 makes ten.
        Assert.Equal(1, RestRoll.Roll(new ScriptedDice(5, 5), extraDice: 1).Hearts);
        Assert.Equal(RestRoll.MaxDice, RestRoll.Roll(new ScriptedDice([.. Enumerable.Repeat(6, RestRoll.MaxDice)])).Dice.Count);
        // A drink's +4 makes 3 + 4 + 3 = 10.
        Assert.Equal(1, RestRoll.Roll(new ScriptedDice(3, 3), extraDice: 1, bonus: 4).Hearts);
    }

    [Fact]
    public void RestingRollsForEveryoneAndTakesFiftySteps()
    {
        WoarldJourney journey = WoarldJourney.Begin(Map, 2);

        (IReadOnlyList<RestRoll> rolls, _, _) = journey.Rest(new ScriptedDice(3, 6, 2), partySize: 2);

        Assert.Equal([[3], [6, 2]], rolls.Select(roll => roll.Dice.ToArray()));
        Assert.Equal(WoarldJourney.RestSteps, journey.StepsToday);
    }

    [Fact]
    public void TheJourneySavesAndLoads()
    {
        WoarldJourney journey = WoarldJourney.Begin(Map, 3);
        journey.Step(Direction.East);
        journey.Step(Direction.South);
        journey.MarkBeaten("emerald_keep");
        journey.PassDay();

        WoarldJourney loaded = WoarldJourney.FromJson(journey.ToJson(), Map);

        Assert.Equal(journey.Leader, loaded.Leader);
        Assert.Equal(journey.Trail, loaded.Trail);
        Assert.Equal((2, 0, 2), (loaded.Day, loaded.StepsToday, loaded.TotalSteps));
        Assert.Equal(["emerald_keep"], loaded.Beaten);
        Assert.Equal(journey.ToJson(), loaded.ToJson());
    }

    [Fact]
    public void ANewRaidGathersThePartyAtTheInn()
    {
        WoarldJourney journey = WoarldJourney.Begin(Map, 3);
        journey.Step(Direction.East);
        journey.Step(Direction.East);

        journey.ReturnTo(Map.Start);

        Assert.Equal(Map.Start, journey.Leader);
        Assert.Equal([Map.Start, Map.Start], journey.Trail);
        Assert.Throws<ArgumentException>(() => journey.ReturnTo(new GridPosition(0, 0)));
    }

    [Fact]
    public void TheLineGrowsAndShrinksWithTheParty()
    {
        WoarldJourney journey = WoarldJourney.Begin(Map, 3);
        journey.Step(Direction.East);

        journey.SetPartySize(5);
        Assert.Equal(4, journey.Trail.Count);
        journey.SetPartySize(1);
        Assert.Empty(journey.Trail);
    }

    [Theory]
    [InlineData("""{"version": 1, "woarldId": "elsewhere"}""")]
    [InlineData("not json")]
    public void ForeignOrDamagedSavesAreRefused(string json) =>
        Assert.Throws<InvalidDataException>(() => WoarldJourney.FromJson(json, Map));

    [Fact]
    public void ABrokenMapIsReported()
    {
        const string json = """
            { "id": "bad", "name": "Bad", "start": { "x": 0, "y": 0 }, "rows": ["~.", "..x"],
              "places": [{ "id": "nowhere", "name": "Nowhere", "kind": "dungeon", "x": 1, "y": 0, "themeId": "missing" }] }
            """;

        var problem = Assert.Throws<InvalidContentException>(() => WoarldMap.Parse(json, ContentCatalog.LoadDefault()));
        Assert.Contains(problem.Errors, error => error.Contains("row 1"));
    }
}
