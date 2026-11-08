using Woad.Core.Grid;

namespace Woad.Core.Model;

/// <summary>A slain monster's body floating belly-up over the water (Leviathan Jr.'s), on <paramref name="Tiles"/>, which can be stood on.</summary>
public sealed record Carcass(string MonsterId, IReadOnlyList<GridPosition> Tiles);
