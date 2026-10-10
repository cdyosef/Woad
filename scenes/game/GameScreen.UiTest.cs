using Godot;
using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// Debug mode (<c>-- --uitest</c>): plays the first two turns by pushing real mouse clicks through
/// the viewport, checking that buttons, card slots, targeting and board tiles all reach the rules.
/// Exits with code 0 on success and 1 on the first failed check.
/// </summary>
public partial class GameScreen
{
    private async void RunUiTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;

            // Belt bomb: select it, press Use, click the first highlighted target, confirm the Luck-held attack.
            CardInstance bomb = aria.Belt.Single(card => card.Definition.Id == "bomb");
            CardSlot bombSlot = _inventory.BeltSlot(aria.Belt.ToList().IndexOf(bomb));
            await Click(bombSlot);
            Check(_selectedCardId == bomb.Id, "clicking a belt card selects it");
            await Click(_inventory.ActionButton(CardAction.Use));
            Check(_targeting, "Use enters targeting");
            IReadOnlyList<GridPosition> targets = _session.GetCardTargets(bomb.Id);
            Check(targets.Count > 0, "the bomb has targets from the starting tile");
            await ClickTile(targets[0]);
            await WaitUntilIdle();
            if (_session.State.Turn!.PendingAttack is not null)
            {
                Check(_confirm.Visible, "a Luck-held attack shows the Confirm button");
                await Click(_confirm);
                await WaitUntilIdle();
            }

            Check(aria.FindCard(bomb.Id) is null, "using the bomb consumes it");
            Check(!_session.State.Turn!.HasActed, "a belt item doesn't use the action");

            // Roll button, then click the nearest blue tile.
            await Click(_roll);
            await WaitUntilIdle();
            Check(_session.State.Turn!.MovementRoll is not null, "the Roll button rolls for movement");
            GridPosition destination = _session.GetReachableTiles().OrderBy(entry => entry.Value).First().Key;
            await ClickTile(destination);
            await WaitUntilIdle();
            Check(aria.Position == destination, "clicking a highlighted tile moves the hero");

            // End turn hands over to the second seat.
            await Click(_endTurn);
            await WaitUntilIdle();
            Hero bran = ActiveHero!;
            Check(bran.Seat == 1, "End turn passes to the next player");

            // Equip the boomerang from the backpack; the buckler swaps out.
            CardInstance boomerang = bran.Backpack.Single(card => card.Definition.Id == "boomerang");
            await Click(_inventory.BackpackSlot(bran.Backpack.ToList().IndexOf(boomerang)));
            await Click(_inventory.ActionButton(CardAction.Equip));
            await WaitUntilIdle();
            Check(bran.Equipment.GetValueOrDefault(EquipSlot.OffHand) == boomerang, "Equip puts the card in its slot");
            Check(bran.Backpack.Any(card => card.Definition.Id == "buckler"), "the old card goes to the backpack");

            await Click(_endTurn);
            await WaitUntilIdle();
            Check(_session.State.Round == 2 || _session.State.Phase != RunPhase.InProgress, "the round advances after the enemy phase");

            GD.Print("UI test passed.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"UI test failed: {exception.Message}");
            GetTree().Quit(1);
        }
    }

    private static void Check(bool condition, string expectation)
    {
        if (!condition)
        {
            throw new InvalidOperationException(expectation);
        }

        GD.Print($"  ok: {expectation}");
    }

    private async Task WaitUntilIdle()
    {
        do
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        while (_busy);
    }

    private Task Click(Control control) => ClickAt(control.GetGlobalRect().GetCenter());

    private Task ClickTile(GridPosition tile) =>
        ClickAt(_board.GetGlobalRect().Position + BoardView.TileOrigin(tile) + new Vector2(BoardView.TileSize / 2f, BoardView.TileSize / 2f));

    /// <summary>Moves the mouse to a point (in 640x360 canvas coordinates) and clicks it.</summary>
    private async Task ClickAt(Vector2 position)
    {
        Viewport viewport = GetViewport();
        viewport.PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position }, inLocalCoords: true);
        foreach (bool pressed in new[] { true, false })
        {
            var click = new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = pressed,
                Position = position,
                GlobalPosition = position,
            };
            viewport.PushInput(click, inLocalCoords: true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
