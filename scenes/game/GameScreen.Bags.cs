using Godot;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// The bags under the belt, double-clicked: the Medicine Bag aims Aid and Burgling Tools go to work, as from the belt;
/// the Rock Bag asks USE, with two blinking buttons, the Od Hand (into the off hand) and Rumble (into the main hand);
/// any other bag uses what's on top in it, or opens when it's empty.
/// </summary>
public partial class GameScreen
{
    private Panel? _bagUse;

    /// <summary>The Rock Bag's USE pop-up, while it's up.</summary>
    internal Panel? BagUsePopup => _bagUse is { Visible: true } ? _bagUse : null;

    private void UseBag(int bagId)
    {
        CloseBagUse();
        if (Locked || ActiveHero is not { } hero || hero.Bags.FirstOrDefault(bag => bag.Id == bagId) is not { } bag)
        {
            return;
        }

        if (bag.Definition.MedicineBag || bag.Definition.Tool is not null)
        {
            UseFromBelt(bag.Id);
            return;
        }

        if (BagRow.Top(bag) is not { } top)
        {
            _heroPanel.TogglePouch(bag.Id);
            RefreshPanels();
            return;
        }

        if (top.Definition.Slot is EquipSlot.OffHand or EquipSlot.MainHand && top.Definition.AltSlot is not null)
        {
            AskBagUse(hero, bag, top);
            return;
        }

        UseFromBelt(top.Id);
    }

    /// <summary>
    /// USE, in the display font, over two blinking buttons: the Od Hand's (left) draws a handful into the off hand, Rumble's
    /// (right) into the main hand; one that can't go there is crossed out.
    /// </summary>
    private void AskBagUse(Hero hero, CardInstance bag, CardInstance top)
    {
        int index = hero.Bags.ToList().IndexOf(bag);
        Vector2 at = _heroPanel.BagRow.GetGlobalTransform() * _heroPanel.BagRow.KnotPoint(index);
        _bagUse = new Panel { Position = new Vector2(Mathf.Clamp(at.X - 26, 2, SideWidth * _sheetScale - 54), at.Y - 46), Size = new Vector2(52, 40), ZIndex = 6 };
        _bagUse.AddThemeStyleboxOverride("panel", UiTheme.Box(Palette.Ink, Palette.Trim));
        Label title = UiTheme.Heading(UiTheme.Label(new Vector2(0, 1), new Vector2(52, 12), "USE", 9, Palette.TrimBright), 1);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        _bagUse.AddChild(title);
        AddChild(_bagUse);
        foreach ((EquipSlot hand, string icon, Color color, float x) in new[] { (EquipSlot.OffHand, "icon_odhand", Palette.DieOdHand, 4f), (EquipSlot.MainHand, "icon_rumble", Palette.DieRumble, 28f) })
        {
            string? why = _session.CheckEquip(top.Id, hand);
            var button = new Button
            {
                Name = hand == EquipSlot.OffHand ? "OdHand" : "Rumble",
                Position = new Vector2(x, 15),
                Size = new Vector2(20, 20),
                Icon = GameArt.Ui(icon),
                ExpandIcon = true,
                TooltipText = why ?? $"{top.Definition.Name} into your {(hand == EquipSlot.OffHand ? "off hand (Od Hand)" : "main hand (Rumble)")}.",
            };
            button.AddThemeStyleboxOverride("normal", UiTheme.Box(Palette.Panel, color));
            button.AddThemeStyleboxOverride("hover", UiTheme.Box(Palette.PanelHover, color.Lightened(0.3f), 2));
            _bagUse.AddChild(button);
            if (why is not null)
            {
                button.Disabled = true;
                button.AddChild(new CrossOut { Size = button.Size, MouseFilter = Control.MouseFilterEnum.Ignore });
            }
            else
            {
                Tween blink = button.CreateTween().SetLoops();
                blink.TweenProperty(button, "modulate", new Color(1, 1, 1, 0.45f), 0.45);
                blink.TweenProperty(button, "modulate", Colors.White, 0.45);
            }

            button.Pressed += () =>
            {
                CloseBagUse();
                Execute(new EquipCard(hero.Id, top.Id, hand));
            };
        }
    }

    private void CloseBagUse()
    {
        _bagUse?.QueueFree();
        _bagUse = null;
    }

    /// <summary>A red X over a button that can't be used.</summary>
    private sealed partial class CrossOut : Control
    {
        public override void _Draw()
        {
            DrawLine(new Vector2(3, 3), Size - new Vector2(3, 3), Palette.Danger, 2);
            DrawLine(new Vector2(Size.X - 3, 3), new Vector2(3, Size.Y - 3), Palette.Danger, 2);
        }
    }
}
