using Woad.Core.Grid;
using Woad.Core.Model;
using static Woad.Scenes.Debug.UiDriver;

namespace Woad.Scenes.Game;

/// <summary>The portal clicks, for the gate test (<see cref="DemoRun.Portals"/>): light Aria's portal with her banked Power Die.</summary>
public partial class GameScreen
{
    internal async Task CheckPortals()
    {
        Hero hero = ActiveHero!;
        Room near = _session.State.Rooms[hero.RoomId];
        GridPosition portal = near.TilesOfKind(TileKind.Portal).Single();
        Check(hero.Power.Banked == 1 && hero.Position.ChebyshevDistanceTo(portal) == 1, "Aria stands beside a dark portal with a banked Power Die");

        await ClickTile(portal);
        Check(_inventory.IsChoosingHand && _inventory.HandButton(0).Text == "Power Die", $"clicking the dark portal beside you asks for a die to channel into it ({_inventory.HandButton(0).Text})");
        await Click(this, _inventory.HandButton(0));
        await WaitUntilIdle();
        Check(near.PortalLit && hero.Power.Banked == 0, "the Power Die lights the portal");
        Check(_messages.Any(message => message.Contains("channels a die into the portal")), "the log tells of it");
        if (System.Environment.GetEnvironmentVariable("WOAD_SHOTS") is { } shots)
        {
            for (int frame = 0; frame < 20; frame++)
            {
                await NextFrame(this);
            }

            GetViewport().GetTexture().GetImage().SavePng($"{shots}/portal_lit.png");
        }

        await ClickTile(portal);
        Check(!_inventory.IsChoosingHand && _messages.Last().Contains("No other portal"),
            $"with no other portal lit, there's nowhere to go yet ({_messages.Last()})");
    }
}
