namespace Woad.Core.Model;

/// <summary>
/// How a character looks: which figure (<paramref name="Look"/>) and its colors. Purely cosmetic; the
/// game maps the ids to art and treats unknown or empty ones as that look's defaults.
/// </summary>
public sealed record Appearance(string Look = "", string Main = "", string Trim = "", string Skin = "", string Hair = "")
{
    public static Appearance Default { get; } = new();
}
