using Woad.Core.Grid;

namespace Woad.Core.Model;

/// <summary>
/// Where a hero or companion died: "Here lies Bran", with what killed them. What they dropped lies on
/// its tile. It doesn't block movement or line of sight.
/// </summary>
/// <param name="Epitaph">What killed them, e.g. "Slain by a Goblin".</param>
/// <param name="HereLies">Whether the stone reads "Here lies…" rather than "RIP…".</param>
public sealed record Tombstone(GridPosition Position, int HeroId, string Name, string Epitaph, bool HereLies)
{
    /// <summary>The name line: "Here lies Bran" or "RIP Bran".</summary>
    public string Inscription => HereLies ? $"Here lies {Name}" : $"RIP {Name}";
}
