using Godot;
using Woad.Core.Commands;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Scenes.Ui;
using static Woad.Scenes.Debug.UiDriver;

namespace Woad.Scenes.Game;

/// <summary>
/// Debug mode (<c>-- --bathstest</c>, with <see cref="DemoRun.Baths"/>): Aria, at the edge of Leviathan Jr.'s pool,
/// fights it with every Rumble die each turn until it falls; it floats belly-up, a raft over the water.
/// </summary>
public partial class GameScreen
{
    private async void RunBathsTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            Room room = ShownRoom;
            Monster leviathan = room.Monsters.Single();
            Check(leviathan.Definition.Name == "Leviathan Jr." && room.Layout[leviathan.Position] == TileKind.DeepWater, "Leviathan Jr. waits in the deep");
            await SaveShot("baths_start");

            // Aria says something in Primordial: nobody else is there to understand, so it stays in runes.
            OpenChat();
            Check(Chatting && _chatLanguage.ItemCount == 2, "Enter opens the chat box, with Common and Primordial to pick from");
            _chatLanguage.Select(1);
            SendChat("Hello there big fellow");
            Check(!Chatting && HasBubble(aria.Id), "her words show in a bubble over her");
            await Pause(0.4);
            await SaveShot("baths_chat");

            for (int round = 0; round < 30 && room.FindMonster(leviathan.Id) is not null && aria.IsActive; round++)
            {
                while (_session.State.Turn?.HeroId == aria.Id && _session.GetAttackableMonsters(null).Any(monster => monster.Id == leviathan.Id) && _session.AttackDiceLeft > 0)
                {
                    await ExecuteAsync(new AttackMonster(aria.Id, leviathan.Id, Dice: 1));
                    await WaitUntilIdle();
                    while (_session.State.Turn is { } turn && (turn.PendingRoll is not null || turn.UnsettledPowerDice > 0))
                    {
                        await ExecuteAsync(turn.UnsettledPowerDice > 0 ? new BankPowerDie(aria.Id) : new ConfirmAttack(aria.Id));
                        await WaitUntilIdle();
                    }
                }

                if (room.FindMonster(leviathan.Id) is null)
                {
                    break;
                }

                if (aria.GripTotal > 0)
                {
                    await SaveShot("baths_snatched");
                }

                await Click(this, _endTurn);
                await WaitUntilIdle();
                if (round == 0)
                {
                    await SaveShot("baths_speech");
                    Check(HasBubble(leviathan.Id), "Leviathan Jr. grumbles in a bubble");
                }
            }

            Check(room.FindMonster(leviathan.Id) is null, "Leviathan Jr. falls");
            Check(room.Carcasses.Count == 1 && room.Carcasses[0].Tiles.All(tile => room.Layout[tile] == TileKind.Floor), "it floats belly-up, a surface to stand on");
            await SaveShot("baths_carcass");

            GD.Print("Baths test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Baths test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }
}
