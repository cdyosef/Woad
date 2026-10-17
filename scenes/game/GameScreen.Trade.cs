using Godot;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>Trading: clicking an ally beside you opens the <see cref="TradePanel"/>; it follows each command and closes once they're out of reach.</summary>
public partial class GameScreen
{
    private TradePanel _trade = null!;

    private void BuildTradeWindow()
    {
        _trade = new TradePanel { Position = BoardOrigin };
        _trade.CommandRequested += factory => ExecuteForActiveHero(heroId => factory(heroId));
        _trade.CountRequested += (question, max, then) =>
        {
            if (max <= 1)
            {
                then(1);
            }
            else
            {
                _countPrompt.Ask(question, max, then);
            }
        };
        _trade.LootRequested += () =>
        {
            if (ActiveHero is { } hero && _trade.PartnerId is int id && _session.State.Heroes.FirstOrDefault(other => other.Id == id) is { } partner)
            {
                CloseTrade();
                Rummage(hero, ShownRoom, partner.Position);
            }
        };
        _trade.CloseRequested += CloseTrade;
        AddChild(_trade);
    }

    private void OpenTrade(Hero hero, Hero partner)
    {
        CloseRummage();
        _trade.ShowTrade(hero, partner, ShownRoom.HasLootAt(partner.Position));
    }

    private void RefreshTrade()
    {
        if (_trade.PartnerId is not int id)
        {
            return;
        }

        if (ActiveHero is not { IsCompanion: false } hero || _session.GetTradePartners().FirstOrDefault(other => other.Id == id) is not { } partner)
        {
            CloseTrade();
            return;
        }

        _trade.ShowTrade(hero, partner, ShownRoom.HasLootAt(partner.Position));
    }

    private void CloseTrade()
    {
        _trade.Close();
        _countPrompt.Visible = false;
    }
}
