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
}
