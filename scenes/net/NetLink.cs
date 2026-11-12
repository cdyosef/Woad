using System.Text.Json;
using Godot;

namespace Woad.Scenes.Net;

/// <summary>Someone at the table: their peer number, the name they gave, and the party they've brought (as its heroes' names).</summary>
public sealed record TablePlayer(int Peer, string Name, IReadOnlyList<string> Party);

/// <summary>
/// The connection for playing online: hosting a table or joining one (Godot's ENet, UDP), and the messages that cross
/// it, all text. Before a run: who's at the table, and the party each has brought. In a run: a player sends the host a
/// move; the host passes every move it accepts on to everyone, numbered, with the run's fingerprint after it. It knows
/// nothing of the rules: the lobby and the game screen listen to it and tell it what to send.
/// </summary>
public partial class NetLink : Node
{
    public const int DefaultPort = 7777;

    /// <summary>
    /// This copy of the game, as the table checks it: its version's name and its rules' build. Two copies can only
    /// play together if theirs match (a run is played through the rules on every machine).
    /// </summary>
    public static string GameVersion { get; } =
        OS.GetEnvironment("WOAD_FAKE_VERSION") is { Length: > 0 } fake ? fake
        : $"{ProjectSettings.GetSetting("application/config/version").AsString()} ({typeof(Woad.Core.Rules.GameSession).Assembly.ManifestModule.ModuleVersionId.ToString()[..8]})";

    /// <summary>The host's peer number (Godot's server is always 1).</summary>
    public const int HostPeer = 1;

    private readonly Dictionary<int, TablePlayer> _players = [];
    private readonly Dictionary<int, string> _parties = [];
    private (string Heroes, string[] Names)? _myOffer;

    public bool IsOnline { get; private set; }

    public bool IsHost { get; private set; }

    public int MyPeer => IsOnline ? Multiplayer.GetUniqueId() : 0;

    public string MyName { get; private set; } = "";

    /// <summary>Everyone at the table, the host first.</summary>
    public IReadOnlyList<TablePlayer> Players => [.. _players.Values.OrderBy(player => player.Peer == HostPeer ? 0 : 1).ThenBy(player => player.Peer)];

    /// <summary>On the host: each peer's party as it was sent (the heroes, as text).</summary>
    public IReadOnlyDictionary<int, string> Parties => _parties;

    /// <summary>Something to tell the player about the connection.</summary>
    public event Action<string>? StatusChanged;

    /// <summary>Who's at the table, or what they've brought, changed.</summary>
    public event Action? TableChanged;

    /// <summary>The host began a run: the run (a saved run's text) and who plays which seat (seat to peer, as text).</summary>
    public event Action<string, string>? RunStarted;

    /// <summary>On the host: a player sent a move (their peer, the move).</summary>
    public event Action<int, string>? MoveSubmitted;

    /// <summary>The host passed on a move it accepted: its number, the move, the run's fingerprint after it.</summary>
    public event Action<int, string, string>? MoveArrived;

    /// <summary>The host refused the move this player sent, and why.</summary>
    public event Action<string>? MoveRefused;

    /// <summary>Someone left the table (their peer, their name).</summary>
    public event Action<int, string>? PlayerLeft;

    /// <summary>The table is gone: the host left, the connection dropped, or the host turned this copy away.</summary>
    public event Action<string>? Disconnected;

    /// <summary>Something was said at the table, as a hero: the hero's id, the words, the language's id.</summary>
    public event Action<int, string, string>? ChatArrived;

    /// <summary>On the host: may this peer speak as this hero? (Set by the game while a run's on.)</summary>
    public Func<int, int, bool>? MaySpeakAs { get; set; }

    /// <summary>On the host: someone sat down at the table (their peer, their name), perhaps while a run is on.</summary>
    public event Action<int, string>? PlayerArrived;

    /// <summary>A run under way was sent to this player, joining (or rejoining) it: the run so far and the seats, as text.</summary>
    public event Action<string, string>? RunJoined;

