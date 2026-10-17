using Godot;
using Woad.Core.Commands;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// Companion orders: on a player's turn, the Orders button (beside End turn) opens a panel with a
/// row per companion and a button for each standing order.
/// </summary>
public partial class GameScreen
{
    private const float OrderRowHeight = 46;

    private static readonly (CompanionOrder Order, string Label, string Help)[] Orders =
    [
        (CompanionOrder.Follow, "Follow", "Stay close to you and guard you: go after monsters closing in on you."),
        (CompanionOrder.Hold, "Hold", "Stay on this tile and attack whatever comes within reach."),
        (CompanionOrder.Fight, "Fight", "Go after the monsters in the room, then follow you once it's clear."),
        (CompanionOrder.StayBack, "Back", "Keep away from monsters and stay near you; strike back only when cornered."),
        (CompanionOrder.Tend, "Tend", "Go to allies Keeling Over in the room, even mid-fight, and rouse them; otherwise follow you."),
        (CompanionOrder.FindMe, "Find me", "Find a way to you wherever you are: through locked doors (a key, or picking the lock) and down pits to the floor below."),
        (CompanionOrder.Burgle, "Burgle", "Pick the room's locks (doors, chests, gates; keys first) and open its chests, then follow you."),
        (CompanionOrder.Loot, "Loot", "Gather the room's loot: bags, chests, gold, keys, pots. Then follow you. Trade with it to share."),
        (CompanionOrder.Explore, "Explore", "Search each room once for secret doors, traps and stashes, then follow you."),
    ];

    private Button _orders = null!;
    private Panel _ordersPanel = null!;
    private bool _showingOrders;

    internal Button OrdersButton => _orders;

    /// <summary>The order button for one of the active player's companions, while the panel is open.</summary>
    internal Button OrderButton(int companionIndex, CompanionOrder order) =>
        _ordersPanel.GetNode<Button>($"{companionIndex}/{order}");

    private void BuildOrders(Control right)
    {
        _orders = UiTheme.Button(new Vector2(8, 329), new Vector2(52, 22), "Orders");
        _orders.TooltipText = "Give your companions standing orders.";
        _orders.Pressed += () =>
        {
            _showingOrders = !_showingOrders;
            RefreshPanels();
        };
        right.AddChild(_orders);

        _ordersPanel = new Panel { Visible = false };
        right.AddChild(_ordersPanel);
    }

    /// <summary>Shows the Orders button while the active player has companions, and the panel while it's open.</summary>
    private void RefreshOrders()
    {
        IReadOnlyList<Hero> companions = ActiveHero is { IsCompanion: false } owner ? _session.CompanionsOf(owner) : [];
        _orders.Visible = companions.Count > 0;
        _orders.Disabled = Locked;
        _endTurn.Position = new Vector2(_orders.Visible ? 64 : 8, 329);
        _endTurn.Size = new Vector2(_orders.Visible ? 104 : 160, 22);

        _showingOrders &= _orders.Visible;
        _ordersPanel.Visible = _showingOrders && !Locked;
        _orders.Text = _showingOrders ? "Close" : "Orders";
        foreach (Node child in _ordersPanel.GetChildren())
        {
            _ordersPanel.RemoveChild(child);
            child.QueueFree();
        }

        if (!_showingOrders)
        {
            return;
        }

        float height = companions.Count * OrderRowHeight + 6;
        _ordersPanel.Position = new Vector2(4, 325 - height);
        _ordersPanel.Size = new Vector2(168, height);
        for (int i = 0; i < companions.Count; i++)
        {
            Hero companion = companions[i];
            var row = new Control { Name = i.ToString(), Position = new Vector2(5, 3 + i * OrderRowHeight), Size = new Vector2(158, OrderRowHeight) };
            _ordersPanel.AddChild(row);
            row.AddChild(UiTheme.Label(Vector2.Zero, new Vector2(158, 12), $"{companion.Name}: {Describe(companion.Order)}", color: Palette.Seat(companion.Seat)));
            for (int o = 0; o < Orders.Length; o++)
            {
                // Five on the first row, the newer four (wider) on the second.
                (CompanionOrder order, string label, string help) = Orders[o];
                Button button = o < 5
                    ? UiTheme.CompactButton(new Vector2(o * 32, 12), new Vector2(30, 15), label)
                    : UiTheme.CompactButton(new Vector2((o - 5) * 40, 29), new Vector2(38, 15), label);
                button.Name = order.ToString();
                button.TooltipText = help;
                button.ToggleMode = true;
                button.SetPressedNoSignal(companion.Order == order);
                int companionId = companion.Id;
                button.Pressed += () => ExecuteForActiveHero(ownerId => new OrderCompanion(ownerId, companionId, order));
                row.AddChild(button);
            }
        }
    }

    /// <summary>"following", "holding"...: a companion's order as the log and panels say it.</summary>
    private static string Describe(CompanionOrder order) => order switch
    {
        CompanionOrder.Hold => "holding",
        CompanionOrder.Fight => "fighting",
        CompanionOrder.StayBack => "staying back",
        CompanionOrder.Tend => "tending the fallen",
        CompanionOrder.FindMe => "finding you",
        CompanionOrder.Burgle => "burgling",
        CompanionOrder.Loot => "looting",
        CompanionOrder.Explore => "exploring",
        _ => "following",
    };
}
