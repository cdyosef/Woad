using Godot;
using Woad.Core.Characters;
using Woad.Core.Model;
using Woad.Core.Saves;
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
            wren.Earn(Money.Gold(500));
            var chest = new PartyChest();
            chest.Earn(Money.Gold(100));
            var screen = new WoarldMapScreen { Journey = world.Journey, Party = [wren], World = world, Content = _content, Chest = chest };
            int trades = 0;
            screen.CharactersChanged += _ => trades++;
            Show(screen);
            await NextFrame(this);

            async Task ShopClick(string name)
            {
                screen.PartyMenu.Find(name);
                await NextFrame(this);
                await Click(this, screen.PartyMenu.Find(name)!);
            }

            WanderingMerchant wanderer = world.Wanderers(_content)[0];
            Check(wanderer.Stock.Count >= RaidWorld.WandererStock, $"{wanderer.Name} is out on the roads with wares");
            world.Journey.Resume(world.Journey.Place with { Leader = wanderer.Position, Trail = [] });
            screen.Refresh();
            Check(screen.EnterButton.Visible && screen.EnterButton.Text == $"Trade with {wanderer.Name}", "standing with them, the button offers to trade");

            await Click(this, screen.EnterButton);
            Check(screen.PartyMenu.Shop is not null && screen.PartyMenu.Find($"Ware_{wanderer.Stock[0]}") is not null, "Trade opens their wares beside the party's things");
            int wares = wanderer.Stock.Count;
            await ShopClick($"Ware_{wanderer.Stock[0]}");
            await ShopClick("BuyForWren");
            Check(wanderer.Stock.Count == wares - 1 && wren.Coins < Money.Gold(500) && trades == 1, "picking a ware and then Wren buys it with Wren's gold");
            await ShopClick($"Ware_{wanderer.Stock[0]}");
            await ShopClick("BuyForChest");
            Check(wanderer.Stock.Count == wares - 2 && chest.Cards.Count == 1 && chest.Coins < Money.Gold(100), "and the Party Chest can buy one too, into the chest");
            await ShopClick("Card0_club");
            int purse = wren.Coins;
            await ShopClick("Sell");
            Check(wren.Coins == purse + Money.Gold(1) && wanderer.Stock.Contains("club"), "Wren's club can be sold from the shop page");
            await Click(this, screen.PartyMenu.Find("Close")!);

            wren.GiveCard("chain_mail~2", _content);
            WoarldPlace smithy = world.Journey.Map.Places.Single(place => place.Kind == PlaceKind.Smithy);
            world.Journey.Resume(world.Journey.Place with { Leader = smithy.Position, Trail = [] });
            screen.Refresh();
            Check(screen.EnterButton.Visible && screen.EnterButton.Text == $"Enter {smithy.Name}", "at the shack, the button offers to go in");
            await Click(this, screen.EnterButton);
            Check(screen.PartyMenu.Shop?.Name == smithy.Name, "Enter opens the Blacksmith's wares beside the party's things");
            int gold = wren.Coins;
            await ShopClick("Card0_chain_mail");
            await ShopClick("Mend");
            Check(!wren.WornGear.Any() && wren.Coins == gold - Money.Gold(4), "Mend mends the chain mail for 4 gold");
            await ShopClick($"Ware_{smithy.Stock[0]}");
            await ShopClick("BuyForWren");
            Check(wren.Coins < gold - Money.Gold(4), "Buy buys from the forge");

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
