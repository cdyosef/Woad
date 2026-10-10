using Godot;
using Woad.Core.Content;
using Woad.Core.Model;

namespace Woad.Scenes.Ui;

/// <summary>A clickable card in an equipment slot, the belt or the backpack.</summary>
public partial class CardSlot : Button
{
    public CardSlot()
    {
        ToggleMode = true;
        FocusMode = FocusModeEnum.None;
        Alignment = HorizontalAlignment.Left;
        ClipText = true;
        TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        Pressed += () => Sfx.Play("click");
    }

    public CardInstance? Card { get; private set; }

    public void ShowCard(CardInstance? card, string emptyText, bool selected)
    {
        Card = card;
        Disabled = card is null;
        SetPressedNoSignal(selected && card is not null);
        Text = card is null ? emptyText : Title(card);
        TooltipText = card is null ? "" : $"{card.Definition.Name}\n{card.Definition.Text}";
    }

    public static string Title(CardInstance card) =>
        card.Definition.Usage == CardUsage.Charges
            ? $"{card.Definition.Name} ({card.ChargesRemaining}/{card.Definition.Charges})"
            : card.Definition.Name;
}
