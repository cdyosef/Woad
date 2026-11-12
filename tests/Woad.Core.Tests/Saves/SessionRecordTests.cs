using Woad.Core.Bots;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Net;
using Woad.Core.Rules;
using Woad.Core.Saves;

namespace Woad.Core.Tests.Saves;

/// <summary>A session at the table, recorded to watch again.</summary>
public sealed class SessionRecordTests
{
    private static readonly ContentCatalog Content = ContentCatalog.LoadDefault();

    private static (SessionRecord Record, GameSession Played) Played(int moves)
    {
        Stats stats = GameRules.HeroBaseStats with { Hearts = 12 };
        var run = new SavedRun { ThemeId = "emerald_keep", Seed = 3, Heroes = [new HeroSetup("Aria", stats, ["wooden_sword"]), new HeroSetup("Bran", stats, ["wooden_sword"])] };
        var record = new SessionRecord { GameVersion = "test", Run = run.ToJson(), Players = ["Host", "Guest"] };
        GameSession session = run.Restore(Content);
        for (int i = 0; i < moves && session.State.Phase == RunPhase.InProgress; i++)
        {
            var command = SimpleBot.NextCommand(session);
            Assert.True(session.Execute(command).IsAccepted);
            run.Record(command, session.State);
            record.Record(run, session.State.Turn?.HeroId == session.State.Heroes[0].Id ? "Host" : "Guest");
            if (i == 4)
            {
                record.Note(SessionNoteKind.Chat, "Guest", i + 1, session.State.Heroes[1].Id, "Behind you!", "common");
            }
        }

        return (SessionRecord.FromJson(record.ToJson()), session);
    }

    [Fact]
    public void AReplayPlaysTheSessionBackToWhereItWas()
    {
        (SessionRecord record, GameSession played) = Played(80);

        Assert.True(record.Moves.Count >= 30, $"only {record.Moves.Count} moves");
        Assert.Equal(record.Commands().Count, record.Moves.Count);
        Assert.Equal(new SessionNote(5, record.Notes[0].Utc, SessionNoteKind.Chat, "Guest", played.State.Heroes[1].Id, "Behind you!", "common"), Assert.Single(record.Notes));
        Assert.Equal(RunWire.Fingerprint(played.State), RunWire.Fingerprint(record.PlayTo(Content, record.Moves.Count).State));
    }

    [Fact]
    public void AReplayCanStartFromAnyRound()
    {
        (SessionRecord record, _) = Played(80);
        IReadOnlyDictionary<int, int> rounds = record.RoundStarts(Content);

        Assert.True(rounds.Count >= 3, $"only {rounds.Count} rounds");
        int third = rounds.Keys.Order().ElementAt(2);
        Assert.Equal(third, record.PlayTo(Content, rounds[third]).State.Round);
        Assert.Equal(third - 1, record.PlayTo(Content, rounds[third] - 1).State.Round);
    }
}
