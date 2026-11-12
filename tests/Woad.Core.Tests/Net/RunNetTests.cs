using Woad.Core.Bots;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Net;
using Woad.Core.Rules;
using Woad.Core.Saves;

namespace Woad.Core.Tests.Net;

/// <summary>A run played over a network: the host decides, the copies follow, and a copy out of step is caught.</summary>
public sealed class RunNetTests
{
    private static readonly ContentCatalog Content = ContentCatalog.LoadDefault();

    private const int HostPeer = 1;
    private const int GuestPeer = 2;

    /// <summary>A two-hero run in the Emerald Keep: the host plays seat 0, a guest seat 1. The host's side, and a guest's copy made from what the host would send.</summary>
    private static (RunHost Host, RunReplica Guest) Table(ulong seed = 11)
    {
        Stats stats = GameRules.HeroBaseStats with { Hearts = 12 };
        var start = new SavedRun
        {
            ThemeId = "emerald_keep",
            Seed = seed,
            Heroes = [new HeroSetup("Aria", stats, ["wooden_sword"]), new HeroSetup("Bran", stats, ["wooden_sword"])],
        };
        var host = new RunHost(start.Restore(Content), new SeatMap(new Dictionary<int, int> { [0] = HostPeer, [1] = GuestPeer }), start);
        SavedRun sent = SavedRun.FromJson(start.ToJson());
        return (host, new RunReplica(sent.Restore(Content), sent.Commands.Count));
    }

    private static int PeerToMove(RunHost host) => host.Seats.PeerOf(host.Session.State, host.Session.State.ActiveHero!)!.Value;

    [Fact]
    public void TheCopiesPlayTheHostsMovesAndStayTheSame()
    {
        (RunHost host, RunReplica guest) = Table();

        // The bot plays both heroes through the host; each accepted move goes over the wire as text.
        for (int move = 0; move < 150 && host.Session.State.Phase == RunPhase.InProgress; move++)
        {
            GameCommand command = SimpleBot.NextCommand(host.Session);
            (AcceptedMove? accepted, _, string? refusal) = host.Submit(PeerToMove(host), command);
            Assert.True(accepted is not null, $"move {move + 1} ({command.GetType().Name}) refused: {refusal}");

            guest.Apply(accepted! with { Command = RunWire.Read(RunWire.Write(accepted.Command)) });
            Assert.Equal(RunWire.Fingerprint(host.Session.State), RunWire.Fingerprint(guest.Session.State));
        }

        Assert.True(guest.MovesPlayed >= 20, $"only {guest.MovesPlayed} moves were played");
        Assert.Equal(host.Session.State.Round, guest.Session.State.Round);
    }

    [Fact]
    public void TheHostRefusesAMoveForAHeroThatIsntTheSendersToMove()
    {
        (RunHost host, _) = Table();
        Hero aria = host.Session.State.Heroes[0];

        (AcceptedMove? accepted, _, string? refusal) = host.Submit(GuestPeer, new EndTurn(aria.Id));

        Assert.Null(accepted);
        Assert.Contains("isn't yours", refusal);
        Assert.NotNull(host.Submit(HostPeer, new EndTurn(aria.Id)).Move);
    }

    [Fact]
    public void ACompanionGoesWithWhoeverItFollows()
    {
        Stats stats = GameRules.HeroBaseStats with { Hearts = 12 };
        var start = new SavedRun
        {
            ThemeId = "emerald_keep",
            Seed = 5,
            Heroes = [new HeroSetup("Aria", stats, []), new HeroSetup("Bran", stats, []), new HeroSetup("Pip", stats, [], OwnerSeat: 1)],
        };
        var seats = new SeatMap(new Dictionary<int, int> { [0] = HostPeer, [1] = GuestPeer });
        GameState state = start.Restore(Content).State;

        Assert.Equal(GuestPeer, seats.PeerOf(state, state.Heroes[2]));
        Assert.True(seats.Controls(GuestPeer, state, state.Heroes[2].Id));

        // The guest drops out: their seats pass to the host.
        seats.Reassign(GuestPeer, HostPeer);
        Assert.True(seats.Controls(HostPeer, state, state.Heroes[2].Id));
    }

    [Fact]
    public void ACopyOutOfStepIsCaught()
    {
        (RunHost host, RunReplica guest) = Table();
        AcceptedMove first = host.Submit(PeerToMove(host), SimpleBot.NextCommand(host.Session)).Move!;
        AcceptedMove second = host.Submit(PeerToMove(host), SimpleBot.NextCommand(host.Session)).Move!;

        Assert.Contains("was due", Assert.Throws<RunOutOfStepException>(() => guest.Apply(second)).Message);
        Assert.Contains("no longer matches", Assert.Throws<RunOutOfStepException>(() => guest.Apply(first with { Fingerprint = first.Fingerprint + 1 })).Message);
    }

    [Fact]
    public void SomeoneJoiningLateIsSentTheRunSoFarAndCarriesOn()
    {
        (RunHost host, _) = Table();
        for (int move = 0; move < 12; move++)
        {
            Assert.NotNull(host.Submit(PeerToMove(host), SimpleBot.NextCommand(host.Session)).Move);
        }

        // What the host sends: the run's start and its moves so far.
        SavedRun sent = SavedRun.FromJson(host.Record.ToJson());
        var late = new RunReplica(sent.Restore(Content), sent.Commands.Count);
        Assert.Equal(RunWire.Fingerprint(host.Session.State), RunWire.Fingerprint(late.Session.State));

        late.Apply(host.Submit(PeerToMove(host), SimpleBot.NextCommand(host.Session)).Move!);
        Assert.Equal(13, late.MovesPlayed);
    }
}
