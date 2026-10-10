namespace Woad.Core.Content;

/// <summary>Content data files failed to parse or broke a rule. Lists every problem found.</summary>
public sealed class InvalidContentException(IReadOnlyList<string> errors)
    : Exception("Invalid game content:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => "  - " + e)))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
