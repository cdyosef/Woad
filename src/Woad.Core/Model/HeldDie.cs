using Woad.Core.Content;

namespace Woad.Core.Model;

/// <summary>A die rolled and held for a hero's next defense (a 6 rolled another, so it can be more faces), by whom and with what skill.</summary>
public sealed record HeldDie(int ByHeroId, Skill Skill, IReadOnlyList<int> Faces);
