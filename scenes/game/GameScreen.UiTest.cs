using Godot;
using Woad.Core.Commands;
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

            // The turn order: Aria first, then Bran, then the monsters. Bran's card shows his sheet; hers brings hers back.
            Hero bran0 = _session.State.Heroes.Single(hero => hero.Name == "Bran");
            Check(SheetHero == aria, "the sheet is the active hero's");
            await ClickAt(this, _turnOrder.GetGlobalRect().Position + new Vector2(30 + 4 + 11, 18));
            Check(_viewedHeroId == bran0.Id && SheetHero == bran0, "clicking the next card in the turn order puts up Bran's sheet");
            await ClickAt(this, _turnOrder.GetGlobalRect().Position + new Vector2(15, 16));
            Check(_viewedHeroId is null && SheetHero == aria, "clicking the active hero's card comes back to her");

            // Clicking a monster puts up its sheet on the right; the x puts it away.
            Monster sighted = ShownRoom.Monsters.First();
            await ClickTile(sighted.Position);
            await WaitUntilIdle();
            Check(_targetId == sighted.Id && _targetSheet.Visible, "clicking a monster puts up its sheet");
            if (System.Environment.GetEnvironmentVariable("WOAD_SHOTS") is { } targetShots)
            {
                await Pause(0.3);
                GetViewport().GetTexture().GetImage().SavePng($"{targetShots}/target_sheet.png");
            }

            CancelSelection();
            _targetId = null;
            RefreshPanels();

            // Belt bomb: select it, press Use, click the first highlighted target, confirm the Luck-held attack.
            CardInstance bomb = aria.Belt.Single(card => card.Definition.Id == "bomb");
            await Click(this, _heroPanel.BeltSlot(aria.Belt.ToList().IndexOf(bomb)));
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
            await UseAbility(_roll);
            await WaitUntilIdle();
            Check(_session.State.Turn!.MovementRoll is not null, "the Roll button rolls for movement");
            GridPosition destination = _session.GetReachableTiles().OrderBy(entry => entry.Value).First().Key;
            await ClickTile(destination);
            await WaitUntilIdle();
            Check(aria.Position == destination, "clicking a highlighted tile moves the hero");

            // Search spends the Od Hand die, not the action.
            await UseAbility(_search);
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

            // Equip the boomerang from the backpack, choosing a hand: the off hand, so the buckler swaps out.
            CardInstance boomerang = bran.Backpack.Single(card => card.Definition.Id == "boomerang");
            await Click(this, _heroPanel.BackpackSlot(bran.Backpack.ToList().IndexOf(boomerang)));
            await Click(this, _inventory.ActionButton(CardAction.Equip));
            Check(_inventory.IsChoosingHand && _inventory.HandButton(0).Text == "Main hand (stows Wooden Sword)" && _inventory.HandButton(1).Text == "Off hand (stows Buckler)",
                "Equip on the boomerang asks which hand, saying what each would stow");
            await Click(this, _inventory.HandButton(1));
            await WaitUntilIdle();
            Check(bran.Equipment.GetValueOrDefault(EquipSlot.OffHand) == boomerang, "Equip puts the card in its slot");
            Check(bran.Backpack.Any(card => card.Definition.Id == "buckler"), "the old card goes to the backpack");

            // Two weapons in hand: the Rumble row's toggle picks which one attacks, and so does a click on it in hand.
            Check(ReadiedWeapon is null, "with two weapons in hand, the rules pick until the hero does");
            _heroPanel.Pool.EmitSwap();
            Check(ReadiedWeapon?.Definition.Id == "wooden_sword", "the Rumble row's toggle readies the main hand first");
            _heroPanel.Pool.EmitSwap();
            Check(ReadiedWeapon == boomerang, "then the off hand");
            _heroPanel.Pool.EmitSwap();
            Check(ReadiedWeapon is null, "then back to letting the rules pick");
            CancelSelection();
            await Click(this, _heroPanel.EquipmentSlot(EquipSlot.OffHand));
            Check(ReadiedWeapon == boomerang, "clicking the Boomerang in hand readies it");
            await Click(this, _heroPanel.EquipmentSlot(EquipSlot.OffHand));

            // The bag opens into its cards, face up, to pick from.
            CardInstance buckler = bran.Backpack.Single(card => card.Definition.Id == "buckler");
            await Click(this, _heroPanel.BagDeck);
            Check(_heroPanel.IsBagOpen, "clicking the bag opens it over the board");
            if (System.Environment.GetEnvironmentVariable("WOAD_SHOTS") is { } shots)
            {
                await Pause(0.1);
                GetViewport().GetTexture().GetImage().SavePng($"{shots}/bag_open.png");
            }

            await Click(this, _heroPanel.BagCard(bran.Backpack.ToList().IndexOf(buckler)));
            Check(_selectedCardId == buckler.Id, "a card in the open bag can be picked");
            await Click(this, _heroPanel.BagDeck);
            Check(!_heroPanel.IsBagOpen, "clicking the bag again closes it");
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

            await UseAbility(_roll);
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
            await UseAbility(_shove);
            Check(_shoving, "Shove waits for a monster to be clicked");
            await ClickTile(new GridPosition(4, 4));
            await ConfirmIfHeld();
            Check(ledge.Monsters.Count == 0 && aria.Kills == 1, "the shoved slimeling falls into the pit and dies of the fall");

            // Leap from (4,5) over the pit to (4,2): a 6 lands there, 4-5 come down in the pit and drop
            // the hero below, 2-3 land just short at the edge, and a 1 goes nowhere.
            await UseAbility(_leap);
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
            await UseAbility(_roll);
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
            Check(_messages.Any(message => message.Contains("aids Bran")), "the log tells how the rousing went");

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
            await UseAbility(_dropRoll);
            await WaitUntilIdle();
            // (The imps may set her alight again in their phase, so the log tells whether it went out.)
            Check(aria.Prone, "Drop & roll leaves Aria prone");
            Check(_figures[aria.Id].Prone, "her figure lies down");
            Check(ActiveHero == aria && _session.State.Round > 1, "it takes her whole turn");

            await UseAbility(_roll);
            await WaitUntilIdle();
            Check(_prone.Text == "Get up" && !_prone.Disabled, "prone, the button offers to get up");
            await UseAbility(_prone);
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

            await UseAbility(_search);
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
            await Click(this, aria.Belt.Contains(bomb) ? _heroPanel.BeltSlot(aria.Belt.ToList().IndexOf(bomb)) : _heroPanel.BackpackSlot(aria.Backpack.ToList().IndexOf(bomb)));
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

            await UseAbility(_rumble);
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
    /// Debug mode (<c>-- --merchanttest</c>, with <see cref="DemoRun.Market"/>): Aria picks up the key
    /// she stands on; clicking Silas, chained beside her, frees him with it; clicking him again opens his
    /// wares, where she sells her potion and buys what she can; then, with Aria walked off, he follows her.
    /// </summary>
    private async void RunMerchantTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            await ClickTile(aria.Position);
            await WaitUntilIdle();
            Check(aria.Keys == 1, "Aria picks up the key at her feet");
            Merchant silas = ShownRoom.Merchants.Single();
            Check(_figures.ContainsKey(silas.Id) && _figures[silas.Id].Chained, "Silas is on the board, in chains");

            await ClickTile(silas.Position);
            await WaitUntilIdle();
            Check(!silas.Chained && aria.Keys == 0, "clicking him with a key frees him");

            await ClickTile(silas.Position);
            await WaitUntilIdle();
            Check(_merchantPanel.Visible && _merchantPanel.MerchantId == silas.Id, "clicking him again opens his wares");
            int wares = silas.Stock.Count;
            await Click(this, _merchantPanel.GetChildren().OfType<Button>().First(button => button.Text == "Sell"));
            await WaitUntilIdle();
            Check(aria.Gold > 0 && silas.Stock.Count == wares + 1, "Sell sells him something for gold");
            int gold = aria.Gold;
            if (_merchantPanel.GetChildren().OfType<Button>().FirstOrDefault(button => button.Text == "Buy" && !button.Disabled) is { } buy)
            {
                await Click(this, buy);
                await WaitUntilIdle();
                Check(aria.Gold < gold, "Buy buys what she can afford");
            }

            await Click(this, _merchantPanel.GetChildren().OfType<Button>().First(button => button.Text == "Close"));

            await ExecuteAsync(new RollMovement(aria.Id));
            await WaitUntilIdle();
            await ConfirmIfHeld();
            GridPosition away = _session.GetReachableTiles().Keys.OrderByDescending(tile => tile.ManhattanDistanceTo(silas.Position)).First();
            await ExecuteAsync(new MoveHero(aria.Id, away));
            await WaitUntilIdle();
            GridPosition chainedAt = silas.Position;
            await Click(this, _endTurn);
            await WaitUntilIdle();
            Check(silas.Position != chainedAt && silas.Position.ManhattanDistanceTo(aria.Position) < chainedAt.ManhattanDistanceTo(aria.Position), "with Aria gone off, he follows her");

            GD.Print("Merchant test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Merchant test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    /// <summary>
    /// Debug mode (<c>-- --dentest</c>, with <see cref="DemoRun.Den"/>): Aria walks through the door into
    /// the Ogre Champ's den, which starts its song, then fights it for a few rounds (its blows may well
    /// knock her into the pits and down to the cellar).
    /// </summary>
    private async void RunDenTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            await ExecuteAsync(new RollMovement(aria.Id));
            await WaitUntilIdle();
            await ConfirmIfHeld();
            await ExecuteAsync(new GoThroughDoor(aria.Id, Direction.North));
            await WaitUntilIdle();
            Check(aria.RoomId == "den", "Aria goes through the door into the den");
            Check(Music.Current == "ogre", "the Ogre Champ's song plays");
            Monster champ = ShownRoom.Monsters.Single();
            Check(champ.Definition.Id == "ogre_champ" && _figures.ContainsKey(champ.Id), "the Ogre Champ waits on its platform");

            for (int round = 0; round < 4 && _session.State.Phase == RunPhase.InProgress && aria.IsActive; round++)
            {
                await Click(this, _endTurn);
                await WaitUntilIdle();
            }

            GD.Print($"Den test passed (Aria ends in {aria.RoomId} at {aria.Position}).");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Den test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    /// <summary>
    /// Debug mode (<c>-- --spelltest</c>, with <see cref="DemoRun.Spells"/>): Aria freezes one slime with
    /// her Frost Wand and burns the other with her Flame Scroll, by selecting each, pressing Use and
    /// clicking the slime.
    /// </summary>
    private async void RunSpellTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            Room study = ShownRoom;
            Monster first = study.MonsterAt(new GridPosition(3, 3))!, second = study.MonsterAt(new GridPosition(3, 5))!;

            CardInstance wand = aria.AllCards.Single(card => card.Definition.Id == "frost_wand");
            await Click(this, _heroPanel.EquipmentSlot(EquipSlot.OffHand));
            Check(_selectedCardId == wand.Id, "clicking the off-hand slot selects the Frost Wand");
            await Click(this, _inventory.ActionButton(CardAction.Use));
            Check(_targeting, $"Use enters targeting ({_inventory.ActionButton(CardAction.Use).TooltipText})");
            await ClickTile(first.Position);
            await ConfirmIfHeld();
            Check(ShownRoom.FindMonster(first.Id) is null || first.Conditions.Has(Condition.Freeze), "clicking the slime freezes it (or its Ice dice finish it)");

            CardInstance scroll = aria.Belt.Single(card => card.Definition.Id == "flame_scroll");
            await Click(this, _heroPanel.BeltSlot(aria.Belt.ToList().IndexOf(scroll)));
            await Click(this, _inventory.ActionButton(CardAction.Use));
            Check(_targeting, $"Use enters targeting for the scroll ({_inventory.ActionButton(CardAction.Use).TooltipText})");
            await ClickTile(second.Position);
            await WaitUntilIdle();
            await ConfirmIfHeld();
            Check(aria.FindCard(scroll.Id) is null, "clicking the other slime burns it with the scroll, which is used up");

            GD.Print("Spell test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Spell test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    /// <summary>
    /// Debug mode (<c>-- --magictest</c>, with <see cref="DemoRun.Magic"/>): Aria casts Hurl Fire at a slime
    /// from the Cast window, then next turn puts an extra Power Die into Swirly Eyes on the ogre, picks
    /// where it goes, and studies her Flame Scroll.
    /// </summary>
    private async void RunMagicTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            Check(_heroPanel.MagicCard is { Visible: true, Disabled: false }, "a hero who knows spells has a lit Magic card to cast from");
            await Click(this, _heroPanel.MagicCard);
            Check(_spread.Hand == _magicHand && SpellCard("hurl_fire") is not null && !_spellButtons["hurl_fire"].Disabled, "the Magic card spreads out the spells she knows");
            Check(_spellButtons["quake"].Disabled, "Quake is dull with nothing beside her");
            Check(_magicHand.Cards.Count == aria.Spells.Count, "her Magic hand holds only the spells she knows");
            if (System.Environment.GetEnvironmentVariable("WOAD_SHOTS") is { } shots)
            {
                await Pause(0.1);
                GetViewport().GetTexture().GetImage().SavePng($"{shots}/magic_hand.png");
            }

            await Click(this, SpellCard("hurl_fire")!);
            Check(_casting == "hurl_fire", "picking Hurl Fire aims it");
            Monster slime = ShownRoom.MonsterAt(new GridPosition(3, 3))!;
            Check(_session.GetSpellTargets("hurl_fire").Contains(slime.Position), "the slime is in its range");
            await ClickTile(slime.Position);
            await WaitUntilIdle();
            await ConfirmIfHeld();
            Check(aria.Power.Banked == 3 && _session.AttackDiceLeft == 0, "casting spends a Power Die and her Rumble dice");
            Check(_cast.Disabled, "with her Rumble dice spent there's no more casting this turn");

            await Click(this, _endTurn);
            await WaitFor(this, () => !_busy && ActiveHero == aria && _session.State.Turn is { PendingRoll: null }, "Aria's next turn");
            Monster ogre = ShownRoom.MonsterAt(new GridPosition(3, 4)) ?? ShownRoom.Monsters.First(monster => monster.Definition.Id == "ogre");
            // Her defense against the monsters may have banked more Power: count from here.
            int banked = aria.Power.Banked;
            OnPoolTokenClicked(DicePool.Power, 0);
            Check(SelectedPowerDice == 1, "a banked Power Die selected");
            await Click(this, _heroPanel.MagicCard);
            await Click(this, SpellCard("swirly_eyes")!);
            Check(_session.GetSpellTargets("swirly_eyes").Contains(ogre.Position), "the ogre is in Swirly Eyes' range");
            await ClickTile(ogre.Position);
            Check(_swirling == ogre.Position, "Swirly Eyes asks where to move the ogre");
            GridPosition to = SwirlDestinations(_session.Content.GetSpell("swirly_eyes"), ogre.Position).First();
            await ClickTile(to);
            await WaitUntilIdle();
            await ConfirmIfHeld();
            Check(aria.Power.Banked == banked - 2, $"the selected Power Die went in on top of the spell's cost ({banked} banked, {aria.Power.Banked} left)");
            Check(_messages.Any(message => message.Contains("casts Swirly Eyes")), "the log says what she cast");

            // Her Od Hand die is still hers: she studies the Flame Scroll, whatever comes of it.
            CardInstance scroll = aria.AllCards.Single(card => card.Definition.Id == "flame_scroll");
            await Click(this, aria.FindCard(scroll.Id)!.Value.Location == CardLocation.Belt ? _heroPanel.BeltSlot(aria.Belt.ToList().IndexOf(scroll)) : _heroPanel.BackpackSlot(aria.Backpack.ToList().IndexOf(scroll)));
            Button learn = _inventory.ActionButton(CardAction.Equip);
            Check(learn.Text == "Learn" && !learn.Disabled, $"a scroll with a spell on it offers Learn ({learn.TooltipText})");
            await Click(this, learn);
            await WaitUntilIdle();
            await ConfirmIfHeld();
            Check(aria.FindCard(scroll.Id) is null, "studying it uses the scroll up");
            Check(aria.Spells.Any(spell => spell.Id == "flame_blast") || _messages.Any(message => message.Contains("goes off")), "she learns Flame Blast, or it goes off in her hands");

            GD.Print("Magic test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Magic test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    /// <summary>
    /// Debug mode (<c>-- --wingstest</c>, with <see cref="DemoRun.Wings"/>): Aria beats the Icarus Wings
    /// from her accessory slot and flies out over the chasm, rising off the board; out of flight she's
    /// offered Land. or Fly!, and lands. Then she keeps beating them, turn after turn, until a 1 breaks
    /// them and drops her, prone and stunned. Set WOAD_SHOTS to a folder to save screenshots along the way.
    /// </summary>
    private async void RunWingsTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            string? shots = System.Environment.GetEnvironmentVariable("WOAD_SHOTS");
            int shot = 0;
            async Task Shoot(string name, double wait = 0.15)
            {
                if (shots is not null)
                {
                    await Pause(wait);
                    GetViewport().GetTexture().GetImage().SavePng($"{shots}/wings_{++shot}_{name}.png");
                }
            }

            async Task Beat(Button? button = null)
            {
                if (button is null)
                {
                    // A click on the worn wings asks Land. or Fly!
                    await Click(this, _heroPanel.EquipmentSlot(EquipSlot.Accessory));
                    Check(_land.Visible && _flyOn.Visible && !_flyOn.Disabled, "clicking the wings offers Land. or Fly!");
                    await Click(this, _flyOn);
                }
                else
                {
                    await Click(this, button);
                }

                await Shoot("beat", 0.05);
                await WaitUntilIdle();
            }

            async Task FlyOut()
            {
                while (aria.Aloft && _session.State.Turn!.FlightPoints > 0 && _session.GetFlightTiles().Count > 0)
                {
                    GridPosition to = _session.GetFlightTiles().OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key.X).First().Key;
                    await ClickTile(to);
                    await WaitUntilIdle();
                }
            }

            async Task NextTurn()
            {
                await Click(this, _endTurn);
                await WaitFor(this, () => !_busy && ActiveHero == aria && _session.State.Turn is { PendingRoll: null }, "Aria's next turn");
            }

            await Beat();
            if (aria.Aloft)
            {
                Check(_figures[aria.Id].Aloft, "beating the wings lifts her figure into the air");
                await Pause(0.5);
                await Shoot("aloft");
                Check(_tokens[aria.Id].Lift > 0, "her token rises off the board");
                Check(Music.Current == "icarus", "the flight music plays");
                await FlyOut();
                await Shoot("flown", 0.3);
                Check(_land.Visible && _flyOn.Visible, "out of flight: Land. or Fly!");
                await Click(this, _land);
                await WaitUntilIdle();
                Check(!aria.Aloft && !_figures[aria.Id].Aloft && !aria.Prone, "Land. brings her down, on her feet");

                // A right-click on the wings picks them up instead, for their card buttons.
                Vector2 wings = _heroPanel.EquipmentSlot(EquipSlot.Accessory).GetGlobalRect().GetCenter();
                foreach (bool pressed in new[] { true, false })
                {
                    GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = pressed, Position = wings, GlobalPosition = wings }, inLocalCoords: true);
                    await NextFrame(this);
                }

                Check(_selectedCardId == aria.Equipment[EquipSlot.Accessory].Id && !aria.Aloft, "right-clicking the wings picks them up rather than taking off");
                CancelSelection();
            }

            // Now she dares them again and again until they break.
            bool broke = !aria.EquippedCards.Any(card => card.Definition.Wings);
            for (int turn = 0; turn < 30 && !broke; turn++)
            {
                await NextTurn();
                await Beat();
                while (aria.Aloft)
                {
                    await FlyOut();
                    if (aria.Aloft)
                    {
                        await Beat(_flyOn);
                    }
                }

                broke = !aria.EquippedCards.Any(card => card.Definition.Wings);
            }

            Check(broke, "a 1 breaks the wings sooner or later");
            await Shoot("broken", 0.1);
            Check(aria.Prone && aria.Stun.IsStunned, "broken wings leave her prone and stunned");
            Check(_session.MoveDiceLeft == 0, "and her Wend gone for the turn");
            Check(!_figures[aria.Id].Aloft, "her figure is back on the ground");
            GD.Print("Wings test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Wings test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    /// <summary>
    /// Debug mode (<c>-- --terraintest</c>, with <see cref="DemoRun.Terrain"/>): in a dark room, Aria lays
    /// Icicle Arrow's ice, lights a brazier with Hurl Fire, then shines with Shining Light and hides with
    /// Shadowbind, a turn each; the board and her figure must show it all.
    /// </summary>
    private async void RunTerrainTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            Room crypt = ShownRoom;
            Check(_board.DarkTiles.Count == crypt.Positions.Count(), "the dark room starts all dark");

            async Task CastAt(string spellId, GridPosition target)
            {
                await Click(this, _heroPanel.MagicCard);
                await Click(this, SpellCard(spellId)!);
                Check(_session.GetSpellTargets(spellId).Contains(target), $"{spellId} can be aimed at {target}");
                await ClickTile(target);
                await WaitUntilIdle();
                await ConfirmIfHeld();
            }

            async Task NextTurn()
            {
                await Click(this, _endTurn);
                await WaitFor(this, () => !_busy && ActiveHero == aria && _session.State.Turn is { PendingRoll: null }, "Aria's next turn");
            }

            await CastAt("icicle_arrow", new GridPosition(4, 4));
            Check(crypt.IsIcy(new GridPosition(4, 4)), "Icicle Arrow at bare floor lays ice there");
            if (_session.State.Turn!.IceToLay > 0)
            {
                Check(LayingIce, "with more to lay, the clicks lay it");
                int before = crypt.Ice.Count;
                await ClickTile(_session.GetIceTargets().First(tile => !crypt.IsIcy(tile)));
                await WaitUntilIdle();
                Check(crypt.Ice.Count == before + 1, "a click lays another patch");
            }

            await NextTurn();
            var brazier = new GridPosition(1, 1);
            await CastAt("hurl_fire", brazier);
            Check(crypt.LitBraziers.Contains(brazier), "Hurl Fire at a brazier lights it");
            Check(!_board.DarkTiles.Contains(new GridPosition(2, 2)) && _board.DarkTiles.Contains(new GridPosition(6, 6)), "its light reaches two tiles, no further");

            await NextTurn();
            await CastAt("shining_light", aria.Position);
            Check(aria.ShiningRounds > 0 && _figures[aria.Id].Shining, "Shining Light on herself: she glows");
            Check(!_board.DarkTiles.Contains(aria.Position), "and lights the dark around her");

            await NextTurn();
            await CastAt("shadowbind", aria.Position);
            Check(aria.ShadowboundRounds > 0 && _figures[aria.Id].Shadowbound, "Shadowbind on herself: she fades into the shadows");

            GD.Print("Terrain test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Terrain test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    /// <summary>
    /// Debug mode (<c>-- --dietypetest</c>, with <see cref="DemoRun.Elements"/>): Aria reads her scrolls
    /// of typed dice at the monsters, a belt one and a pack one a turn, digging out or tending her eyes
    /// when she must. After every step each figure's tokens must match the rules', and the typed dice
    /// must show in their own colors.
    /// </summary>
    private async void RunDieTypeTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            for (int turn = 0; turn < 8 && aria.IsInPlay && _session.State.Phase == RunPhase.InProgress; turn++)
            {
                if (aria.Conditions.Has(Condition.Buried) && _digOut.Visible && !_digOut.Disabled)
                {
                    await UseAbility(_digOut);
                    await WaitUntilIdle();
                    CheckTokens();
                }

                if (aria.Conditions.Has(Condition.Blind) && _session.CheckAid(aria.Id, Ailment.Blind) is null)
                {
                    ExecuteOdHand((heroId, dice, tools, power) => new Aid(heroId, aria.Id, Ailment.Blind, dice, tools, power));
                    await WaitUntilIdle();
                    await ConfirmIfHeld();
                    CheckTokens();
                }

                foreach (bool fromBelt in new[] { true, false })
                {
                    if (ActiveHero != aria)
                    {
                        break;
                    }

                    IReadOnlyList<CardInstance> place = fromBelt ? aria.Belt : aria.Backpack;
                    if (place.FirstOrDefault(card => card.Definition.Effect?.DieType is not null) is not { } scroll
                        || _session.GetCardTargets(scroll.Id).Where(tile => ShownRoom.MonsterAt(tile) is not null).Select(tile => (GridPosition?)tile).FirstOrDefault() is not { } target)
                    {
                        continue;
                    }

                    int slot = place.ToList().IndexOf(scroll);
                    await Click(this, fromBelt ? _heroPanel.BeltSlot(slot) : _heroPanel.BackpackSlot(slot));
                    await Click(this, _inventory.ActionButton(CardAction.Use));
                    Check(_targeting, $"Use enters targeting for the {scroll.Definition.Name}");
                    await ClickTile(target);
                    await ConfirmIfHeld();
                    Check(aria.FindCard(scroll.Id) is null || aria.FindCard(scroll.Id)!.Value.Card.Count < scroll.Count + 1, $"the {scroll.Definition.Name} is read");
                    CheckTokens();
                }

                if (ActiveHero == aria)
                {
                    await Click(this, _endTurn);
                }

                await WaitUntilIdle();
                CheckTokens();
            }

            Check(_dice.ShowedTypedDice, "typed dice show in their own colors");
            GD.Print("Die type test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Die type test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    /// <summary>Every figure on show carries the tokens the rules say it has.</summary>
    private void CheckTokens()
    {
        foreach (Monster monster in ShownRoom.Monsters)
        {
            Check(_figures.TryGetValue(monster.Id, out Figure? figure) && SameTokens(figure.Conditions, monster.Conditions), $"the {monster.Definition.Name}'s figure shows its tokens ({string.Join(", ", monster.Conditions.All.Select(entry => $"{entry.Key} {entry.Value}"))})");
        }

        foreach (Hero hero in _session.State.HeroesIn(ShownRoom))
        {
            Check(_figures.TryGetValue(hero.Id, out Figure? figure) && SameTokens(figure.Conditions, hero.Conditions), $"{hero.Name}'s figure shows their tokens ({string.Join(", ", hero.Conditions.All.Select(entry => $"{entry.Key} {entry.Value}"))})");
        }
    }

    private static bool SameTokens(IReadOnlyDictionary<Condition, int> shown, Conditions held) =>
        shown.Count == held.All.Count && held.All.All(entry => shown.GetValueOrDefault(entry.Key) == entry.Value);

    /// <summary>
    /// Debug mode (<c>-- --monstertest</c>, with <see cref="DemoRun.Menagerie"/>): clicking the chest
    /// beside Aria wakes a Mimic in its place, whose grab (when it lands) shuts off Wend; then a few
    /// rounds of the new monsters acting on the board, the Necromancer raising the dead among them.
    /// </summary>
    private async void RunMonsterTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            var chest = new GridPosition(5, 4);

            await ClickTile(chest);
            await WaitUntilIdle();
            Check(ShownRoom.Layout[chest] == TileKind.Floor && ShownRoom.MonsterAt(chest)?.Definition.Id == "mimic", "clicking the chest wakes a Mimic where it stood");
            Check(_figures.Values.Any(figure => figure.Position == chest && !figure.IsHero), "the Mimic is on the board");
            Check(_session.HeldBy(aria) is null || _roll.Disabled, "held by the Mimic, Aria can't roll to move");

            for (int round = 0; round < 3 && _session.State.Phase == RunPhase.InProgress; round++)
            {
                await Click(this, _endTurn);
                await WaitUntilIdle();
            }

            Check(ShownRoom.Monsters.Any(monster => monster.RaisedBy is not null), "the Necromancer has raised the dead");
            Check(ShownRoom.Monsters.All(monster => _figures.ContainsKey(monster.Id)), "every monster has a figure");

            GD.Print("Monster test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Monster test failed: {exception.Message}");
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
            Check(Woad.Core.Rules.GameSession.CanHeroReach(aria, ShownRoom.Layout, aria.Position, slime.Position), "clicking it again walks her up to it");
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
            await Click(this, _heroPanel.BackpackSlot(aria.Backpack.ToList().IndexOf(emerald)));
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

    /// <summary>
    /// Debug mode (<c>-- --companiontest</c>, with <see cref="DemoRun.Escort"/>): Aria orders Pip to
    /// Fight from the Orders panel and ends her turns; Pip walks over to the slime and attacks it.
    /// </summary>
    private async void RunCompanionTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            Hero pip = _session.State.Heroes.Single(hero => hero.Name == "Pip");
            await Click(this, _orders);
            await Click(this, OrderButton(0, CompanionOrder.Fight));
            await WaitUntilIdle();
            Check(pip.Order == CompanionOrder.Fight, "the Fight order button gives the order");

            bool attacked = false;
            CommandPlayed += (command, _) => attacked |= command is AttackMonster && command.HeroId == pip.Id;
            for (int round = 0; round < 8 && !attacked && _session.State.Phase == RunPhase.InProgress; round++)
            {
                await Click(this, _endTurn);
                await WaitFor(this, () => ActiveHero == aria && !_busy || _session.State.Phase != RunPhase.InProgress, "Pip's turn and the slime's to pass");
                await WaitUntilIdle();
            }

            Check(attacked, $"Pip attacks the slime (Pip at {pip.Position}, {_session.State.Rooms["yard"].Monsters.FirstOrDefault()?.ToString() ?? "no slime"})");
            GD.Print("Companion test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Companion test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    /// <summary>
    /// Debug mode (<c>-- --partytest</c>, with <see cref="DemoRun.PartyCheck"/>): Aria opens the Party menu,
    /// spends her point on a heart, and tells Pip to pick up loot.
    /// </summary>
    private async void RunPartyTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            Hero pip = _session.State.Heroes.Single(hero => hero.Name == "Pip");
            Check(_partyButton.Text == "Party!", "the Party button says when there's a point to spend");
            await Click(this, _partyButton);
            Check(_party.Visible, "the Party button opens the Party menu");

            int max = aria.MaxHearts;
            await Click(this, _party.Find($"Spend{aria.Id}Hearts")!);
            await WaitUntilIdle();
            Check(aria.MaxHearts == max + 1 && aria.PointsToSpend == 0, "spending the point on a heart raises her hearts");

            await Click(this, _party.Find($"Policy{pip.Id}Loot")!);
            await WaitUntilIdle();
            Check(pip.Policy.PicksUpLoot, "the Loot switch tells Pip to pick up loot");

            await Click(this, _party.Find("Close")!);
            Check(!_party.Visible, "Close puts the Party menu away");
            GD.Print("Party test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Party test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    /// <summary>
    /// Debug mode (<c>-- --bosstest</c>, with <see cref="DemoRun.BossRoom"/>): Aria orders Pip to Fight in the
    /// Iron Warden's room and ends her turns; the Warden and Pip both have to act where they can be seen.
    /// </summary>
    private async void RunBossTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            Hero pip = _session.State.Heroes.Single(hero => hero.Name == "Pip");
            await Click(this, _orders);
            await Click(this, OrderButton(0, CompanionOrder.Fight));
            await WaitUntilIdle();

            int pipAttacks = 0, monsterAttacks = 0;
            CommandPlayed += (command, _) => pipAttacks += command is AttackMonster && command.HeroId == pip.Id ? 1 : 0;
            for (int round = 0; round < 4 && _session.State.Phase == RunPhase.InProgress; round++)
            {
                int before = _session.State.Round;
                await Click(this, _endTurn);
                await WaitFor(this, () => (ActiveHero == aria && !_busy) || _session.State.Phase != RunPhase.InProgress, "the round to come back to Aria");
                await WaitUntilIdle();
                monsterAttacks += _session.State.Round - before;
            }

            GD.Print($"Boss test: Pip attacked {pipAttacks} times; Pip at {pip.Position}, Aria at {aria.Position}; {string.Join(", ", ShownRoom.Monsters)}.");
            Check(pipAttacks > 0, "Pip attacks in the boss room");
            GD.Print("Boss test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Boss test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    internal Button SearchButton => _search;

    /// <summary>Out on the Woarld Map, up in the top strip by the Party button.</summary>
    internal Button RetreatButton => _retreat;

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

    /// <summary>Waits for event playback to finish, adding any Power Dice a 6 earned.</summary>
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

    /// <summary>A spell's card in the spread Magic hand.</summary>
    private SheetCard? SpellCard(string spellId) => _spellButtons.TryGetValue(spellId, out Button? button) ? _spread.CardFor(button) : null;

    /// <summary>Plays an ability from its hand: spreads the hand out, then clicks its card.</summary>
    internal async Task UseAbility(Button action)
    {
        HandView hand = Hands.First(candidate => candidate.Cards.Any(card => card.Action == action));
        if (_spread.Hand != hand)
        {
            await Click(this, hand);
        }

        SheetCard? card = _spread.CardFor(action);
        Check(card is not null, $"{action.Text} is in the {hand.Title} hand");
        if (System.Environment.GetEnvironmentVariable("WOAD_SHOTS") is { } shots)
        {
            await Pause(0.1);
            GetViewport().GetTexture().GetImage().SavePng($"{shots}/hand_{hand.Title.Replace(" ", "")}_{System.DateTime.Now.Ticks}.png");
        }

        await Click(this, card!);
    }

    private Task ClickTile(GridPosition tile) =>
        ClickAt(this, _board.GetGlobalRect().Position + BoardView.TileOrigin(tile) + new Vector2(BoardView.TileSize / 2f, BoardView.TileSize / 2f));
}
