using Godot;
using Woad.Core.Commands;
using Woad.Core.Model;
using Woad.Core.Net;
using Woad.Scenes.Net;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>A run being played online: the connection, who plays which seat, and how many moves the run had when this machine took it up (0 from the start; more, joining one under way).</summary>
public sealed record NetRun(NetLink Link, SeatMap Seats, int StartMoves = 0)
{
    public bool IsHost => Link.IsHost;

    /// <summary>Whether the hero is this machine's to move.</summary>
    public bool Mine(GameState state, int heroId) => Seats.Controls(Link.MyPeer, state, heroId);
}

/// <summary>
/// Playing online. On the host the run is played as ever, and every move the rules accept is passed on to everyone
/// (numbered, with the run's fingerprint); a move a player sends is played here if the hero is theirs to move, and
/// refused if not (or if the rules won't have it). Everywhere else nothing is played until the host passes it on: this
/// machine's own moves are sent to the host, and the host's moves are played in order as they arrive (waiting for the
/// one before to finish showing). Only the heroes in this machine's seats can be played from it; the game plays
/// companions on the host alone.
/// </summary>
public partial class GameScreen
{
    /// <summary>The run's connection, when it's played online; null at one machine.</summary>
    public NetRun? Net { get; init; }

    private readonly Queue<(int Peer, GameCommand Command)> _netSubmitted = new();
    private readonly Queue<AcceptedMove> _netArrived = new();
    private int _netMoves;
    private bool _netApplying;
    private bool _netPumping;
    private string? _lastRefusal;

    /// <summary>Whether this machine's copy of the run fell out of step with the host's.</summary>
    internal bool NetOutOfStep { get; private set; }

    /// <summary>How many of the run's moves have been played here.</summary>
    internal int NetMoves => _netMoves;

    private void NetReady()
    {
        if (Net is not { } net)
        {
            return;
        }

        _netMoves = net.StartMoves;
        net.Link.MoveSubmitted += OnMoveSubmitted;
        net.Link.ChatArrived += OnChatArrived;
        net.Link.MoveArrived += OnMoveArrived;
        net.Link.MoveRefused += OnMoveRefused;
        net.Link.PlayerLeft += OnPlayerLeft;
        net.Link.Disconnected += OnNetDisconnected;
        TreeExiting += () =>
        {
            net.Link.MoveSubmitted -= OnMoveSubmitted;
            net.Link.ChatArrived -= OnChatArrived;
            net.Link.MoveArrived -= OnMoveArrived;
            net.Link.MoveRefused -= OnMoveRefused;
            net.Link.PlayerLeft -= OnPlayerLeft;
            net.Link.Disconnected -= OnNetDisconnected;
        };
    }

    private void OnMoveSubmitted(int peer, string json)
    {
        try
        {
            _netSubmitted.Enqueue((peer, RunWire.Read(json)));
        }
        catch (InvalidDataException exception)
        {
            Net!.Link.Refuse(peer, exception.Message);
        }
    }

    private void OnMoveArrived(int index, string json, string fingerprint)
    {
        try
        {
            _netArrived.Enqueue(new AcceptedMove(index, RunWire.Read(json), ulong.Parse(fingerprint)));
        }
        catch (Exception exception) when (exception is InvalidDataException or FormatException)
        {
            OutOfStep($"a move from the host couldn't be read ({exception.Message})");
        }
    }

    private void OnMoveRefused(string why) => Log(why, Palette.Danger);

    private void OnPlayerLeft(int peer, string name)
    {
        // Their heroes pass to the host, so the run can go on (until they're back).
        Net!.Seats.Reassign(peer, NetLink.HostPeer);
        Log($"{name} left the table: the host plays their heroes until they're back.", Palette.TextDim);
        RefreshPanels();
    }

    /// <summary>Someone took up their seats again (or sat down to watch): the panels follow who plays what now.</summary>
    internal void NetSeatsChanged(string name, bool rejoined)
    {
        Log(rejoined ? $"{name} is back at the table, and takes up their heroes again." : $"{name} has come to watch.", Palette.TrimBright);
        RefreshPanels();
    }

    private void OnNetDisconnected(string why)
    {
        Log($"{why} The run can't go on from here.", Palette.Danger);
        NetOutOfStep = true;
        RefreshPanels();
    }

    private void OutOfStep(string why)
    {
        NetOutOfStep = true;
        _netArrived.Clear();
        Log($"This game fell out of step with the host's: {why}.", Palette.Danger);
        GD.PushError($"Net: out of step: {why}");
    }

    /// <summary>
    /// For the tests: the bot picks the run's next move; if the hero it's for is this machine's, it's played (through
    /// the host), and this waits for it to come back. True if a move was played.
    /// </summary>
    internal async Task<bool> NetBotStep()
    {
        if (Net is null || _busy || _netPumping || _netArrived.Count > 0 || _netSubmitted.Count > 0 || NetOutOfStep || _session.State.Phase != RunPhase.InProgress || _session.State.Turn is null)
        {
            return false;
        }

        GameCommand command = Woad.Core.Bots.SimpleBot.NextCommand(_session);
        if (!Net.Mine(_session.State, command.HeroId))
        {
            return false;
        }

        int before = _netMoves;
        await ExecuteAsync(command);
        for (int frames = 0; frames < 900 && _netMoves == before && !NetOutOfStep; frames++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        return _netMoves > before;
    }

    public override void _Process(double delta)
    {
        if (Replay is not null)
        {
            ReplayNext();
            return;
        }

        if (Net is null || _netPumping || _busy || NetOutOfStep || (_netSubmitted.Count == 0 && _netArrived.Count == 0))
        {
            return;
        }

        PumpNet();
    }

    /// <summary>Plays the next move waiting from the network, now that nothing else is being shown.</summary>
    private async void PumpNet()
    {
        _netPumping = true;
        try
        {
            if (Net!.IsHost && _netSubmitted.TryDequeue(out (int Peer, GameCommand Command) sent))
            {
                if (!Net.Seats.Controls(sent.Peer, _session.State, sent.Command.HeroId))
                {
                    Net.Link.Refuse(sent.Peer, "That hero isn't yours to move.");
                }
                else if (!await ExecuteAsync(sent.Command))
                {
                    Net.Link.Refuse(sent.Peer, _lastRefusal ?? "The host couldn't play that just then: try again.");
                }
            }
            else if (!Net.IsHost && _netArrived.TryDequeue(out AcceptedMove? move))
            {
                if (move.Index != _netMoves)
                {
                    OutOfStep($"move {move.Index + 1} came when move {_netMoves + 1} was due");
                    return;
                }

                _netApplying = true;
                bool played = await ExecuteAsync(move.Command);
                _netApplying = false;
                if (!played)
                {
                    OutOfStep($"move {move.Index + 1} ({move.Command.GetType().Name}) was refused here: {_lastRefusal}");
                }
                else if (RunWire.Fingerprint(_session.State) != move.Fingerprint)
                {
                    OutOfStep($"after move {move.Index + 1} ({move.Command.GetType().Name}) the two games no longer match");
                }
            }
        }
        finally
        {
            _netApplying = false;
            _netPumping = false;
        }
    }
}
