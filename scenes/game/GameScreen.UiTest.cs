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

    /// <summary>
    /// Debug mode (<c>-- --firetest</c>, with <see cref="DemoRun.Fire"/>): Aria waits among Cinder Imps
    /// until one sets her alight, puts it out with Drop &amp; roll (her whole turn, leaving her prone),
    /// then rolls to move and gets back up with the Get up button.
    /// </summary>
    private async void RunFireTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            Check(!_dropRoll.Visible, "Drop & roll only shows while you're on fire");
            for (int rounds = 0; aria.FireDice == 0 && rounds < 60; rounds++)
            {
                await Click(this, _endTurn);
                await WaitUntilIdle();
            }

            Check(aria.FireDice > 0 && _figures[aria.Id].FireDice == aria.FireDice, "an imp's fire attack sets Aria alight, and her figure shows it");
            Check(_dropRoll.Visible && !_dropRoll.Disabled, "on fire, Drop & roll is offered");
            await Click(this, _dropRoll);
            await WaitUntilIdle();
            // (The imps may set her alight again in their phase, so the log tells whether it went out.)
            Check(aria.Prone, "Drop & roll leaves Aria prone");
            Check(_figures[aria.Id].Prone, "her figure lies down");
            Check(ActiveHero == aria && _session.State.Round > 1, "it takes her whole turn");

            await Click(this, _roll);
            await WaitUntilIdle();
            Check(_prone.Text == "Get up" && !_prone.Disabled, "prone, the button offers to get up");
            await Click(this, _prone);
            await WaitUntilIdle();
            Check(!aria.Prone && !_figures[aria.Id].Prone, "Get up stands Aria back up");

            GD.Print("Fire test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Fire test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    /// <summary>
    /// Debug mode (<c>-- --cameratest</c>, with <see cref="DemoRun.BigHall"/>): in a hall bigger than the
    /// view, the camera starts on Aria, a right-drag pans no further than keeps her in view, a plain
    /// right-click still cancels, and on Bran's turn the view scrolls across the hall to him.
    /// </summary>
    private async void RunCameraTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            await WaitFor(this, () => _board.IsInView(aria.Position), "the view to settle on Aria");
            Vector2 before = _board.Position;

            Vector2 middle = _board.GetParent<Control>().GetGlobalRect().GetCenter();
            await RightDrag(this, middle, middle - new Vector2(400, 400));
            Check(_board.Position != before, "dragging with the right button pans the view");
            Check(_board.IsInView(aria.Position), "panning stops where Aria would leave the view");

            await Click(this, _search);
            await ConfirmIfHeld();
            Check(_board.IsInView(aria.Position), "after she acts, the view is back on Aria");

            await Click(this, _endTurn);
            await WaitUntilIdle();
            Hero bran = ActiveHero!;
            Check(bran.Name == "Bran", "Bran's turn comes next");
            await WaitFor(this, () => _board.IsInView(bran.Position), "the view to scroll across the hall to Bran", maxFrames: 600);
            Check(!_board.IsInView(aria.Position), "Aria, in the far corner, is out of view now");

            GD.Print("Camera test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Camera test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    /// <summary>
    /// Debug mode (<c>-- --bombtest</c>, with <see cref="DemoRun.Bombs"/>): Aria throws her bomb into a
    /// huddle of slimes. It lies there lit through her turn and the next, then goes off at the end of
    /// her next turn: the blast rolls, the slimes take it, and the bomb is gone.
    /// </summary>
    private async void RunBombTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            Room armory = _session.State.Rooms["armory"];
            CardInstance bomb = aria.AllCards.Single(card => card.Definition.Id == "bomb");
            await Click(this, aria.Belt.Contains(bomb) ? _inventory.BeltSlot(aria.Belt.ToList().IndexOf(bomb)) : _inventory.BackpackSlot(aria.Backpack.ToList().IndexOf(bomb)));
            await Click(this, _inventory.ActionButton(CardAction.Use));
            Check(_targeting && _session.GetCardTargets(bomb.Id).Contains(new GridPosition(4, 3)), "the bomb can be thrown 3 tiles");
            await ClickTile(new GridPosition(4, 3));
            await WaitUntilIdle();
            Check(armory.BombAt(new GridPosition(4, 3)) is { Lit: true, FuseTurns: 2 }, "the bomb lands lit, burning through this turn and the next");

            await Click(this, _endTurn);
            await WaitUntilIdle();
            Check(armory.Bombs.Count == 1, "it hasn't gone off after one turn");

            await Click(this, _endTurn);
            await WaitUntilIdle();
            Check(armory.Bombs.Count == 0, "it goes off at the end of her next turn");

            GD.Print("Bomb test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Bomb test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    /// <summary>
    /// Debug mode (<c>-- --rumbletest</c>, with <see cref="DemoRun.Duel"/>): clicking the slime beside Aria
    /// doesn't attack it; the space bar shows her reach, a click off target puts Rumble away, and the
    /// Rumble button then a click on the slime attacks it.
    /// </summary>
    private async void RunRumbleTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            Monster slime = _session.State.Rooms["yard"].Monsters.Single();

            await PressKey(Key.W);
            await WaitUntilIdle();
            await ConfirmIfHeld();
            Check(_session.State.Turn!.MovementPoints > 0, "W rolls the Wend dice");

            await ClickTile(slime.Position);
            await WaitUntilIdle();
            Check(_session.State.Turn!.AttackDiceUsed == 0, "clicking a monster no longer attacks it");

            GetViewport().PushInput(new InputEventKey { Pressed = true, Keycode = Key.Space }, inLocalCoords: true);
            await NextFrame(this);
            Check(_rumbling && RumbleTargets(ShownRoom).Contains(slime.Position), "Space readies Rumble, with the slime in reach");
            await ClickTile(new GridPosition(0, 0));
            await WaitUntilIdle();
            Check(!_rumbling, "a click off target puts Rumble away");

            await DoubleClickAt(this, _board.GetGlobalRect().Position + BoardView.TileOrigin(slime.Position) + new Vector2(BoardView.TileSize / 2f, BoardView.TileSize / 2f));
            await WaitUntilIdle();
            Check(_rumbling && _session.State.Turn!.AttackDiceUsed == 0, "double-clicking a monster in reach aims Rumble at it, without attacking yet");
            await ClickTile(new GridPosition(0, 0));
            await WaitUntilIdle();

            await Click(this, _rumble);
            await ClickTile(slime.Position);
            await ConfirmIfHeld();
            Check(_session.State.Turn!.AttackDiceUsed > 0 || _session.State.Turn.HeroId != aria.Id, "Rumble, then a click on the slime, attacks it");

            GD.Print("Rumble test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Rumble test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    /// <summary>
    /// Debug mode (<c>-- --clicktest</c>, with <see cref="DemoRun.Stalk"/>): double-clicking the slime
    /// across the yard rolls Aria's Wend dice; clicking it again walks her up to it with Rumble aimed,
    /// and one more click attacks.
    /// </summary>
    private async void RunClickTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            Monster slime = _session.State.Rooms["yard"].Monsters.Single();
            Vector2 at = _board.GetGlobalRect().Position + BoardView.TileOrigin(slime.Position) + new Vector2(BoardView.TileSize / 2f, BoardView.TileSize / 2f);

            await DoubleClickAt(this, at);
            await WaitUntilIdle();
            await ConfirmIfHeld();
            Check(_session.State.Turn!.MovementPoints > 0, "double-clicking a far-off monster rolls the Wend dice");
            Check(aria.Position == new GridPosition(1, 4), "the roll alone doesn't move her");

            await ClickTile(slime.Position);
            await WaitUntilIdle();
            Check(Woad.Core.Rules.Targeting.CanHeroAttack(ShownRoom.Layout, aria.AttackRange, aria.Position, slime.Position), "clicking it again walks her up to it");
            Check(_rumbling, "and Rumble is aimed at it");
            await PressKey(Key.R);
            Check(!_rumbling, "R puts Rumble away");
            await PressKey(Key.R);
            Check(_rumbling, "and R aims it again");

            await ClickTile(slime.Position);
            await ConfirmIfHeld();
            Check(_session.State.Turn!.AttackDiceUsed > 0 || _session.State.Turn.HeroId != aria.Id, "one more click attacks");


            GD.Print("Click test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Click test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    /// <summary>
    /// Debug mode (<c>-- --loottest</c>, with <see cref="DemoRun.Treasury"/>): Aria drops two of her three
    /// lockpicks (asked how many) and her emerald, which lie in a bag; she rummages through it, takes the
    /// emerald back and then the rest; then she opens the chest beside her and takes what's inside.
    /// </summary>
    private async void RunLootTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            CardInstance picks = aria.AllCards.Single(card => card.Definition.Id == "lockpicks");
            Check(picks.Count == 3 && aria.Equipment[EquipSlot.OffHand] == picks, "three lockpicks make one stack, in the off hand");

            await Click(this, _heroPanel.EquipmentSlot(EquipSlot.OffHand));
            await Click(this, _inventory.ActionButton(CardAction.Drop));
            Check(_countPrompt.Visible, "dropping a stack asks how many");
            await Click(this, _countPrompt.PlusButton);
            await Click(this, _countPrompt.OkButton);
            await WaitUntilIdle();
            Check(picks.Count == 1 && ShownRoom.LootAt(aria.Position).Sum(card => card.Count) == 2, "two of the three are dropped");

            CardInstance emerald = aria.AllCards.Single(card => card.Definition.Id == "emerald");
            await Click(this, _inventory.BackpackSlot(aria.Backpack.ToList().IndexOf(emerald)));
            await Click(this, _inventory.ActionButton(CardAction.Drop));
            await WaitUntilIdle();

            await ClickTile(aria.Position);
            Check(_rummage.Visible && _rummage.Tile == aria.Position, "clicking a bag of loot opens it");
            await Click(this, _rummage.TakeButton(emerald.Id)!);
            await WaitUntilIdle();
            Check(aria.AllCards.Contains(emerald) && _rummage.Visible, "Take takes just that card, and the bag stays open");
            await Click(this, _rummage.TakeAllButton);
            await WaitUntilIdle();
            Check(picks.Count == 3 && !_rummage.Visible, "Take all empties the bag (the picks rejoin the stack) and closes it");

            var chest = new GridPosition(4, 3);
            await ClickTile(chest);
            await WaitUntilIdle();
            Check(ShownRoom.IsChestOpened(chest), "clicking the chest opens it");
            if (ShownRoom.LootAt(chest).Count == 0)
            {
                Check(!_rummage.Visible, "coins go straight to the purse, leaving nothing to rummage through");
            }
            else
            {
                Check(_rummage.Visible && _rummage.Tile == chest, "and shows what's inside");
                await Click(this, _rummage.TakeAllButton);
                await WaitUntilIdle();
                Check(ShownRoom.LootAt(chest).Count == 0 || aria.Backpack.Count == 6, "Take all empties the chest (or fills the backpack)");
            }

            GD.Print("Loot test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Loot test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    /// <summary>
    /// Debug mode (<c>-- --tradetest</c>, with <see cref="DemoRun.Camp"/>): Aria clicks Pip beside her to
    /// trade, gives it one of her two potions (asked how many) and has it equip its Knight's Sword; then
    /// orders it to Find me from the Orders panel.
    /// </summary>
    private async void RunTradeTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            Hero pip = _session.State.Heroes.Single(hero => hero.Name == "Pip");

            await ClickTile(pip.Position);
            Check(_trade.Visible && _trade.PartnerId == pip.Id, "clicking an ally beside you opens trading");

            CardInstance potions = aria.AllCards.Single(card => card.Definition.Id == "health_potion");
            await Click(this, _trade.Button($"Give{potions.Id}")!);
            Check(_countPrompt.Visible, "giving part of a stack asks how many");
            await Click(this, _countPrompt.OkButton);
            await WaitUntilIdle();
            Check(potions.Count == 1 && pip.AllCards.Any(card => card.Definition.Id == "health_potion"), "one potion goes to Pip");

            CardInstance sword = pip.AllCards.Single(card => card.Definition.Id == "knights_sword");
            await Click(this, _trade.Button($"Equip{sword.Id}")!);
            await WaitUntilIdle();
            Check(pip.MainWeapon == sword, "Aria has her companion equip its Knight's Sword");

            await Click(this, _trade.Button("Close") ?? _trade.GetChildren().OfType<Button>().First(button => button.Text == "Close"));
            Check(!_trade.Visible, "Close puts trading away");

            await Click(this, _orders);
            await Click(this, OrderButton(0, CompanionOrder.FindMe));
            await WaitUntilIdle();
            Check(pip.Order == CompanionOrder.FindMe, "the Find me order button gives the order");

            GD.Print("Trade test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Trade test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    private async Task PressKey(Key key)
    {
        GetViewport().PushInput(new InputEventKey { Pressed = true, Keycode = key }, inLocalCoords: true);
        await NextFrame(this);
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
