using Godot;
using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Tavern;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Menu;

/// <summary>
/// After a run, a companion whose leader couldn't pay them goes up for offers: the party's other players (the raid's
/// leader first) each Match the wage, Raise over the best offer so far, or type their own, as far as their purse goes.
/// Close the bidding, and the best offer wins, gold and bond together: it's the companion's wage from then on, and they
/// follow whoever made it. With no offer, a bond in the party keeps them; without one, they mean to leave.
/// </summary>
public partial class WageAuctionScreen : MenuScreen
{
    /// <summary>The companions who went unpaid, with who owed them.</summary>
    public IReadOnlyList<(Character Companion, Character Owner)> Unpaid { get; init; } = [];

    /// <summary>Everyone in the party, in seat order.</summary>
    public IReadOnlyList<Character> Party { get; init; } = [];

    public Guid Leader { get; init; }

    public RaidWorld? World { get; init; }

    /// <summary>The bidding closed on a companion: how it came out (the game seats them with their new leader, and saves).</summary>
    public event Action<Character, AuctionResult>? Settled;

    public event Action? DoneRequested;

    private int _index;
    private readonly List<(Character Bidder, LineEdit Offer)> _offers = [];
    private Control _body = null!;

    internal Button CloseButton { get; private set; } = null!;

    internal Button DoneButton { get; private set; } = null!;

    internal string Outcome { get; private set; } = "";

    /// <summary>A bidder's offer box, and their Match and Raise buttons (for the click tests).</summary>
    internal LineEdit OfferBox(int bidder) => _offers[bidder].Offer;

    internal Button? Find(string name) => FindChild(name, recursive: true, owned: false) as Button;

    public override void _Ready()
    {
        BuildFrame("WHO'LL TAKE THEM ON?");
        _body = Add(new Control { Position = new Vector2(20, 44), Size = new Vector2(600, 260) });
        CloseButton = Add(UiTheme.Button(new Vector2(396, 316), new Vector2(112, 26), "Close bidding"));
        CloseButton.Pressed += Close;
        DoneButton = Add(UiTheme.Button(new Vector2(516, 316), new Vector2(102, 26), "Next"));
        DoneButton.Pressed += Next;
        ShowCurrent();
    }

    private static int GoldOf(LineEdit box) => int.TryParse(box.Text.Trim(), out int gold) ? Math.Max(0, gold) : 0;

    private void ShowCurrent()
    {
        foreach (Node old in _body.GetChildren())
        {
            _body.RemoveChild(old);
            old.QueueFree();
        }

        _offers.Clear();
        Outcome = "";
        (Character companion, Character owner) = Unpaid[_index];
        int wage = companion.Wage / Money.CopperPerGold;
        Label head = UiTheme.Label(new Vector2(0, 0), new Vector2(600, 28),
            $"{owner.Name} couldn't pay {companion.Name} ({Money.Short(companion.Wage)} a run). The best offer wins, gold and bond together; it's their wage from then on, paid after each run.", color: Palette.Text);
        head.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _body.AddChild(head);

        IReadOnlyList<Character> bidders = TavernServices.Bidders(owner, Party, Leader);
        if (bidders.Count == 0)
        {
            _body.AddChild(UiTheme.Label(new Vector2(0, 40), new Vector2(600, 14), "There's no other player in the party to make an offer.", color: Palette.TextDim));
        }

        for (int i = 0; i < bidders.Count; i++)
        {
            Character bidder = bidders[i];
            float y = 40 + i * 38;
            int bond = World?.Bond(companion.Id, bidder.Id) ?? 0;
            int purse = bidder.Coins / Money.CopperPerGold;
            _body.AddChild(new Panel { Position = new Vector2(0, y), Size = new Vector2(600, 34), MouseFilter = MouseFilterEnum.Ignore });
            _body.AddChild(UiTheme.Label(new Vector2(8, y + 10), new Vector2(300, 14),
                $"{bidder.Name}{(bidder.Id == Leader ? " (leader)" : "")}   purse {Money.Short(bidder.Coins)}   bond {bond}", color: Palette.Text));
            var offer = new LineEdit { Position = new Vector2(330, y + 2), Size = new Vector2(60, 30), PlaceholderText = "gold", MaxLength = 5, Name = $"Offer{i}", TooltipText = "Type an offer in gold (no more than the purse holds)." };
            _body.AddChild(offer);
            _offers.Add((bidder, offer));

            Button match = UiTheme.CompactButton(new Vector2(398, y + 7), new Vector2(60, 20), $"Match {wage}");
            match.Name = $"Match{i}";
            match.Disabled = purse < wage;
            match.TooltipText = match.Disabled ? "Their purse won't cover the wage." : $"Offer the wage: {wage} gold a run.";
            match.Pressed += () => offer.Text = wage.ToString();
            _body.AddChild(match);

            Button raise = UiTheme.CompactButton(new Vector2(462, y + 7), new Vector2(60, 20), "Raise");
            raise.Name = $"Raise{i}";
            raise.TooltipText = "A gold over the best offer so far (or over the wage).";
            raise.Pressed += () =>
            {
                int best = Math.Max(wage, _offers.Where(entry => entry.Offer != offer).Select(entry => GoldOf(entry.Offer)).DefaultIfEmpty(0).Max());
                offer.Text = Math.Min(purse, Math.Max(best + 1, GoldOf(offer) + 1)).ToString();
            };
            _body.AddChild(raise);

            Button pass = UiTheme.CompactButton(new Vector2(526, y + 7), new Vector2(60, 20), "Pass");
            pass.Name = $"Pass{i}";
            pass.Pressed += () => offer.Text = "";
            _body.AddChild(pass);
        }

        CloseButton.Disabled = false;
        DoneButton.Disabled = true;
        DoneButton.Text = _index == Unpaid.Count - 1 ? "Done" : "Next";
    }

    private void Close()
    {
        (Character companion, Character owner) = Unpaid[_index];
        AuctionResult result = TavernServices.CloseAuction(companion, owner,
            [.. _offers.Select(entry => (entry.Bidder, GoldOf(entry.Offer) * Money.CopperPerGold))], Party, World);
        Outcome = result.Message;
        Label told = UiTheme.Label(new Vector2(0, 232), new Vector2(600, 28), result.Message,
            color: result.Outcome == AuctionOutcome.Leaving ? Palette.Danger : Palette.Heal);
        told.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _body.AddChild(told);
        foreach (Node child in _body.GetChildren())
        {
            if (child is Button button)
            {
                button.Disabled = true;
            }
            else if (child is LineEdit box)
            {
                box.Editable = false;
            }
        }

        CloseButton.Disabled = true;
        DoneButton.Disabled = false;
        Settled?.Invoke(companion, result);
    }

    private void Next()
    {
        if (_index < Unpaid.Count - 1)
        {
            _index++;
            ShowCurrent();
            return;
        }

        DoneRequested?.Invoke();
    }
}
