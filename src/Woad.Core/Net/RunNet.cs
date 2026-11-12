using Woad.Core.Commands;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Saves;

namespace Woad.Core.Net;

/// <summary>
/// A run played over a network, the rules' side of it (no sockets here). The host holds the run. Every player sends
/// the host their moves; the host plays each through the rules (refusing what isn't theirs to make, or isn't allowed)
/// and passes every accepted move on, numbered, with a fingerprint of the run after it. Everyone else holds a copy of
/// the run (the same start, the same seed) and plays the same moves in the same order, checking the fingerprint: the
/// rules are deterministic, so the copies stay the same, and one that doesn't is caught at once. A run's start and its
/// moves so far are a <see cref="SavedRun"/>, so that's also what someone joining late (or again) is sent.
/// </summary>
public static class RunWire
{
    /// <summary>A move as text, to send.</summary>
    public static string Write(GameCommand command) => SavedRun.CommandToJson(command);

    /// <exception cref="InvalidDataException">Not a move this version knows.</exception>
    public static GameCommand Read(string json) => SavedRun.CommandFromJson(json);

    /// <summary>
    /// A fingerprint of where a run stands: its round, whose turn it is, and every hero and monster (where they are,
    /// their hearts, what they carry). Two copies of a run with the same fingerprint are, for play, the same.
    /// </summary>
    public static ulong Fingerprint(GameState state)
    {
        ulong hash = 14695981039346656037UL;
        void Mix(long value) => hash = (hash ^ (ulong)value) * 1099511628211UL;
        void MixText(string text)
        {
            foreach (char letter in text)
            {
                Mix(letter);
            }
        }

        Mix(state.Round);
        Mix((int)state.Phase);
        Mix(state.Turn?.HeroId ?? -1);
        Mix(state.Turn?.MovementPoints ?? -1);
        foreach (Hero hero in state.Heroes)
        {
            Mix(hero.Id);
            MixText(hero.RoomId);
            Mix(hero.Position.X);
            Mix(hero.Position.Y);
            Mix(hero.Hearts);
            Mix((int)hero.Status);
            Mix(hero.Coins);
            Mix(hero.Keys);
            Mix(hero.FireDice);
            Mix(hero.OwnerId ?? -1);
            Mix(hero.AllCards.Sum(card => (long)card.Id * card.Count));
        }

        foreach ((string id, Room room) in state.Rooms.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            MixText(id);
            foreach (Monster monster in room.Monsters)
            {
                Mix(monster.Id);
                Mix(monster.Position.X);
                Mix(monster.Position.Y);
                Mix(monster.Hearts);
            }
        }

        return hash;
    }
}

/// <summary>
/// Who plays which seat: each seat of the run belongs to a peer (a machine at the table, by its number). A companion
/// goes with whoever it follows, wherever it sits. Several seats can belong to one peer (friends sharing a laptop).
/// </summary>
public sealed class SeatMap
{
    private readonly Dictionary<int, int> _peerBySeat;

    /// <param name="peerBySeat">For each player's seat, the peer that plays it.</param>
    public SeatMap(IReadOnlyDictionary<int, int> peerBySeat) => _peerBySeat = new Dictionary<int, int>(peerBySeat);

    public IReadOnlyDictionary<int, int> PeerBySeat => _peerBySeat;

    /// <summary>The peer that plays this hero: the one whose seat it is, or (a companion, a summons) whose seat its leader has.</summary>
    public int? PeerOf(GameState state, Hero hero)
    {
        Hero seated = hero;
        for (int guard = 0; guard < 8 && seated.OwnerId is int owner; guard++)
        {
            seated = state.GetHero(owner);
        }

        return _peerBySeat.TryGetValue(seated.Seat, out int peer) ? peer : null;
    }

    /// <summary>Whether <paramref name="peer"/> may make moves for the hero with this id.</summary>
    public bool Controls(int peer, GameState state, int heroId) =>
        state.Heroes.FirstOrDefault(hero => hero.Id == heroId) is { } hero && PeerOf(state, hero) == peer;

    /// <summary>A peer left: their seats pass to <paramref name="to"/> (the host), so the run can go on.</summary>
    public void Reassign(int from, int to)
    {
        foreach (int seat in _peerBySeat.Where(entry => entry.Value == from).Select(entry => entry.Key).ToList())
        {
            _peerBySeat[seat] = to;
        }
    }
}

/// <summary>A move the host accepted: its number in the run (from 0), the move, and the run's fingerprint after it.</summary>
public sealed record AcceptedMove(int Index, GameCommand Command, ulong Fingerprint);

/// <summary>The host's side: it holds the run, and decides what happens.</summary>
public sealed class RunHost(GameSession session, SeatMap seats, SavedRun record)
{
    public GameSession Session { get; } = session;

    public SeatMap Seats { get; } = seats;

    /// <summary>The run's start and every move accepted so far: what a late (or returning) player is sent.</summary>
    public SavedRun Record { get; } = record;

    /// <summary>
    /// A move from <paramref name="peer"/>: refused (with why) if the hero isn't theirs to move or the rules won't have
    /// it; else played, recorded, and returned to pass on to everyone, with what happened.
    /// </summary>
    public (AcceptedMove? Move, IReadOnlyList<Events.GameEvent> Events, string? Refusal) Submit(int peer, GameCommand command)
    {
        if (!Seats.Controls(peer, Session.State, command.HeroId))
        {
            return (null, [], "That hero isn't yours to move.");
        }

        CommandResult result = Session.Execute(command);
        if (!result.IsAccepted)
        {
            return (null, [], result.Error);
        }

        Record.Record(command, Session.State);
        return (new AcceptedMove(Record.Commands.Count - 1, command, RunWire.Fingerprint(Session.State)), result.Events, null);
    }
}

/// <summary>A copy of the run, kept by everyone but the host: it plays the moves the host passes on, in order.</summary>
public sealed class RunReplica(GameSession session, int movesPlayed = 0)
{
    public GameSession Session { get; } = session;

    /// <summary>How many of the run's moves this copy has played.</summary>
    public int MovesPlayed { get; private set; } = movesPlayed;

    /// <summary>
    /// Plays the host's next move on this copy, and says what happened.
    /// </summary>
    /// <exception cref="RunOutOfStepException">It isn't the next move, this copy refuses it, or the copies no longer match.</exception>
    public IReadOnlyList<Events.GameEvent> Apply(AcceptedMove move)
    {
        if (move.Index != MovesPlayed)
        {
            throw new RunOutOfStepException($"Move {move.Index + 1} came when move {MovesPlayed + 1} was due.");
        }

        CommandResult result = Session.Execute(move.Command);
        if (!result.IsAccepted)
        {
            throw new RunOutOfStepException($"Move {move.Index + 1} ({move.Command.GetType().Name}) was refused here: {result.Error}");
        }

        MovesPlayed++;
        if (RunWire.Fingerprint(Session.State) != move.Fingerprint)
        {
            throw new RunOutOfStepException($"After move {move.Index + 1} ({move.Command.GetType().Name}) this copy of the run no longer matches the host's.");
        }

        return result.Events;
    }
}

/// <summary>A copy of a networked run fell out of step with the host's: it needs sending the run afresh.</summary>
public sealed class RunOutOfStepException(string message) : Exception(message);