    public override void _Ready()
    {
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += () => Drop("Couldn't reach the host.");
        Multiplayer.ServerDisconnected += () => Drop("The host has gone.");
    }

    /// <summary>Hosts a table on this machine. Returns why it couldn't, or null.</summary>
    public string? Host(int port, string name)
    {
        Leave();
        var peer = new ENetMultiplayerPeer();
        Error error = peer.CreateServer(port, maxClients: 7);
        if (error != Error.Ok)
        {
            return $"Couldn't host on port {port} ({error}). Is another game already hosting there?";
        }

        Multiplayer.MultiplayerPeer = peer;
        IsOnline = IsHost = true;
        MyName = name;
        _players[HostPeer] = new TablePlayer(HostPeer, name, []);
        StatusChanged?.Invoke($"Hosting on port {port}. Friends on your network join with this machine's address.");
        TableChanged?.Invoke();
        return null;
    }

    /// <summary>Joins the table hosted at an address. Returns why it couldn't even try, or null (then <see cref="StatusChanged"/> or <see cref="Disconnected"/> says how it went).</summary>
    public string? Join(string address, int port, string name)
    {
        Leave();
        var peer = new ENetMultiplayerPeer();
        Error error = peer.CreateClient(address, port);
        if (error != Error.Ok)
        {
            return $"Couldn't connect to {address}:{port} ({error}).";
        }

        Multiplayer.MultiplayerPeer = peer;
        IsOnline = true;
        IsHost = false;
        MyName = name;
        StatusChanged?.Invoke($"Looking for the table at {address}:{port}...");
        return null;
    }

    /// <summary>Leaves the table (closing it, for the host).</summary>
    public void Leave()
    {
        if (!IsOnline)
        {
            return;
        }

        Multiplayer.MultiplayerPeer?.Close();
        Multiplayer.MultiplayerPeer = null;
        IsOnline = IsHost = false;
        _myOffer = null;
        _players.Clear();
        _parties.Clear();
        TableChanged?.Invoke();
    }

    private void Drop(string why)
    {
        Leave();
        Disconnected?.Invoke(why);
    }

    private void OnConnectedToServer()
    {
        StatusChanged?.Invoke("At the table.");
        RpcId(HostPeer, MethodName.Hello, MyName, GameVersion);
        // A party picked before the table was reached is brought along.
        if (_myOffer is var (heroes, names))
        {
            RpcId(HostPeer, MethodName.OfferParty, heroes, names);
        }
    }

    private void OnPeerConnected(long id)
    {
    }

    private void OnPeerDisconnected(long id)
    {
        if (!IsHost || !_players.Remove((int)id, out TablePlayer? gone))
        {
            return;
        }

        _parties.Remove((int)id);
        SendTable();
        Rpc(MethodName.Left, (int)id, gone.Name);
        PlayerLeft?.Invoke((int)id, gone.Name);
    }

    // ---- The table, before a run ---------------------------------------------------------------

