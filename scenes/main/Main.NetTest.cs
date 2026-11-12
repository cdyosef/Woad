using Godot;
using Woad.Core.Characters;
using Woad.Core.Model;
using Woad.Scenes.Game;
using Woad.Scenes.Net;
using Woad.Scenes.Ui;
using static Woad.Scenes.Debug.UiDriver;

namespace Woad.Scenes;

/// <summary>
/// Debug modes for playing online, two games on one machine. <c>-- --nethosttest</c> hosts a table from the lobby
/// with Aria, starts a second game (<c>-- --netguesttest</c>) that joins it with Bran, starts a run of the Emerald
/// Keep for both, and plays its own hero's turns while the guest plays theirs: every move goes through the host, and
/// both games must stay in step into the third round. The host passes when the guest's game says it did too.
/// </summary>
public partial class Main
{
    private const int NetTestPort = 7791;
    private const int NetTestRounds = 3;

    private async Task<LobbyScreen> NetTestLobby(string heroName)
    {
        Character hero = Character.Create(heroName, heroName == "Aria" ? "ember" : "woad", new StatAllocation { Hearts = 4 }, _content, DateTimeOffset.UtcNow);
        await _characters.SaveAsync(hero);
        ShowLobby();
        LobbyScreen lobby = await Screen<LobbyScreen>();
        await WaitFor(this, () => lobby.CharacterButton(heroName) is not null, "the lobby to list the saved character");
        lobby.NameEdit.Text = heroName == "Aria" ? "Host" : "Guest";
        return lobby;
    }

    /// <summary>Plays this machine's heroes (the bot picks their moves) until the run reaches the round, falls out of step, or ends.</summary>
    private async Task NetTestPlay(GameScreen game, Func<bool> done)
    {
        for (int steps = 0; steps < 4000 && !done() && !game.NetOutOfStep && game.Session.State.Phase == RunPhase.InProgress; steps++)
        {
            await game.NetBotStep();
            await NextFrame(this);
        }
    }

    private async void RunNetHostTest(string saveFolder)
    {
        int guest = -1;
        try
        {
            LobbyScreen lobby = await NetTestLobby("Aria");
            lobby.AddressEdit.Text = "127.0.0.1";
            SetNetTestPort(lobby);
            await Click(this, lobby.HostButton);
            Check(_net.IsHost && _net.Players.Count == 1, $"Host opens a table ({lobby.Status})");
            await Click(this, lobby.CharacterButton("Aria")!);
            Check(_net.Players[0].Party.SequenceEqual(["Aria"]) && lobby.StartButton.Visible, "the host brings Aria, and can start once everyone's ready");

            guest = OS.CreateProcess(OS.GetExecutablePath(), ["--headless", "--path", ProjectSettings.GlobalizePath("res://"), "--", "--netguesttest"]);
            Check(guest > 0, "a second game starts, to join");
            await WaitFor(this, () => _net.Players.Count == 2 && _net.Players.All(player => player.Party.Count == 1), "the guest to join the table with Bran", maxFrames: 3000);
            Check(_net.Players[1].Name == "Guest" && _net.Players[1].Party[0] == "Bran" && !lobby.StartButton.Disabled, "the guest sits down with Bran: the table's ready");

            if (System.Environment.GetEnvironmentVariable("WOAD_SHOTS") is { } shots)
            {
                await NextFrame(this);
                await NextFrame(this);
                GetViewport().GetTexture().GetImage().SavePng($"{shots}/lobby.png");
            }

            await Click(this, lobby.StartButton);
            GameScreen game = await Screen<GameScreen>();
            await game.WaitUntilIdle();
            Check(game.Session.State.Heroes.Select(hero => hero.Name).SequenceEqual(["Aria", "Bran"]), "the run begins with both heroes, the host's first");
            Check(game.Net!.Mine(game.Session.State, game.Session.State.Heroes[0].Id) && !game.Net.Mine(game.Session.State, game.Session.State.Heroes[1].Id), "Aria is the host's to move, Bran the guest's");

            await NetTestPlay(game, () => !OS.IsProcessRunning(guest));
            Check(!game.NetOutOfStep && game.Session.State.Round >= NetTestRounds, $"the run is played into round {NetTestRounds} ({game.Session.State.Round}, {game.NetMoves} moves)");
            await WaitFor(this, () => !OS.IsProcessRunning(guest), "the guest's game to finish", maxFrames: 3000);
            int verdict = OS.GetProcessExitCode(guest);
            guest = -1;
            Check(verdict == 0, $"the guest's game stayed in step and passed too (exit code {verdict})");

            GD.Print("Net test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Net test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
        finally
        {
            if (guest > 0 && OS.IsProcessRunning(guest))
            {
                OS.Kill(guest);
            }

            if (Directory.Exists(saveFolder))
            {
                Directory.Delete(saveFolder, recursive: true);
            }
        }
    }

    private async void RunNetGuestTest(string saveFolder)
    {
        try
        {
            LobbyScreen lobby = await NetTestLobby("Bran");
            lobby.AddressEdit.Text = "127.0.0.1";
            SetNetTestPort(lobby);
            await Click(this, lobby.CharacterButton("Bran")!);
            await Click(this, lobby.JoinButton);
            await WaitFor(this, () => _net.Players.Count == 2, "to reach the table", maxFrames: 3000);
            Check(!_net.IsHost && !lobby.StartButton.Visible, "the guest is at the table (and can't start the run)");

            GameScreen game = await Screen<GameScreen>();
            await game.WaitUntilIdle();
            Check(game.Net!.Mine(game.Session.State, game.Session.State.Heroes[1].Id), "Bran is the guest's to move");
            await NetTestPlay(game, () => game.Session.State.Round >= NetTestRounds);
            Check(!game.NetOutOfStep, "the guest's game stayed in step with the host's");
            Check(game.Session.State.Round >= NetTestRounds, $"and reached round {NetTestRounds} ({game.Session.State.Round}, {game.NetMoves} moves)");

            GD.Print("Net guest test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Net guest test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
        finally
        {
            if (Directory.Exists(saveFolder))
            {
                Directory.Delete(saveFolder, recursive: true);
            }
        }
    }

    private static void SetNetTestPort(LobbyScreen lobby) => lobby.PortEdit.Text = NetTestPort.ToString();
}
