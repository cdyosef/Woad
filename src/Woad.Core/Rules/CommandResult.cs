using Woad.Core.Events;

namespace Woad.Core.Rules;

/// <summary>The outcome of a command: the events it caused, or why it was refused.</summary>
public sealed class CommandResult
{
    private CommandResult(string? error, IReadOnlyList<GameEvent> events)
    {
        Error = error;
        Events = events;
    }

    public bool IsAccepted => Error is null;

    /// <summary>A message for the player explaining why the command was refused.</summary>
    public string? Error { get; }

    public IReadOnlyList<GameEvent> Events { get; }

    internal static CommandResult Accept(IReadOnlyList<GameEvent> events) => new(null, events);

    internal static CommandResult Reject(string error) => new(error, []);

    public override string ToString() => IsAccepted ? $"Accepted ({Events.Count} events)" : $"Rejected: {Error}";
}