    /// <summary>The party this player brings: its heroes (as text, for the host) and their names (for everyone).</summary>
    public void Offer(string heroesJson, IReadOnlyList<string> names)
    {
        _myOffer = (heroesJson, [.. names]);
        if (IsHost)
        {
            Offered(HostPeer, heroesJson, [.. names]);
        }
        else if (IsOnline)
        {
            RpcId(HostPeer, MethodName.OfferParty, heroesJson, names.ToArray());
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Hello(string name, string version)
    {
        if (!IsHost)
        {
            return;
        }

        int peer = Multiplayer.GetRemoteSenderId();
        if (version != GameVersion)
        {
            // Different rules would fall out of step at once: turned away, with why.
            RpcId(peer, MethodName.TurnedAway, $"The host plays version {GameVersion}, and you have {version}. Update to the same version to join.");
            // They leave on hearing it; if they don't, the host lets them go a moment later (once the message is out).
            GetTree().CreateTimer(2).Timeout += () => (Multiplayer.MultiplayerPeer as ENetMultiplayerPeer)?.DisconnectPeer(peer);
            return;
        }

        name = name.Length > 24 ? name[..24] : name;
        _players[peer] = new TablePlayer(peer, name, []);
        SendTable();
        PlayerArrived?.Invoke(peer, name);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TurnedAway(string why) => Drop(why);

    /// <summary>Says something at the table, as one of this player's heroes.</summary>
    public void SendChat(int heroId, string text, string languageId)
    {
        text = text.Length > 120 ? text[..120] : text;
        if (IsHost)
        {
            Said(HostPeer, heroId, text, languageId);
        }
        else if (IsOnline)
        {
            RpcId(HostPeer, MethodName.Speak, heroId, text, languageId);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Speak(int heroId, string text, string languageId)
    {
        if (IsHost)
        {
            Said(Multiplayer.GetRemoteSenderId(), heroId, text.Length > 120 ? text[..120] : text, languageId);
        }
    }

    private void Said(int peer, int heroId, string text, string languageId)
    {
        if (MaySpeakAs is { } may && !may(peer, heroId))
        {
            return;
        }

        Rpc(MethodName.Heard, heroId, text, languageId);
        ChatArrived?.Invoke(heroId, text, languageId);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Heard(int heroId, string text, string languageId) => ChatArrived?.Invoke(heroId, text, languageId);

    /// <summary>The host sends a run under way to someone (re)joining it: the run so far, and the seats.</summary>
    public void SendRun(int peer, string runJson, string seatsJson)
    {
        if (IsHost)
        {
            RpcId(peer, MethodName.Joined, runJson, seatsJson);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Joined(string runJson, string seatsJson) => RunJoined?.Invoke(runJson, seatsJson);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void OfferParty(string heroesJson, string[] names)
    {
        if (IsHost)
        {
            Offered(Multiplayer.GetRemoteSenderId(), heroesJson, names);
        }
    }

    private void Offered(int peer, string heroesJson, string[] names)
    {
        if (!_players.TryGetValue(peer, out TablePlayer? player))
        {
            return;
        }

        _players[peer] = player with { Party = names };
        _parties[peer] = heroesJson;
        SendTable();
    }

    private void SendTable()
    {
        Rpc(MethodName.Table, JsonSerializer.Serialize(Players));
        TableChanged?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Table(string json)
    {
        _players.Clear();
        foreach (TablePlayer player in JsonSerializer.Deserialize<List<TablePlayer>>(json) ?? [])
        {
            _players[player.Peer] = player;
        }

        TableChanged?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Left(int peer, string name) => PlayerLeft?.Invoke(peer, name);

    // ---- A run ---------------------------------------------------------------------------------

    /// <summary>The host begins a run for everyone at the table.</summary>
    public void StartRun(string runJson, string seatsJson)
    {
        if (!IsHost)
        {
            return;
        }

        Rpc(MethodName.Started, runJson, seatsJson);
        RunStarted?.Invoke(runJson, seatsJson);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Started(string runJson, string seatsJson) => RunStarted?.Invoke(runJson, seatsJson);

    /// <summary>A player sends the host a move.</summary>
    public void SubmitMove(string moveJson)
    {
        if (IsOnline && !IsHost)
        {
            RpcId(HostPeer, MethodName.Submit, moveJson);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Submit(string moveJson)
    {
        if (IsHost)
        {
            MoveSubmitted?.Invoke(Multiplayer.GetRemoteSenderId(), moveJson);
        }
    }

    /// <summary>The host passes a move it accepted on to everyone else.</summary>
    public void BroadcastMove(int index, string moveJson, ulong fingerprint)
    {
        if (IsHost)
        {
            Rpc(MethodName.Move, index, moveJson, fingerprint.ToString());
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Move(int index, string moveJson, string fingerprint) => MoveArrived?.Invoke(index, moveJson, fingerprint);

    /// <summary>The host tells a player their move was refused.</summary>
    public void Refuse(int peer, string why)
    {
        if (IsHost && peer != HostPeer)
        {
            RpcId(peer, MethodName.Refused, why);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Refused(string why) => MoveRefused?.Invoke(why);
}
