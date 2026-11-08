using Godot;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Scenes.Ui;
using static Woad.Scenes.Debug.UiDriver;

namespace Woad.Scenes.Game;

/// <summary>
/// Debug mode (<c>-- --painttest</c>, with <see cref="DemoRun.Paint"/>): Aria uses her Shield Paint, and her crest
/// shows on the Knight's Shield in her off hand, on its card and on her figure.
/// </summary>
public partial class GameScreen
{
    private async void RunPaintTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            CardInstance shield = aria.Equipment[EquipSlot.OffHand];
            CardInstance paint = aria.AllCards.Single(card => card.Definition.Id == "shield_paint");
            Check(shield.Definition.Id == "knights_shield" && !shield.Painted, "Aria holds a bare Knight's Shield");
            await ExecuteAsync(new UseCard(aria.Id, paint.Id));
            await WaitUntilIdle();
            Check(shield.Painted && aria.FindCard(paint.Id) is null, "the paint is used up, painting the shield");
            Check(_tokens[aria.Id].Gear?.Crest is { Charge: "eagle", Field: "per_fess" }, "her figure's shield shows her crest");
            Check(SheetCard.PaintedCrest?.Invoke(shield) is { Charge: "eagle" }, "the shield's card shows her crest");
            await SaveShot("painted_shield");

            GD.Print("Paint test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Paint test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }
}
