using Godot;

namespace Woad.Scenes.Ui;

public sealed record Portrait(string Id, string Name, Color Color, int Sigil);

/// <summary>Placeholder portraits: heraldic crests until the art pass brings real character portraits.</summary>
public static class Portraits
{
    public static IReadOnlyList<Portrait> All { get; } =
    [
        new("woad", "Woad", new Color("3f6fd8"), 0),
        new("ember", "Ember", new Color("d8613f"), 1),
        new("moss", "Moss", new Color("5fae5a"), 2),
        new("gilt", "Gilt", new Color("d8b13f"), 3),
        new("violet", "Violet", new Color("9a5fd8"), 4),
        new("bone", "Bone", new Color("d8d0bc"), 5),
        new("blood", "Blood", new Color("b8323f"), 6),
        new("frost", "Frost", new Color("5fc8d8"), 7),
    ];

    public static Portrait Get(string id) => All.FirstOrDefault(portrait => portrait.Id == id) ?? All[0];
}
