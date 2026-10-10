namespace Woad.Core.Characters;

/// <summary>Problems are files that couldn't be read (or parts of them that were skipped), for the player to see.</summary>
public sealed record CharacterLoadResult(IReadOnlyList<Character> Characters, IReadOnlyList<string> Problems);

/// <summary>
/// Where characters live between runs. Local JSON files for now; online play will swap in a
/// server-backed implementation, which is why the methods are asynchronous.
/// </summary>
public interface ICharacterRepository
{
    /// <summary>All saved characters, oldest first.</summary>
    Task<CharacterLoadResult> LoadAllAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(Character character, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid characterId, CancellationToken cancellationToken = default);
}
