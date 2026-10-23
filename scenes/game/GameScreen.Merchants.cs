using Godot;
using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// Merchants on the board: chained ones to free (a key, or a picked lock), freed ones to trade with
/// (a click beside them opens their wares), and their comings and goings as they follow the party.
/// </summary>
public partial class GameScreen
{
    private static readonly string[] MerchantLooks = ["noble", "bard", "sultan", "halfling", "dwarf", "monk"];

    private MerchantPanel _merchantPanel = null!;

    private void BuildMerchantWindow()
    {
        _merchantPanel = new MerchantPanel { Position = BoardOrigin + new Vector2(24, 20) };
        _merchantPanel.BuyRequested += cardId =>
        {
            if (ActiveHero is { } hero && _merchantPanel.MerchantId is int merchantId)
            {
                Execute(new BuyFromMerchant(hero.Id, merchantId, cardId));
            }
        };
        _merchantPanel.SellRequested += cardId =>
        {
            if (ActiveHero is { } hero && _merchantPanel.MerchantId is int merchantId)
            {
                Execute(new SellToMerchant(hero.Id, merchantId, cardId));
            }
        };
        _merchantPanel.CloseRequested += () => _merchantPanel.Close();
        AddChild(_merchantPanel);
    }

    /// <summary>Figures for the merchants in every room.</summary>
    private void LoadMerchantFigures()
    {
        foreach (Room room in _session.State.Rooms.Values)
        {
            foreach (Merchant merchant in room.Merchants)
            {
                _names[merchant.Id] = merchant.Name;
                _figures[merchant.Id] = new Figure
                {
                    Id = merchant.Id,
                    RoomId = room.Id,
                    Position = merchant.Position,
                    Hearts = merchant.Hearts,
                    MaxHearts = 1,
                    Glyph = merchant.Name[..1],
                    Fill = Palette.Loot,
                    Look = Looks.Resolve(new Appearance(MerchantLooks[merchant.Id % MerchantLooks.Length], Main: "gilt"), ""),
                    Chained = merchant.Chained,
                };
            }
        }
    }

    /// <summary>
    /// A click on a merchant: beside a chained one, free them (a key) or pick their chains' lock; beside a
    /// freed one, open their wares. Further off, say so.
    /// </summary>
    private bool ClickMerchant(Hero hero, Room room, GridPosition tile)
    {
        if (room.MerchantAt(tile) is not { } merchant)
        {
            return false;
        }

        if (merchant.Position.ChebyshevDistanceTo(hero.Position) != 1)
        {
            Log($"Stand next to {merchant.Name} to {(merchant.Chained ? "free them" : "trade")}.", Palette.TextDim);
        }
        else if (merchant.Chained && hero.Keys > 0)
        {
            Execute(new FreeMerchant(hero.Id, merchant.Id));
        }
        else if (merchant.Chained)
        {
            ExecuteOdHand((heroId, dice, tools, power) => new PickLock(heroId, tile, dice, tools, power));
        }
        else if (_session.CheckTrade(merchant.Id) is { } refused)
        {
            Log(refused, Palette.TextDim);
        }
        else
        {
            _merchantPanel.ShowMerchant(merchant, hero, Content);
        }

        return true;
    }

    /// <summary>Keeps an open merchant's wares up to date, and closes them once the hero's moved off or the merchant's gone.</summary>
    private void RefreshMerchantPanel()
    {
        if (_merchantPanel.MerchantId is not int merchantId)
        {
            return;
        }

        if (ActiveHero is { } hero && _session.State.RoomOf(hero).FindMerchant(merchantId) is { } merchant && _session.CheckTrade(merchantId) is null)
        {
            _merchantPanel.ShowMerchant(merchant, hero, Content);
        }
        else
        {
            _merchantPanel.Close();
        }
    }

    /// <summary>Plays a merchant's event on the board; false when the event isn't a merchant's.</summary>
    private async Task<bool> PlayMerchantEvent(GameEvent gameEvent)
    {
        switch (gameEvent)
        {
            case MerchantFreed freed:
                CalloutOver(freed.MerchantId, "FREED!", Palette.Loot);
                await UpdateFigure(freed.MerchantId, figure => figure.Chained = false, 0.3);
                return true;

            case MerchantMoved moved:
                await Walk(moved.MerchantId, moved.Path);
                return true;

            case MerchantFollowed followed:
                if (_figures.TryGetValue(followed.MerchantId, out Figure? follower))
                {
                    follower.RoomId = followed.RoomId;
                    follower.Position = followed.Position;
                    if (followed.RoomId == _shownRoomId || followed.FromRoomId == _shownRoomId)
                    {
                        ShowRoom(_shownRoomId);
                    }
                }

                return true;

            case MerchantSlain slain:
                await RemoveFigure(slain.MerchantId);
                _board.QueueRedraw();
                return true;

            case MerchantRescued rescued:
                await RemoveFigure(rescued.MerchantId);
                return true;

            case MerchantTraded or MerchantRobbed:
                return true;

            default:
                return false;
        }
    }
}
