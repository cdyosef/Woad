using Godot;
using Woad.Core.Characters;
using Woad.Core.Tavern;
using Woad.Core.Woarld;
using Woad.Scenes.Ui;
using Woad.Scenes.Woarld;
using static Woad.Scenes.Debug.UiDriver;

namespace Woad.Scenes;

/// <summary>
/// Debug mode (<c>-- --wanderertest</c>): on a fresh Woarld (kept in memory, nothing saved), Wren goes to
/// the day's first wandering merchant, opens their wares with the Trade button and buys one; then to the
/// Blacksmith, who mends her acid-worn chain mail and sells her an Autosmith Hammer.
/// </summary>
public partial class Main
{
    private async void RunWandererTest()
    {
        try
        {
            RaidWorld world = RaidWorld.Begin(WoarldMap.LoadDefault());
            Character wren = Character.Create("Wren", "frost", StatAllocation.None, _content, DateTimeOffset.UtcNow);
            wren.EarnGold(500);
            var screen = new WoarldMapScreen { Journey = world.Journey, Party = [wren], World = world, Content = _content };
            int trades = 0;
            screen.Traded += _ => trades++;
            Show(screen);
            await NextFrame(this);

            WanderingMerchant wanderer = world.Wanderers(_content)[0];
            Check(wanderer.Stock.Count >= RaidWorld.WandererStock, $"{wanderer.Name} is out on the roads with wares");
            world.Journey.Resume(world.Journey.Place with { Leader = wanderer.Position, Trail = [] });
            screen.Refresh();
            Check(screen.EnterButton.Visible && screen.EnterButton.Text == $"Trade with {wanderer.Name}", "standing with them, the button offers to trade");

            await Click(this, screen.EnterButton);
            Check(screen.WandererPanel.Visible, "Trade opens their wares");
            int wares = wanderer.Stock.Count;
            Button buy = screen.WandererPanel.GetChildren().OfType<Button>().First(button => button.Text == "Buy" && !button.Disabled);
            await Click(this, buy);
            Check(wanderer.Stock.Count == wares - 1 && wren.Gold < 500 && trades == 1, "Buy buys one with Wren's gold");

            wren.GiveCard("chain_mail~2", _content);
            WoarldPlace smithy = world.Journey.Map.Places.Single(place => place.Kind == PlaceKind.Smithy);
            world.Journey.Resume(world.Journey.Place with { Leader = smithy.Position, Trail = [] });
            screen.Refresh();
            Check(screen.EnterButton.Visible && screen.EnterButton.Text == $"Enter {smithy.Name}", "at the shack, the button offers to go in");
            await Click(this, screen.EnterButton);
            Check(screen.SmithyPanel.Visible, "Enter opens the Blacksmith");
            int gold = wren.Gold;
            await Click(this, screen.SmithyPanel.GetChildren().OfType<Button>().First(button => button.Text == "Mend" && !button.Disabled));
            Check(!wren.WornGear.Any() && wren.Gold == gold - 4, "Mend mends the chain mail for 4 gold");
            await Click(this, screen.SmithyPanel.GetChildren().OfType<Button>().First(button => button.Text == "Buy" && !button.Disabled));
            Check(trades == 3 && wren.Gold < gold - 4, "Buy buys from the forge");

            GD.Print("Wanderer test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Wanderer test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }
}
