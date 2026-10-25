using Godot;
using Woad.Core.Characters;
using Woad.Core.Grid;
using Woad.Core.Tavern;
using Woad.Core.Woarld;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Woarld;

/// <summary>
/// The Woarld Map's dangers, met on the way: roaming bands (Fight or Flee), ambushes (fight, surprised
/// unless the party saw it coming) and toll posts (Pay, Turn back, Fight or Suss Out); and Scouting for
/// what lies in wait. Fights go to the game screen; standing changes are saved on the characters.
/// </summary>
public partial class WoarldMapScreen
{
    private EncounterDialog _encounter = null!;
    private Direction? _lastStep;

    /// <summary>The party chose to fight: play this skirmish, then come back to the map.</summary>
    public event Action<SkirmishSpec>? FightRequested;

    /// <summary>Characters' gold or standing changed out on the map (a toll paid, a coward's flight): save them.</summary>
    public event Action<IReadOnlyList<Character>>? CharactersChanged;

    internal EncounterDialog Encounter => _encounter;

    internal Button ScoutButton { get; private set; } = null!;

    private List<int> OdHandDice => [.. Party.Select(hero => hero.Stats(Content).OdHandDice)];

    private List<int> WendDice => [.. Party.Select(hero => hero.Stats(Content).MoveDice)];

    private void BuildEncounters()
    {
        _encounter = new EncounterDialog();
        AddChild(_encounter);
    }

    /// <summary>After a step: the dangers move, and perhaps the party meets one. True if something happened.</summary>
    private bool MeetDangers(StepResult step)
    {
        if (World is null)
        {
            return false;
        }

        Encounter? met = World.AfterStep(Content, Party.Count, step.Steps, _random, out IReadOnlyList<string> lost);
        if (lost.Count > 0)
        {
            Say($"{string.Join(" and ", lost)}, cornered on the road, didn't make it.");
        }

        if (met is null)
        {
            return lost.Count > 0;
        }

        _path.Clear();
        _view.Destination = null;
        Meet(met);
        return true;
    }

    private void Meet(Encounter met)
    {
        switch (met.Kind)
        {
            case EncounterKind.Band:
                _encounter.Ask(met.Title, met.Text + (met.Band!.Cornering is null ? "" : $" They've got {met.Band.Cornering} cornered!"),
                    ("Fight", "Fight them here, on this ground.", () => Fight(met, monstersFirst: false)),
                    ("Flee", "The party's Wend against their chase. Get away and they lose you (and you lose some time); fail and they strike first. Fleeing is noted by Cowards.", () => Flee(met)));
                break;

            case EncounterKind.Ambush:
                WoarldDangers dangers = World!.Dangers(Content);
                bool seen = dangers.SeesItComing(met.Ambush!, Journey.Day, OdHandDice, _random, out int check);
                dangers.Ambushes.Remove(met.Ambush!);
                _encounter.Ask(met.Title,
                    seen ? $"{met.Text} But you saw it coming ({check})." : $"{met.Text} They take you by surprise ({check}): they strike first!",
                    ("Fight", "There's no getting away now.", () => Fight(met, monstersFirst: !seen)));
                break;

            case EncounterKind.Toll:
                AskToll(met);
                break;
        }
    }

    private void AskToll(Encounter met)
    {
        TollPost toll = met.Toll!;
        int due = WoarldDangers.TollPerHero * Party.Count;
        var choices = new List<(string, string, Action)>
        {
            ($"Pay {due}", $"{WoarldDangers.TollPerHero} gold a head, and you pass.", () => PayToll(met, due)),
            ("Turn back", "Back the way you came. Cowards will hear of it.", TurnBack),
            ("Fight", toll.ShowsAsBandits ? "Clear the bandits off the road: the Law will thank you." : "Fight the guards. If they're real, the Law won't forget it.", () => Fight(met with { Monsters = WoarldDangers.TollCompany(toll) }, monstersFirst: false)),
        };
        if (!toll.Sussed)
        {
            choices.Add(("Suss Out", "Everyone's Od Hand against their captain's word: are these real guards?", () => SussOut(met)));
        }

        string title = toll.ShowsAsBandits ? "Bandits at a toll post" : "A toll post";
        string text = toll.ShowsAsBandits ? "The bandits posing as guards block the way, hands out for a toll." : met.Text;
        _encounter.Ask(title, text, [.. choices]);
    }

