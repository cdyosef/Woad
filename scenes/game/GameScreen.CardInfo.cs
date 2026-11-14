using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// What each card in a spread hand says: a short line of its key numbers on its face (dice, cost, reach), and the whole
/// of it in the info panel above the row when it's hovered. A spell tells its cost, its dice, who it reaches, what
/// boosting it with more Power does and how far it can be boosted.
/// </summary>
public partial class GameScreen
{
    private CardInfo DescribeCard(HandView hand, AbilityCard card)
    {
        Hero? hero = ActiveHero;
        if (card.Picture.StartsWith("spell_", StringComparison.Ordinal) && hand == _magicHand
            && Content.FindSpell(card.Picture["spell_".Length..]) is { } spell)
        {
            return DescribeSpell(hero, spell);
        }

        Stats? stats = hero?.EffectiveStats;
        bool turn = _session.State.Turn is not null;
        int rumble = turn ? _session.AttackDiceLeft : stats?.AttackDice ?? 0;
        int wend = turn ? _session.MoveDiceLeft : stats?.MoveDice ?? 0;
        int odHand = turn ? _session.OdHandDiceLeft : stats?.OdHandDice ?? 0;
        string weapon = hero?.MainWeapon?.Definition.Name ?? "your fists";
        bool torch = hero is { TorchesHeld: > 0 };
        bool knowsSearch = hero?.Knows(Skill.Search) == true;
        (string line, string body) = card.Picture switch
        {
            "rumble" => ($"{Dice(rumble)} Rumble\n+ {weapon}",
                $"Attack a monster in reach with your Rumble dice and {weapon}. Beat its defense to hit; each 6 earns a Power Die. Uses your Rumble dice: {rumble} left."),
            "wend" => ($"{Dice(wend)} Wend\nsteps to walk",
                $"Roll your Wend dice (plus your Speed) and walk that many steps. Each 6 earns a Power Die. {wend} left."),
            "search" => knowsSearch
                ? ($"{Dice(odHand)} Od Hand\nwhole room{(torch ? "\n+ torch die" : "")}",
                    $"Search the room, up to {GameRules.SearchRange} tiles away, for hidden doors, traps and secrets. Your Od Hand dice{(torch ? ", the torch's die and +1 for its light" : "")} against each one's difficulty. {odHand} left.")
                : torch
                    ? ($"{Dice(odHand)} look around\n+ torch:\nwhole room",
                        $"You haven't learned Search, but the torch lights the way: one roll, your Od Hand dice looking around the tiles beside you, and the torch's die (+1 for its light) searching the whole room, up to {GameRules.SearchRange} tiles away. The torch helps once a turn.")
                    : ($"{Dice(odHand)} Od Hand\nlook around",
                        "Feel around the tiles beside you for traps and hidden doors with your Od Hand dice. Learn Search (or hold a lit torch) to search the whole room."),
            "leap" => ($"1 Wend die\nup to {GameRules.MaxLeap} tiles",
                $"Pick a tile up to {GameRules.MaxLeap} away in a straight line, then roll one Wend die: you carry half the roll, over pits and monsters."),
            "prone" => (hero?.Prone == true ? $"get up\n{GameRules.GetUpCost} steps" : $"lie down\n{GameRules.ProneCost} step",
                _prone.TooltipText),
            "shove" => ($"{Dice(rumble)} Rumble\npush 1 tile",
                "Shove a monster beside you: beat its defense with your Rumble dice to push it a tile, onto a trap or into a pit. Or nudge an ally out of the way."),
            "bull_rush" => ("Rumble + 1 Wend\ncharge, push 2",
                "Charge in a straight line (diagonals too), attack what you reach and push it back two tiles."),
            "guard" => ("hold 1 Rumble\nfor a defense",
                "Hold a Rumble die back for your own or an ally's next defense (an ally beside you)."),
            "high_alert" => ("Rumble dice\ninto a search",
                "Put your Rumble dice on the alert: they roll into your next search, even without the Search skill."),
            "dig_out" => ($"{Dice(rumble)} Rumble\ndig free",
                $"Dig at the earth with every Rumble die left: every {GameRules.DigPerToken} of the roll throws off a Buried token."),
            "break_free" => ($"{Dice(rumble)} Rumble\nvs. its grip",
                "Struggle with every Rumble die left against the grip holding you: beat it to break free."),
            "steal" => ($"{Dice(odHand)} Od Hand\nonce a turn",
                "Steal from a monster or hero beside you: your Od Hand dice against their roll. Keys and backpack cards can be taken."),
            "pick_lock" => ($"{Dice(odHand)} Od Hand\n+ lockpicks",
                "Pick the lock on a door, chest, gate or chains beside you: your Od Hand dice (and your lockpicks) against the lock's difficulty. A 1 can snap a pick."),
            "disarm" => ($"{Dice(odHand)} Od Hand\ntrap beside you",
                "Disarm a trap (or a lit bomb) beside you or on a door: your Od Hand dice against its difficulty. Fail and it goes off on you."),
            "rouse" => ($"{Dice(odHand)} Od Hand\nally beside you",
                "Aid an ally beside you (or yourself): rouse the fallen from Keeling Over, or cure what ails them, with your Od Hand dice or a Medicine Bag."),
            "parry" => ("hold 1 Od Hand\nfor a defense",
                "Hold an Od Hand die back: it rolls into your next defense."),
            "spell_hurl_fire" => ("Od Hand\nhurl the flame",
                "Hurl the flame you hold (Hold Fire) at a monster in reach, with the Od Hand dice you've selected."),
            "lit_stick" => ("light it", "Light the stick in your hand: a torch, for a while."),
            "learn" => ("Od Hand + Magic\nfrom a scroll", "Learn the spell on a scroll you carry: your Od Hand dice and Magic against its level."),
            _ => ("", card.Action.TooltipText),
        };

        return new CardInfo(line, body);
    }

    private static string Dice(int count) => count == 1 ? "1 die" : $"{count} dice";

    /// <summary>A spell: its cost, dice, reach, what boosting it does and how far it goes; on the face, the key numbers.</summary>
    private CardInfo DescribeSpell(Hero? hero, SpellDefinition spell)
    {
        int magic = Math.Max(0, hero?.EffectiveStats.Magic ?? 0);
        string type = DieTypes.Name(spell.DieType).ToLowerInvariant();
        string reach = spell.Shape switch
        {
            SpellShape.Line => $"a line, {spell.Range} tiles",
            SpellShape.Around => "all beside you",
            SpellShape.Burst => $"3x3 burst, range {spell.Range}",
            _ => $"one target, range {spell.Range}",
        } + spell.Ward switch
        {
            SpellWard.Self => "; or yourself, as a boon",
            SpellWard.Beside => "; or you or an ally beside you, as a boon",
            SpellWard.Ally => $"; or you or an ally in range, as a boon",
            _ => "",
        };
        string shortReach = spell.Shape switch
        {
            SpellShape.Line => $"line {spell.Range}",
            SpellShape.Around => "all beside",
            SpellShape.Burst => $"burst, rng {spell.Range}",
            _ => $"range {spell.Range}",
        };
        string line = $"{spell.Cost} Power\n{spell.Dice}d6 {type}{(magic > 0 ? $" +{magic}" : "")}\n{shortReach}";

        var body = new List<string>
        {
            spell.Text,
            $"Cost: {spell.Cost} Power (Magic Energy first, then banked Power Dice), and the Rumble dice you've left this turn.",
            $"Dice: {spell.Dice} {type} {(spell.Dice == 1 ? "die" : "dice")}, plus your Magic ({magic}), as an attack on each one it reaches"
                + (spell.Ward == SpellWard.None ? "." : "; as a boon, the roll says how long and how strong."),
            $"Reaches: {reach}{(spell.IgnoresSight ? ", walls or no" : "")}.",
            magic > 0
                ? $"Boosting: each Power Die more than its cost adds a {type} die. At most {magic} more (your Magic): up to {spell.Dice + magic} dice for {spell.Cost + magic} Power."
                : "Boosting: you need a rank of Magic to put more Power into a spell; each Power more then adds a die, up to your Magic.",
        };
        if (spell.Splash)
        {
            body.Add("Splash: the same roll hits one random creature beside the target, friend or foe.");
        }

        if (spell.Cleave)
        {
            body.Add("Each 6 jumps the same roll to another monster beside the target.");
        }

        if (spell.Shove)
        {
            body.Add("Your Rumble dice go into a shove against the target too.");
        }

        if (spell.Swirl)
        {
            body.Add("On a hit, you move the target a tile of your choice.");
        }

        if (spell.DrawsPoison)
        {
            body.Add("A die more for each Poison token on you; casting throws them all off.");
        }

        if (spell.RubbleDie)
        {
            body.Add("A die more against targets standing on rubble.");
        }

        if (spell.DarknessDie)
        {
            body.Add("A die more against targets in darkness.");
        }

        if (spell.Floor)
        {
            body.Add("Aimed at bare floor, it lays ice.");
        }

        if (hero?.Spells.FirstOrDefault(known => known.Upgrades == spell.Id) is { } upgrade)
        {
            body.Add($"Improved by {upgrade.Name}.");
        }

        return new CardInfo(line, string.Join("\n", body));
    }
}
