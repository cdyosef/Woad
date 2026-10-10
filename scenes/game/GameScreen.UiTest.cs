using Godot;
using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Scenes.Debug;
using Woad.Scenes.Ui;
using static Woad.Scenes.Debug.UiDriver;

namespace Woad.Scenes.Game;

/// <summary>
/// Debug mode (<c>-- --uitest</c>): plays the first two turns of the debug party by pushing real
/// mouse clicks through the viewport, checking that buttons, card slots, targeting and board
/// tiles all reach the rules. Exits with code 0 on success and 1 on the first failed check.
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
            await Click(this, _inventory.BeltSlot(aria.Belt.ToList().IndexOf(bomb)));
            Check(_selectedCardId == bomb.Id, "clicking a belt card selects it");
            await Click(this, _inventory.ActionButton(CardAction.Use));
            Check(_targeting, "Use enters targeting");
            IReadOnlyList<GridPosition> targets = _session.GetCardTargets(bomb.Id);
            Check(targets.Count > 0, "the bomb has targets from the starting tile");
            await ClickTile(targets[0]);
            await WaitUntilIdle();
            if (_session.State.Turn!.PendingAttack is not null)
            {
                Check(_confirm.Visible, "a Luck-held attack shows the Confirm button");
                await Click(this, _confirm);
                await WaitUntilIdle();
            }

            Check(aria.FindCard(bomb.Id) is null, "using the bomb consumes it");
            Check(!_session.State.Turn!.HasActed, "a belt item doesn't use the action");

            // Roll button, then click the nearest blue tile.
            await Click(this, _roll);
            await WaitUntilIdle();
            Check(_session.State.Turn!.MovementRoll is not null, "the Roll button rolls for movement");
            GridPosition destination = _session.GetReachableTiles().OrderBy(entry => entry.Value).First().Key;
            await ClickTile(destination);
            await WaitUntilIdle();
            Check(aria.Position == destination, "clicking a highlighted tile moves the hero");

            // End turn hands over to the second seat.
            await Click(this, _endTurn);
            await WaitUntilIdle();
            Hero bran = ActiveHero!;
            Check(bran.Seat == 1, "End turn passes to the next player");

            // Equip the boomerang from the backpack; the buckler swaps out.
            CardInstance boomerang = bran.Backpack.Single(card => card.Definition.Id == "boomerang");
            await Click(this, _inventory.BackpackSlot(bran.Backpack.ToList().IndexOf(boomerang)));
            await Click(this, _inventory.ActionButton(CardAction.Equip));
            await WaitUntilIdle();
            Check(bran.Equipment.GetValueOrDefault(EquipSlot.OffHand) == boomerang, "Equip puts the card in its slot");
            Check(bran.Backpack.Any(card => card.Definition.Id == "buckler"), "the old card goes to the backpack");

            await Click(this, _endTurn);
            await WaitUntilIdle();
            Check(_session.State.Round == 2 || _session.State.Phase != RunPhase.InProgress, "the round advances after the enemy phase");

            GD.Print("UI test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"UI test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    /// <summary>
    /// Debug mode (<c>-- --floortest</c>, with <see cref="DemoRun.DoorsAndStairs"/>): clicks through a
    /// door and down the stairs, checking the view and minimap follow the hero.
    /// </summary>
    private async void RunFloorTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            Check(_shownRoomId == "a", "the run starts in the first room");

            await Click(this, _roll);
            await WaitUntilIdle();
            await ClickTile(aria.Position);
            await WaitUntilIdle();
            Check(aria.RoomId == "b" && aria.Position == new GridPosition(0, 4), "clicking your own doorway tile goes through the door");
            Check(_shownRoomId == "b", "the view follows the hero into the next room");
            Check(_session.State.Rooms["b"].Explored, "the new room is explored and shows on the minimap");

            await ClickTile(new GridPosition(1, 4));
            await WaitUntilIdle();
            Check(aria.Position == new GridPosition(1, 4), "the hero walks onto the stairs");
            await ClickTile(aria.Position);
            await WaitUntilIdle();
            Check(aria.RoomId == "c" && aria.DeepestFloor == 2, "clicking the stairs you stand on goes down a floor");
            Check(_shownRoomId == "c", "the view follows the hero to the next floor");

            await Click(this, _endTurn);
            await WaitUntilIdle();
            Check(_shownRoomId == "a" && ActiveHero!.Seat == 1, "the next hero's turn shows their room");

            GD.Print("Floor test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Floor test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    internal Task WaitUntilIdle() => WaitFor(this, () => !_busy, "event playback to finish");

    private Task ClickTile(GridPosition tile) =>
        ClickAt(this, _board.GetGlobalRect().Position + BoardView.TileOrigin(tile) + new Vector2(BoardView.TileSize / 2f, BoardView.TileSize / 2f));
}