    private void SussOut(Encounter met)
    {
        TollPost toll = met.Toll!;
        bool seen = WoarldDangers.SussOut(toll, OdHandDice, _random);
        Say(!seen ? "You can't suss out any trouble." : toll.Bandits ? "These guys are not who they say they are: bandits!" : "These are real guards.");
        AskToll(met);
    }

    private void PayToll(Encounter met, int toll)
    {
        if (Party.Sum(hero => hero.Gold) < toll)
        {
            Say($"The party hasn't got {toll} gold between them.");
            AskToll(met);
            return;
        }

        // Each pays their share if they can; the richest make up the rest.
        int owed = toll;
        foreach (Character hero in Party.OrderBy(hero => hero.Gold).ToList())
        {
            int share = Math.Min(owed, Math.Min(WoarldDangers.TollPerHero, hero.Gold));
            if (hero.SpendGold(share))
            {
                owed -= share;
            }
        }

        foreach (Character hero in Party.OrderByDescending(hero => hero.Gold).ToList())
        {
            int more = Math.Min(owed, hero.Gold);
            if (more > 0 && hero.SpendGold(more))
            {
                owed -= more;
            }
        }

        CharactersChanged?.Invoke(Party);
        Say(met.Toll!.ShowsAsBandits ? $"You pay the bandits {toll} gold. They wave you on, grinning." : $"You pay the toll, {toll} gold, and pass.");
        Refresh();
    }

    private void TurnBack()
    {
        foreach (Character hero in Party)
        {
            hero.ChangeStanding(cowards: 1);
        }

        CharactersChanged?.Invoke(Party);
        if (_lastStep is { } came)
        {
            Journey.Step(came.Opposite(), WendBonus);
        }

        Say("You turn back the way you came. (Cowards +1)");
        JourneyChanged?.Invoke();
        Refresh();
    }

    private void Flee(Encounter met)
    {
        foreach (Character hero in Party)
        {
            hero.ChangeStanding(cowards: 1);
        }

        CharactersChanged?.Invoke(Party);
        Band band = met.Band!;
        if (WoarldDangers.Flees(band, WendDice, _random, out int total, out int chase))
        {
            // They lose the party, and the party loses some time getting away.
            for (int i = 0; i < 4; i++)
            {
                GridPosition away = Enum.GetValues<Direction>().Select(band.Position.Step)
                    .Where(tile => Journey.Map.IsWalkable(tile) && Journey.Map.PlaceAt(tile) is null)
                    .DefaultIfEmpty(band.Position)
                    .MaxBy(tile => tile.ChebyshevDistanceTo(Journey.Leader));
                band.X = away.X;
                band.Y = away.Y;
            }

            Journey.PassTime(WoarldDangers.ScoutSteps);
            Say($"You get away ({total} against their {chase}). (Cowards +1)");
            JourneyChanged?.Invoke();
            Refresh();
            return;
        }

        Say($"They run you down ({total} against their {chase})! (Cowards +1)");
        Fight(met, monstersFirst: true);
    }

    private void Fight(Encounter met, bool monstersFirst)
    {
        string theme = Journey.Map.Places
            .Where(place => place.Kind == PlaceKind.Dungeon && place.ThemeId is not null)
            .OrderBy(place => place.Position.ChebyshevDistanceTo(Journey.Leader))
            .Select(place => place.ThemeId!)
            .FirstOrDefault() ?? "emerald_keep";
        FightRequested?.Invoke(new SkirmishSpec(theme, met.Terrain, met.Monsters, monstersFirst, met.Title, met.Kind, met.Band?.Id ?? 0, Journey.Leader));
    }

    private void Scout()
    {
        if (World is null)
        {
            return;
        }

        IReadOnlyList<HiddenAmbush> found = World.Dangers(Content).Scout(Journey, OdHandDice, _random, out int total);
        (bool newDay, _) = Journey.PassTime(WoarldDangers.ScoutSteps);
        Sfx.Play("dice");
        Say($"Scouting ({total}): " + (found.Count == 0 ? "nothing lies in wait nearby that you can find." : $"you spot {string.Join(", ", found.Select(ambush => ambush.Name))} lying in wait!") +
            (newDay ? $" Day {Journey.Day} dawns." : $" Ambush checks +{WoarldDangers.ScoutBonus} for the rest of the day."));
        JourneyChanged?.Invoke();
        Refresh();
    }
}
