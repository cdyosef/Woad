using Godot;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Scenes.Ui;
using static Woad.Scenes.Debug.UiDriver;

namespace Woad.Scenes.Game;

/// <summary>
/// Debug mode (<c>-- --toolstest</c>, with <see cref="DemoRun.Tools"/>): Aria's worn Sticky Gloves play on their
/// own from the accessory slot; dragged into her off hand they join the Od Hand square and its rolls, leaving
/// their ghost behind, which a double-click puts back on; her belt's Lockpicks are played alone or drawn into a hand.
/// </summary>
public partial class GameScreen
{
    private async void RunToolsTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            CardInstance gloves = aria.EquippedCards.Single(card => card.Definition.Id == "sticky_gloves");
            CardInstance picks = aria.Belt.Single(card => card.Definition.Id == "lockpicks");
            bool InOdHand(CardInstance card) => StackOf(_odHand, aria).Any(stacked => stacked.CardId == card.Id);
            Vector2 accessory = _heroPanel.EquipmentSlot(EquipSlot.Accessory).GetGlobalRect().GetCenter();

            Check(aria.SlotOf(gloves) == EquipSlot.Accessory && !InOdHand(gloves) && _session.ToolsBonusLeft == 0, "worn, the Sticky Gloves stay out of the Od Hand square");

            // Double-clicked where they're worn: they steal on their own.
            await DoubleClickAt(this, accessory);
            await WaitUntilIdle();
            Check(_stealing && _aloneTool == gloves.Id, $"double-clicking the worn gloves aims a steal with them alone ({_stealing}, {_aloneTool})");
            CancelSelection();

            // Dragged into the off hand: in the Od Hand square, in its rolls, their ghost left behind.
            await DragAt(this, accessory, _odHand.GetGlobalRect().GetCenter());
            await WaitUntilIdle();
            Check(aria.SlotOf(gloves) == EquipSlot.OffHand && InOdHand(gloves) && _session.ToolsBonusLeft == 1 && AccessoryGhost(aria) == gloves,
                $"dragged into the off hand, the gloves join the Od Hand square and its rolls, leaving their ghost ({aria.SlotOf(gloves)}, {_session.ToolsBonusLeft})");
            await SaveShot("tools_ghost");

            // The ghost double-clicked: back on.
            await DoubleClickAt(this, accessory);
            await WaitUntilIdle();
            Check(aria.SlotOf(gloves) == EquipSlot.Accessory && AccessoryGhost(aria) is null, "double-clicking the ghost puts the gloves back on");

            // The belt's Lockpicks: played alone, or drawn into a hand.
            await DoubleClickAt(this, _heroPanel.BeltPoint(aria.Belt.ToList().IndexOf(picks)));
            await WaitUntilIdle();
            Check(_inventory.IsChoosingHand && _inventory.HandButton(0).Text == "Pick a lock with it alone" && _inventory.HandButton(1).Text == "Draw it into a hand",
                $"double-clicking the belt's Lockpicks asks: alone, or into a hand ({_inventory.HandButton(0).Text} / {_inventory.HandButton(1).Text})");
            await Click(this, _inventory.HandButton(1));
            await WaitUntilIdle();
            Check(_inventory.IsChoosingHand, "drawing them asks which hand");
            await Click(this, _inventory.HandButton(1));
            await WaitUntilIdle();
            Check(aria.SlotOf(picks) == EquipSlot.OffHand && InOdHand(picks) && _session.ToolsBonusLeft == 1, $"drawn into the off hand, the Lockpicks go in with the Od Hand dice ({aria.SlotOf(picks)})");

            GD.Print("Tools test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Tools test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }
}
