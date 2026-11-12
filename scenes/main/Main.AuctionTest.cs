using Godot;
using Woad.Core.Characters;
using Woad.Core.Model;
using Woad.Core.Tavern;
using Woad.Scenes.Menu;
using Woad.Scenes.Ui;
using static Woad.Scenes.Debug.UiDriver;

namespace Woad.Scenes;

/// <summary>
/// Debug mode (<c>-- --auctiontest</c>): Clod Hopper went unpaid, and goes up for offers. Bryn matches his wage, Dara
/// raises a gold over; the bidding closed, Dara has him. Then a second unpaid hand nobody offers for means to leave.
/// </summary>
public partial class Main
{
    private async void RunAuctionTest()
    {
        try
        {
            Character Player(string name, int gold)
            {
                Character made = Character.Create(name, "moss", StatAllocation.None, _content, DateTimeOffset.UtcNow);
                made.Earn(Money.Gold(gold));
                return made;
            }

            Character ash = Player("Ash", 0), bryn = Player("Bryn", 30), dara = Player("Dara", 30);
            (_, Character? clod) = TavernServices.Hire(ash, TavernServices.Clod, _content, DateTimeOffset.UtcNow);
            Character pip = Character.Create("Pip", "frost", StatAllocation.None, _content, DateTimeOffset.UtcNow, isCompanion: true);
            pip.SetWage(Money.Gold(5));
            Character[] party = [ash, clod!, pip, bryn, dara];

            var auction = new WageAuctionScreen { Unpaid = [(clod!, ash), (pip, ash)], Party = party, Leader = ash.Id, World = null };
            (Character Companion, AuctionResult Result)? settled = null;
            bool done = false;
            auction.Settled += (companion, result) => settled = (companion, result);
            auction.DoneRequested += () => done = true;
            Show(auction);
            await Screen<WageAuctionScreen>();

            Check(auction.DoneButton.Disabled && !auction.CloseButton.Disabled, "the bidding is open for Clod Hopper");
            await Click(this, auction.Find("Match0")!);
            Check(auction.OfferBox(0).Text == "10", "Bryn matches his wage: 10 gold");
            await Click(this, auction.Find("Raise1")!);
            Check(auction.OfferBox(1).Text == "11", "Dara raises a gold over the best offer");
            await Click(this, auction.Find("Raise0")!);
            await Click(this, auction.Find("Pass0")!);
            Check(auction.OfferBox(0).Text == "", "Bryn raises, thinks better of it, and passes");
            if (System.Environment.GetEnvironmentVariable("WOAD_SHOTS") is { } shots)
            {
                await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
                GetViewport().GetTexture().GetImage().SavePng($"{shots}/auction.png");
            }

            await Click(this, auction.CloseButton);
            Check(settled is { Result.Outcome: AuctionOutcome.Hired } hired && hired.Result.NewLeader == dara && clod!.Wage == Money.Gold(11) && !clod.Leaving,
                $"the bidding closed, Dara takes him on at 11 gold a run ({auction.Outcome})");

            await Click(this, auction.DoneButton);
            Check(!done && !auction.CloseButton.Disabled, "next up: Pip");
            await Click(this, auction.CloseButton);
            Check(settled is { Result.Outcome: AuctionOutcome.Leaving } && pip.Leaving, $"nobody offers for Pip: he means to leave ({auction.Outcome})");
            await Click(this, auction.DoneButton);
            Check(done, "Done moves on");

            GD.Print("Auction test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Auction test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }
}
