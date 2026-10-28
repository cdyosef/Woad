using Godot;
using Woad.Core.Characters;
using Woad.Core.Grid;
using Woad.Core.Rules;
using Woad.Core.Tavern;
using Woad.Core.Woarld;
using Woad.Scenes.Game;
using Woad.Scenes.Ui;
using Woad.Scenes.Woarld;
using static Woad.Scenes.Debug.UiDriver;

namespace Woad.Scenes;

/// <summary>
/// Debug modes for the Woarld Map's dangers, on a fresh Woarld kept in memory (nothing saved).
/// <c>-- --encountertest</c>: Wren steps onto a toll post (Suss Out, then Turn back, noted by Cowards),
/// then a band catches up with her and she chooses to Fight; and she Scouts.
/// <c>-- --skirmishtest</c>: a fight under a bridge against the Bridge Troll, to see it on the board.
/// </summary>
public partial class Main
{
    private async void RunEncounterTest()
    {
        try
        {
            RaidWorld world = RaidWorld.Begin(WoarldMap.LoadDefault());
            Character wren = Character.Create("Wren", "frost", StatAllocation.None, _content, DateTimeOffset.UtcNow);
            var screen = new WoarldMapScreen { Journey = world.Journey, Party = [wren], World = world, Content = _content };
            SkirmishSpec? fight = null;
            screen.FightRequested += spec => fight = spec;
            Show(screen);
            await NextFrame(this);
            Check(screen.ScoutButton.Visible, "the Scout button is there on a raid's map");

            WoarldDangers dangers = world.Dangers(_content);
            dangers.Tolls.Clear();
            dangers.Ambushes.Clear();
            GridPosition start = world.Journey.Leader;
            GridPosition east = start with { X = start.X + 1 };
            dangers.Tolls.Add(new TollPost { X = east.X, Y = east.Y, Bandits = true });
            screen.TakeStep(Direction.East);
            await NextFrame(this);
            Check(screen.Encounter.Visible && screen.Encounter.Buttons.Any(button => button.Text == "Suss Out"), "stepping onto a toll post asks: Pay, Turn back, Fight or Suss Out");

            await Click(this, screen.Encounter.Buttons.First(button => button.Text == "Suss Out"));
            Check(screen.Encounter.Visible && screen.Encounter.Buttons.All(button => button.Text != "Suss Out"), "Suss Out is tried once, then the choices come back");

            await Click(this, screen.Encounter.Buttons.First(button => button.Text == "Turn back"));
            Check(world.Journey.Leader == start && wren.CowardStanding == 1, "Turn back walks back the way she came, and Cowards note it");

            dangers.Tolls.Clear();
            dangers.Bands.Clear();
            dangers.Bands.Add(new Band { Id = 99, Name = "a pack of Dire Wolves", Monsters = ["dire_wolf", "dire_wolf"], X = start.X + 2, Y = start.Y });
            screen.TakeStep(Direction.East);
            await NextFrame(this);
            Check(screen.Encounter.Visible && screen.Encounter.Buttons.Any(button => button.Text == "Flee"), "a band catching up asks: Fight or Flee");
            await Click(this, screen.Encounter.Buttons.First(button => button.Text == "Fight"));
            Check(fight is { Kind: EncounterKind.Band, BandId: 99 } && fight.Monsters.Count == 2, "Fight starts a skirmish against the band");

            await Click(this, screen.ScoutButton);
            Check(dangers.ScoutedDay == world.Journey.Day, "Scout scouts");

            wren.GiveCard("flame_scroll", _content);
            await Click(this, screen.PartyButton);
            Check(screen.PartyMenu.Visible, "the Party button opens the Party menu on the map");
            await Click(this, screen.PartyMenu.Find("Card0_flame_scroll")!);
            Check(screen.PartyMenu.Find("Learn") is { Disabled: false }, "a scroll with a spell on it can be studied from the Party menu");
            await Click(this, screen.PartyMenu.Find("Learn")!);
            Check(!wren.Backpack.Concat(wren.Belt).Contains("flame_scroll"), "studying it uses the scroll up");
            Check(wren.Spells.Contains("flame_blast") ? screen.PartyMenu.Note.Contains("learns Flame Blast") : screen.PartyMenu.Note.Contains("goes off"),
                $"and says what came of it ({screen.PartyMenu.Note})");
            await Click(this, screen.PartyMenu.Find("Close")!);
            Check(!screen.PartyMenu.Visible, "and Close puts it away");

            GD.Print("Encounter test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Encounter test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    private async void RunSkirmishTest(ulong seed)
    {
        try
        {
            RunSetup setup = Skirmish.CreateRun(_content, new SkirmishSpec("emerald_keep", Terrain.Bridge, ["bridge_troll"], true, "Ambush: a Bridge Troll!"),
                [DemoRun.DebugHeroes[0] with { BaseStats = DemoRun.DebugHeroes[0].BaseStats with { Hearts = 30 } }], seed);
            var game = new GameScreen { Setup = setup, Content = _content };
            Show(game);
            await game.WaitUntilIdle();
            Woad.Core.Model.Monster troll = game.Session.State.Rooms[Skirmish.RoomId].Monsters.Single();
            Check(troll.Tiles.Count() == 4, "the Bridge Troll fills four tiles");
            Check(game.Session.State.Round >= 1, "the troll got the first turn, and the round goes on");
            Check(game.RetreatButton.Visible && game.SearchButton.Text == "Search", "out on the Woarld Map, Retreat is up top and Search stays");

            GD.Print("Skirmish test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Skirmish test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }
}
