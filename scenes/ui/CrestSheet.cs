using Godot;

namespace Woad.Scenes.Ui;

/// <summary>
/// Debug mode (<c>-- --crestsheet</c>): every crest field and charge on one screen, in a few color pairings,
/// saved as a picture when WOAD_SHOTS is set. For looking the art over.
/// </summary>
public partial class CrestSheet : Control
{
    private static readonly (string Field, string Ink, string Rim)[] Pairings =
    [
        ("woad", "gilt", "bone"),
        ("blood", "bone", "gilt"),
        ("moss", "coal", "gilt"),
        ("violet", "frost", "coal"),
    ];

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        if (System.Environment.GetEnvironmentVariable("WOAD_SHOTS") is { } shots)
        {
            GetTree().CreateTimer(0.5).Timeout += () =>
            {
                GetViewport().GetTexture().GetImage().SavePng($"{shots}/crest_sheet.png");
                GetTree().Quit();
            };
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Palette.Background);
        Color Of(string id) => Looks.Find(Looks.Colors, id)!.Color;

        // Every charge, on a plain field, in each pairing (a row each).
        for (int row = 0; row < Pairings.Length; row++)
        {
            (string field, string ink, string rim) = Pairings[row];
            for (int i = 0; i < Crests.Charges.Count; i++)
            {
                var rect = new Rect2(8 + i * 25, 8 + row * 40, 22, 27);
                Crests.Draw(this, rect, new Crest(row % 2 == 0 ? "plain" : Crests.Fields[1 + i % (Crests.Fields.Count - 1)].Id, Crests.Charges[i].Id, Of(field), Of(ink), Of(rim)));
            }
        }

        // Every field, big, with a roundel and a star.
        for (int i = 0; i < Crests.Fields.Count; i++)
        {
            var rect = new Rect2(8 + i * 48, 180, 40, 48);
            Crests.Draw(this, rect, new Crest(Crests.Fields[i].Id, i % 2 == 0 ? "eagle" : "tower", Of("blood"), Of("gilt"), Of("bone")));
        }

        // The charges big, to see their shapes.
        for (int i = 0; i < Crests.Charges.Count; i++)
        {
            var rect = new Rect2(8 + (i % 12) * 52, 240 + (i / 12) * 60, 44, 54);
            Crests.Draw(this, rect, new Crest("plain", Crests.Charges[i].Id, Of("woad"), Of("gilt"), Of("gilt")));
        }
    }
}
