using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;

namespace Woad.Core.Rules;

/// <summary>
/// Traveling merchants chained up in dungeons. A key or a picked lock frees one; then they follow
/// whoever freed them (into other rooms and down stairs too), keep away from monsters, and trade with
/// the party: prices are better the higher a hero stands with the Merchant Guild. Brought out of the
/// dungeon alive, they're rescued: everyone in the party rises in the Guild's eyes, the one who freed
/// them most. Stealing from a merchant or striking one down costs a hero with the Guild and the Law.
/// Monsters go for a merchant only when no hero is in their reach.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Cards a dungeon merchant has for sale.</summary>
    public const int MerchantStockSize = 4;

    /// <summary>Tiles a freed merchant moves in a turn.</summary>
    private const int MerchantMove = 3;

    /// <summary>A monster this close makes a freed merchant back away.</summary>
    private const int MerchantFear = 3;

    /// <summary>Guild standing for everyone in the party when a merchant is rescued; the one who freed them gets the extra too.</summary>
    public const int RescueStanding = 1;

    public const int RescuerExtraStanding = 1;

    /// <summary>What a theft costs the thief with the Guild and the Law (half of it, rounded down, if they're caught empty-handed).</summary>
    public const int TheftGuildCost = 2;

    public const int TheftLawCost = 1;

    /// <summary>What striking a merchant down costs with the Guild and the Law.</summary>
    public const int MurderGuildCost = 5;

    public const int MurderLawCost = 3;

    /// <summary>
    /// A merchant's wares for a room on <paramref name="floor"/>: cards from the theme's loot (no
    /// treasure), as good as the floor, better the higher the Merchant Guild stands; one time in four,
    /// one more from deeper still.
    /// </summary>
    private static List<string> MerchantStock(ContentCatalog content, ThemeDefinition theme, int floor, int guildLevel, IRandomSource random)
    {
        int tier = Math.Clamp(floor + (guildLevel - 1) / 2, 1, theme.Floors);
        List<LootDeckEntry> wares = theme.LootDeck
            .Where(entry => content.GetCard(entry.Card) is { Type: not CardType.Treasure, Coins: false })
            .ToList();
        List<string> fair = wares.Where(entry => entry.MinFloor <= tier).Select(entry => entry.Card).Distinct().ToList();
        var stock = new List<string>();
        while (stock.Count < MerchantStockSize && fair.Count > 0)
        {
            string card = random.Pick(fair);
            fair.Remove(card);
            stock.Add(card);
        }

        List<string> rare = wares.Where(entry => entry.MinFloor > tier).Select(entry => entry.Card).Distinct().ToList();
        if (rare.Count > 0 && random.NextInt(0, 100) < 25)
        {
            stock.Add(random.Pick(rare));
        }

        return stock;
    }

    private static void AddMerchants(GameState state, ContentCatalog content, RunSetup setup, GameRandom random)
    {
        ThemeDefinition theme = content.GetTheme(setup.ThemeId);
        foreach (RoomSetup roomSetup in setup.Rooms.Where(room => room.Features?.Merchant is not null))
        {
            MerchantSetup chained = roomSetup.Features!.Merchant!;
            List<string> stock = MerchantStock(content, theme, roomSetup.Floor, setup.MerchantGuildLevel, random.Loot);
            state.Rooms[roomSetup.Id].AddMerchant(new Merchant(state.NextId(), chained.Name, chained.Position, chained.LockDifficulty, stock));
        }
    }

    /// <summary>The price a merchant asks a hero for a card: twice its worth, less 5% for each point of the hero's Guild standing (to half), more for each point below.</summary>
    public static int MerchantPrice(CardDefinition card, int standing) =>
        Math.Max(1, (int)Math.Round(card.Value * 2 * (1 - 0.05 * Math.Clamp(standing, -10, 10))));

    /// <summary>What a merchant pays for a card: its worth.</summary>
    public static int MerchantPays(CardDefinition card) => Math.Max(1, card.Value);

    /// <summary>A hero's standing with the Merchant Guild now: what they came in with, and what this run has done to it.</summary>
    public static int GuildStandingOf(Hero hero) => hero.GuildStanding + hero.GuildStandingChange;

    /// <summary>Standing so low the Guild's merchants won't deal with the hero at all.</summary>
    public const int ShunnedStanding = -10;

    /// <summary>Why the active hero can't free the merchant now (with a key; a lock can be picked with Od Hand instead), or null.</summary>
    public string? CheckFreeMerchant(int merchantId)
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        Hero hero = ActiveHero;
        if (ActiveRoom.FindMerchant(merchantId) is not { } merchant)
        {
            return "There's no merchant there.";
        }

        if (!merchant.Chained)
        {
            return $"{merchant.Name} is already free.";
        }

        if (merchant.Position.ChebyshevDistanceTo(hero.Position) != 1)
        {
            return $"Stand next to {merchant.Name} to free them.";
        }

        return hero.Keys < 1 ? $"{merchant.Name}'s chains are locked: use a key, or pick the lock with Od Hand ({merchant.LockDifficulty}+)." : null;
    }

    private string? Handle(FreeMerchant command, List<GameEvent> events)
    {
        if (CheckFreeMerchant(command.MerchantId) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        hero.Keys--;
        events.Add(new KeyUsed(hero.Id, hero.Keys));
        Free(hero, ActiveRoom.FindMerchant(command.MerchantId)!, picked: false, events);
        return null;
    }

    private static void Free(Hero hero, Merchant merchant, bool picked, List<GameEvent> events)
    {
        merchant.FreedById = hero.Id;
        merchant.LeaderId = hero.Id;
        events.Add(new MerchantFreed(hero.Id, merchant.Id, picked));
    }

    /// <summary>Why the active hero can't trade with the merchant now, or null: a freed merchant beside them, who'll still deal with them.</summary>
    public string? CheckTrade(int merchantId)
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        Hero hero = ActiveHero;
        if (ActiveRoom.FindMerchant(merchantId) is not { } merchant)
        {
            return "There's no merchant there.";
        }

        if (merchant.Chained)
        {
            return $"{merchant.Name} won't trade in chains: free them first.";
        }

        if (merchant.Position.ChebyshevDistanceTo(hero.Position) > 1)
        {
            return $"Stand next to {merchant.Name} to trade.";
        }

        return GuildStandingOf(hero) <= ShunnedStanding ? $"{merchant.Name} won't deal with you: the Guild knows your name." : null;
    }

    private string? Handle(BuyFromMerchant command, List<GameEvent> events)
    {
        if (CheckTrade(command.MerchantId) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        Merchant merchant = ActiveRoom.FindMerchant(command.MerchantId)!;
        if (!merchant.Stock.Contains(command.CardId))
        {
            return $"{merchant.Name} hasn't got one of those.";
        }

        CardDefinition card = Content.GetCard(command.CardId);
        int price = MerchantPrice(card, GuildStandingOf(hero));
        if (hero.Gold < price)
        {
            return $"The {card.Name} costs {price} gold, and you have {hero.Gold}.";
        }

        if (!hero.CanCarry(card))
        {
            return "Your backpack is full.";
        }

        merchant.RemoveStock(command.CardId);
        hero.Gold -= price;
        var bought = new CardInstance(State.NextId(), card, broughtInByHeroId: null);
        GiveCards(hero, ActiveRoom, [bought], events);
        events.Add(new MerchantTraded(hero.Id, merchant.Id, bought.Id, price, Bought: true));
        return null;
    }

    private string? Handle(SellToMerchant command, List<GameEvent> events)
    {
        if (CheckTrade(command.MerchantId) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        Merchant merchant = ActiveRoom.FindMerchant(command.MerchantId)!;
        if (hero.FindCard(command.CardId) is not var (card, _))
        {
            return "You haven't got that.";
        }

        int pays = MerchantPays(card.Definition);
        CardInstance sold = TakeFromHero(hero, card, 1);
        merchant.AddStock(sold.Definition.Id);
        GiveGold(hero, pays, events);
        events.Add(new MerchantTraded(hero.Id, merchant.Id, sold.Id, pays, Bought: false));
        return null;
    }

    /// <summary>
    /// A theft from a merchant: their roll is two dice. Beaten, they lose a card at random; either way the
    /// thief's name suffers with the Guild and the Law, less if they're caught empty-handed.
    /// </summary>
    private void StealFromMerchant(Hero thief, Room room, Merchant merchant, int total, List<GameEvent> events)
    {
        DiceRoll guard = _dice.Roll(2);
        events.Add(new DefenseRolled(merchant.Id, guard, 0, guard.Total));
        bool caught = total <= guard.Total || merchant.Stock.Count == 0;
        string? cardId = caught ? null : merchant.Stock[_random.Loot.NextInt(0, merchant.Stock.Count)];
        if (cardId is not null)
        {
            merchant.RemoveStock(cardId);
            merchant.Robbed = true;
            GiveCards(thief, room, [new CardInstance(State.NextId(), Content.GetCard(cardId), broughtInByHeroId: null)], events);
        }

        int guild = caught ? TheftGuildCost / 2 : TheftGuildCost;
        thief.GuildStandingChange -= guild;
        thief.LawStandingChange -= TheftLawCost;
        events.Add(new MerchantRobbed(thief.Id, merchant.Id, cardId, caught, guild, TheftLawCost));
    }

    /// <summary>A blow against a merchant: one die to defend with, and one heart. Struck down by a hero, it costs them dear with the Guild and the Law.</summary>
    private void DamageMerchant(int attackerId, Hero? hero, Room room, Merchant merchant, int attackTotal, List<GameEvent> events)
    {
        DiceRoll defense = _dice.Roll(1);
        events.Add(new DefenseRolled(merchant.Id, defense, 0, defense.Total));
        int damage = DamageFor(attackTotal, defense.Total);
        merchant.Hearts = Math.Max(0, merchant.Hearts - damage);
        events.Add(new Damaged(merchant.Id, damage, merchant.Hearts));
        if (merchant.Hearts > 0)
        {
            return;
        }

        room.RemoveMerchant(merchant);
        GridPosition spot = SafeTile(room, merchant.Position);
        List<CardInstance> wares = merchant.Stock.Select(cardId => new CardInstance(State.NextId(), Content.GetCard(cardId), broughtInByHeroId: null)).ToList();
        if (wares.Count > 0)
        {
            room.AddLoot(spot, wares);
        }

        if (hero is not null)
        {
            hero.GuildStandingChange -= MurderGuildCost;
            hero.LawStandingChange -= MurderLawCost;
        }

        events.Add(new MerchantSlain(merchant.Id, attackerId, hero is not null, spot));
    }

    /// <summary>Merchants the active hero could attack right now (striking one is a crime).</summary>
    public IReadOnlyList<Merchant> GetAttackableMerchants()
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 } || AttackDiceLeft < 1)
        {
            return [];
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        return room.Merchants.Where(merchant => CanHeroReach(hero, room.Layout, hero.Position, merchant.Position)).ToList();
    }

    private string? Handle(AttackMerchant command, List<GameEvent> events)
    {
        int left = AttackDiceLeft;
        int dice = command.Dice ?? left;
        if (left < 1 || dice < 1 || dice > left)
        {
            return left < 1 ? "You've used all your Rumble dice this turn." : $"Choose 1 to {left} Rumble dice.";
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (GetAttackableMerchants().FirstOrDefault(merchant => merchant.Id == command.MerchantId) is not { } target)
        {
            return "They're out of reach.";
        }

        if (CheckBankedPowerDice(hero, command.PowerDice) is { } powerError)
        {
            return powerError;
        }

        Wielding wielding = Wield(hero, room, target.Position);
        bool armed = command.UseWeapon && wielding.HasStrike && (wielding.Bonus > 0 || wielding.Dice > 0);
        Turn.AttackDiceUsed += dice;
        UseWeapon(hero, room, wielding, armed, target.Position, events);
        SpendBankedPowerDice(hero, command.PowerDice, events);
        RollHeroAttack(hero, dice, armed ? wielding.Bonus : 0, [target.Id], DamageSource.Weapon, events, command.PowerDice,
            weaponDice: armed ? wielding.Dice : 0, weapon: wielding.Weapon);
        return null;
    }

    /// <summary>A monster with no hero in reach takes a swing at a merchant beside it, if there is one.</summary>
    private bool MonsterAttacksMerchant(Room room, Monster monster, List<GameEvent> events)
    {
        if (monster.AttackDiceThisTurn < 1
            || room.Merchants.FirstOrDefault(merchant => Targeting.CanAttack(room.Layout, monster.Definition.AttackPattern, monster.Definition.AttackRange, monster.Position, merchant.Position)) is not { } prey)
        {
            return false;
        }

        (DiceRoll roll, IReadOnlyList<DieType> types, int powerDice) = RollMonsterDice(monster, monster.AttackDiceThisTurn);
        int total = roll.Total + monster.Definition.Weapon;
        events.Add(new AttackRolled(monster.Id, roll, monster.Definition.Weapon, total, [prey.Id], powerDice) { DieTypes = types });
        DamageMerchant(monster.Id, null, room, prey, total, events);
        return true;
    }

    /// <summary>
    /// The hero a freed merchant follows: whoever freed them while they're still standing in the
    /// dungeon, else the nearest hero standing in their room, else any hero still standing.
    /// </summary>
    private Hero? MerchantLeader(Room room, Merchant merchant)
    {
        if (merchant.LeaderId is int id && State.Heroes.FirstOrDefault(hero => hero.Id == id) is { IsActive: true } leader)
        {
            return leader;
        }

        Hero? next = State.ActiveHeroesIn(room).OrderBy(hero => hero.Position.ChebyshevDistanceTo(merchant.Position)).ThenBy(hero => hero.Seat).FirstOrDefault()
                     ?? State.Heroes.Where(hero => hero.IsActive).OrderBy(hero => hero.Seat).FirstOrDefault();
        merchant.LeaderId = next?.Id;
        return next;
    }

    /// <summary>
    /// Freed merchants move before the monsters: away from any monster near them, otherwise up beside
    /// the hero they follow.
    /// </summary>
    private void MoveMerchants(Room room, List<GameEvent> events)
    {
        foreach (Merchant merchant in room.Merchants.Where(merchant => !merchant.Chained).ToList())
        {
            if (MerchantLeader(room, merchant) is not { } leader || leader.RoomId != room.Id)
            {
                continue;
            }

            PathSearch search = PathSearch.Run(merchant.Position, tile => MerchantCanStand(room, merchant, tile), MerchantMove,
                stepCost: tile => room.Layout[tile].MoveCost(false));
            List<GridPosition> threats = room.Monsters.Where(monster => monster.Position.ChebyshevDistanceTo(merchant.Position) <= MerchantFear).Select(monster => monster.Position).ToList();
            GridPosition goal = threats.Count > 0
                ? search.Distances.Keys
                    .OrderByDescending(tile => threats.Min(threat => threat.ChebyshevDistanceTo(tile)))
                    .ThenBy(tile => tile.ChebyshevDistanceTo(leader.Position))
                    .ThenBy(tile => tile.Y)
                    .ThenBy(tile => tile.X)
                    .First()
                : merchant.Position.ChebyshevDistanceTo(leader.Position) <= 1
                    ? merchant.Position
                    : search.Distances.Keys
                        .OrderBy(tile => tile.ChebyshevDistanceTo(leader.Position) <= 1 ? 0 : 1)
                        .ThenBy(tile => tile.ManhattanDistanceTo(leader.Position))
                        .ThenBy(tile => search.Distances[tile])
                        .ThenBy(tile => tile.Y)
                        .ThenBy(tile => tile.X)
                        .First();
            if (goal == merchant.Position)
            {
                continue;
            }

            IReadOnlyList<GridPosition> path = search.PathTo(goal);
            merchant.Position = goal;
            events.Add(new MerchantMoved(merchant.Id, path));
        }
    }

    private bool MerchantCanStand(Room room, Merchant merchant, GridPosition tile) =>
        room.Layout[tile].IsWalkable() && room.TrapAt(tile) is null && (room.MerchantAt(tile) is not { } other || other == merchant)
        && room.MonsterAt(tile) is null && !State.HeroesIn(room).Any(hero => hero.Position == tile);

    /// <summary>
    /// A freed merchant whose leader has gone on into another room (or down a floor) follows close
    /// behind, turning up on a free tile beside them.
    /// </summary>
    private void MerchantsFollow(List<GameEvent> events)
    {
        foreach (Room room in State.Rooms.Values.ToList())
        {
            foreach (Merchant merchant in room.Merchants.Where(merchant => !merchant.Chained).ToList())
            {
                if (MerchantLeader(room, merchant) is not { } leader || leader.RoomId == room.Id)
                {
                    continue;
                }

                Room to = State.RoomOf(leader);
                if (to.Positions.Where(tile => tile.ChebyshevDistanceTo(leader.Position) <= 2 && MerchantCanStand(to, merchant, tile))
                        .OrderBy(tile => tile.ChebyshevDistanceTo(leader.Position))
                        .ThenBy(tile => tile.Y)
                        .ThenBy(tile => tile.X)
                        .Select(tile => (GridPosition?)tile)
                        .FirstOrDefault() is not { } spot)
                {
                    continue;
                }

                room.RemoveMerchant(merchant);
                merchant.Position = spot;
                to.AddMerchant(merchant);
                events.Add(new MerchantFollowed(merchant.Id, room.Id, to.Id, spot));
            }
        }
    }

    /// <summary>
    /// Heroes leaving the dungeon take the freed merchants following them along: rescued. Everyone in the
    /// party rises with the Guild, and whoever freed them most.
    /// </summary>
    private void RescueMerchants(IReadOnlyCollection<Hero> leaving, Room room, List<GameEvent> events)
    {
        foreach (Merchant merchant in room.Merchants.Where(merchant => !merchant.Chained && leaving.Any(hero => hero.Id == merchant.LeaderId)).ToList())
        {
            room.RemoveMerchant(merchant);
            foreach (Hero member in State.Heroes)
            {
                member.GuildStandingChange += RescueStanding + (member.Id == merchant.FreedById ? RescuerExtraStanding : 0);
            }

            State.RescuedMerchants.Add(new RescuedMerchant(merchant.Name, merchant.FreedById!.Value));
            events.Add(new MerchantRescued(merchant.Id, merchant.Name, merchant.FreedById!.Value));
        }
    }
}
