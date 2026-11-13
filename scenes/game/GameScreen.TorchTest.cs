using Godot;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Model;
using Woad.Scenes.Ui;
using static Woad.Scenes.Debug.UiDriver;

namespace Woad.Scenes.Game;

/// <summary>
/// Debug mode (<c>-- --torchtest</c>, with <see cref="DemoRun.Torch"/>): Aria, who doesn't know Search, draws her torch
/// into her off hand; double-clicking it in the Od Hand square searches by its light, and with it in hand she can still
/// move and fight.
/// </summary>
public partial class GameScreen
{
    private async void RunTorchTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            CardInstance torch = aria.AllCards.Single(card => card.Definition.Id == "torch");
            await ExecuteAsync(new EquipCard(aria.Id, torch.Id, EquipSlot.OffHand));
            await WaitUntilIdle();
            Check(aria.SlotOf(torch) == EquipSlot.OffHand && aria.TorchesHeld == 1, "the torch is lit in her off hand");
            Check(_session.CheckSearch() is null && _session.Searches, "by its light she can search, without the skill");

            await DoubleClickAt(this, _odHand.GetGlobalRect().Position + _odHand.CardPoint("item:" + torch.Id));
            await WaitUntilIdle();
            await ConfirmIfHeld();
            Check(_messages.Any(message => message.Contains("search", StringComparison.OrdinalIgnoreCase)), $"double-clicking the torch searches ({string.Join(" / ", _messages.TakeLast(3))})");

            Check(!_roll.Disabled && _roll.Visible, "holding the torch, she can still roll Wend");
            RollWend();
            await WaitUntilIdle();
            await ConfirmIfHeld();
            Check(_session.GetReachableTiles().Count > 0, $"and move ({string.Join(" / ", _messages.TakeLast(4))})");

            GD.Print("Torch test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Torch test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    /// <summary>
    /// Debug mode (<c>-- --torchsearchtest</c>, with <see cref="DemoRun.TorchBeside"/>): Aria knows Search and holds a
    /// torch in her off hand, a fire beetle right beside her. After a swing at it, Search still searches.
    /// </summary>
    private async void RunTorchSearchTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            CardInstance torch = aria.AllCards.Single(card => card.Definition.Id == "torch");
            await ExecuteAsync(new EquipCard(aria.Id, torch.Id, EquipSlot.OffHand));
            await WaitUntilIdle();
            // A Rumble swing at the beetle beside her: now the torch in her off hand could follow it up.
            Monster beetle = ShownRoom.Monsters.Single();
            await ExecuteAsync(new AttackMonster(aria.Id, beetle.Id));
            await WaitUntilIdle();
            await ConfirmIfHeld();
            Check(ShownRoom.FindMonster(beetle.Id) is not null, "the beetle is still there");
            int used = _session.State.Turn!.OdHandDiceUsed;
            _search.EmitSignal(BaseButton.SignalName.Pressed);
            await WaitUntilIdle();
            await ConfirmIfHeld();
            Check(!_offHanding && (_session.State.Turn!.OdHandDiceUsed > used || _session.State.Turn.ToolsUsed),
                $"Search searches, by the torch's light ({string.Join(" / ", _messages.TakeLast(3))})");
            GD.Print("Torch search test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Torch test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    /// <summary>
    /// Debug mode (<c>-- --poweruptest</c>): Power up, left of End turn, rolls every die she has left at once, banks the
    /// 6s as Power Dice, and ends her turn.
    /// </summary>
    private async void RunPowerUpTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            int round = _session.State.Round;
            Check(!_powerUp.Disabled && _powerUp.Visible, $"Power up is there to click ({_powerUp.TooltipText})");
            Check(_powerUp.GetGlobalRect().End.X <= _endTurn.GetGlobalRect().Position.X, "it's left of End turn");
            int dice = _session.PowerUpDice;
            int banked = aria.Power.Banked;
            await Click(this, _powerUp);
            await WaitUntilIdle();
            Check(_messages.Any(line => line.Contains("powers up")), $"she powers up ({string.Join(" / ", _messages.TakeLast(3))})");
            Check(_session.State.Round > round, $"and her turn's over (round {round} to {_session.State.Round})");
            Check(aria.Power.Banked >= banked, $"the 6s of {dice} dice banked ({banked} to {aria.Power.Banked})");

            GD.Print("Power up test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Power up test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }
}
