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
            if (_session.State.Turn!.PendingRoll is not null)
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

            // Search spends the Od Hand die, not the action.
            await Click(this, _search);
            await WaitUntilIdle();
            if (_session.State.Turn!.PendingRoll is not null)
            {
                Check(_confirm.Visible, "with Luck left, a search waits to be confirmed like an attack");
                await Click(this, _confirm);
                await WaitUntilIdle();
            }

            Check(_session.State.Turn!.OdHandDiceUsed == 1 && !_session.State.Turn.HasActed, "Search rolls Od Hand without using the action");

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

            CardInstance buckler = bran.Backpack.Single(card => card.Definition.Id == "buckler");
            await Click(this, _inventory.BackpackSlot(bran.Backpack.ToList().IndexOf(buckler)));
            Button equip = _inventory.ActionButton(CardAction.Equip);
            Check(equip.Disabled && equip.TooltipText.Contains("in a fight"), "a greyed-out Equip says why (gear in a fight takes the action)");
            await Click(this, equip);
            Check(_messages[^1].Contains("in a fight"), "clicking a greyed-out button logs the reason");

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

    /// <summary>
    /// Debug mode (<c>-- --hazardtest</c>, with <see cref="DemoRun.Hazards"/>): shoves a slimeling into a
    /// pit that leads to the room below, then leaps over the pit, landing beyond it, short, or below.
    /// </summary>
    private async void RunHazardTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            Room ledge = _session.State.Rooms["ledge"];
            Check(ledge.BelowRoomId == "below", "the pits lead to the room underneath");

            // A slimeling has no defense, so any shove pushes it into the pit; its heart goes in the fall.
            await Click(this, _shove);
            Check(_shoving, "Shove waits for a monster to be clicked");
            await ClickTile(new GridPosition(4, 4));
            await ConfirmIfHeld();
            Check(ledge.Monsters.Count == 0 && aria.Kills == 1, "the shoved slimeling falls into the pit and dies of the fall");

            // Leap from (4,5) over the pit to (4,2): a 6 lands there, 4-5 come down in the pit and drop
            // the hero below, 2-3 land just short at the edge, and a 1 goes nowhere.
            await Click(this, _leap);
            Check(_leaping && _session.GetLeapTargets().Contains(new GridPosition(4, 2)), "Leap offers the tile beyond the pit");
            await ClickTile(new GridPosition(4, 2));
            await WaitUntilIdle();
            Check(_session.State.Turn!.MoveDiceUsed == 1 || aria.RoomId == "below", "the leap spends the Wend die");
            Check(aria.RoomId == "below" || aria.Position == new GridPosition(4, 2) || aria.Position == new GridPosition(4, 4) || aria.Position == new GridPosition(4, 5),
                "the leap lands on target, short of it, or drops the hero through the pit");
            Check(_shownRoomId == aria.RoomId, "the view follows the hero");

            GD.Print("Hazard test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Hazard test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    /// <summary>
    /// Debug mode (<c>-- --keeltest</c>, with <see cref="DemoRun.Keeling"/>): Bran walks onto a hidden
    /// spike trap with his one heart and keels over beside Aria; Aria rouses him by clicking him, and
    /// on his turn the End turn button rolls his Keel Die.
    /// </summary>
    private async void RunKeelTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            await Click(this, _endTurn);
            await WaitUntilIdle();

            Hero bran = ActiveHero!;
            Check(bran.Name == "Bran", "Bran's turn comes next");
            await Click(this, _roll);
            await WaitUntilIdle();
            await ClickTile(new GridPosition(5, 4));
            await WaitUntilIdle();
            Check(bran.IsKeelingOver && bran.Position == new GridPosition(5, 4), "Bran keels over on the spikes");
            Check(_tokens.ContainsKey(bran.Id) && _figures[bran.Id].Keeling, "Bran's figure stays on the board, lying down");
            Check(ActiveHero == aria, "the turn moves on to Aria");

            // Clicking a fallen ally beside you rouses them with your Od Hand dice.
            Check(_session.GetRousableAllies().Contains(bran), "Aria can rouse Bran");
            await ClickTile(bran.Position);
            await ConfirmIfHeld();
            Check(_session.State.Turn!.OdHandDiceUsed == 1 && !_session.State.Turn.HasActed, "rousing uses Aria's Od Hand die, not her action");
            Check(_messages.Any(message => message.Contains("tends Bran")), "the log tells how the rousing went");

            await Click(this, _endTurn);
            await WaitUntilIdle();
            Check(ActiveHero == bran && _session.State.Turn!.KeelingOver, "Bran's turn comes while he's Keeling Over");
            Check(_endTurn.Text == "Roll Keel Die" && _roll.Disabled, "Keeling Over, the only button is Roll Keel Die");
            await Click(this, _endTurn);
            await WaitUntilIdle();
            Check(_messages.Any(message => message.Contains("rolls the Keel Die")), "the Keel Die is rolled");
            Check(ActiveHero == aria || bran.IsActive, "the turn moves on after the Keel roll");

            // Left alone, Bran rolls until three marks stand him up or kill him.
            for (int turns = 0; bran.IsKeelingOver && turns < 200; turns++)
            {
                await Click(this, _endTurn);
                await WaitUntilIdle();
            }

            Room crypt = _session.State.Rooms["crypt"];
            Check(bran.Status == HeroStatus.Dead
                    ? crypt.Tombstones.SingleOrDefault() is { } stone && stone.Inscription.Contains("Bran") && !_tokens.ContainsKey(bran.Id)
                    : bran.IsActive && bran.Hearts == 1 && !_figures[bran.Id].Keeling,
                "Bran either stands up with a heart or dies, leaving a tombstone");

            GD.Print($"Keel test passed ({(bran.IsActive ? "Bran stood up" : "Bran died")}).");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Keel test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    /// <summary>Confirms a roll that Luck is holding open.</summary>
    private async Task ConfirmIfHeld()
    {
        await WaitUntilIdle();
        if (_session.State.Turn?.PendingRoll is not null)
        {
            await Click(this, _confirm);
            await WaitUntilIdle();
        }
    }

    /// <summary>
    /// Waits for event playback to finish. The tests roll real dice, so any Power Die a lucky 6 earns
    /// is added by clicking Add Power Die, keeping the scripted clicks on track.
    /// </summary>
    internal async Task WaitUntilIdle()
    {
        await WaitFor(this, () => !_busy, "event playback to finish");
        while (_session.State.Turn?.UnsettledPowerDice > 0)
        {
            Check(_addPower.Visible, "a 6 offers to add or bank a Power Die");
            await Click(this, _addPower);
            await WaitFor(this, () => !_busy, "the Power Die to roll");
        }
    }

    private Task ClickTile(GridPosition tile) =>
        ClickAt(this, _board.GetGlobalRect().Position + BoardView.TileOrigin(tile) + new Vector2(BoardView.TileSize / 2f, BoardView.TileSize / 2f));
}
