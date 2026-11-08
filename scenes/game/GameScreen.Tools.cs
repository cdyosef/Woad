using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// Tools played on their own: Sticky Gloves worn in the accessory slot, Lockpicks on the belt. Double-clicked
/// there, the tool does its job alone (its bonus and dice, no Od Hand dice): aim it at a lock, a trap or a
/// pocket. In a hand (dragged there, or drawn from the belt) it goes in with the skill and the Od Hand dice
/// instead. Gloves moved into a hand leave their ghost in the accessory slot: double-click it to put them back on.
/// </summary>
public partial class GameScreen
{
    /// <summary>The tool being played on its own, waiting for a click on what to use it on; null for none.</summary>
    private int? _aloneTool;

    private static readonly (Skill Skill, string Verb)[] ToolJobs = [(Skill.PickLock, "Pick a lock"), (Skill.Disarm, "Disarm a trap"), (Skill.Steal, "Steal")];

    /// <summary>Plays a tool on its own: straight to its job if it has one, else asked which.</summary>
    private void PlayToolAlone(Hero hero, CardInstance tool)
    {
        if (tool.Definition.Tool is not { } use)
        {
            return;
        }

        List<(Skill Skill, string Verb)> jobs = [.. ToolJobs.Where(job => use.For.HasFlag(job.Skill))];
        List<(Skill Skill, string Verb)> ready = [.. jobs.Where(job => _session.CheckToolAlone(tool.Id, job.Skill) is null)];
        if (ready.Count == 0)
        {
            Log($"{tool.Definition.Name}: {(jobs.Count > 0 ? _session.CheckToolAlone(tool.Id, jobs[0].Skill) : "nothing it can do alone.")}", Palette.TextDim);
            return;
        }

        if (ready.Count == 1)
        {
            AimToolAlone(tool, ready[0].Skill);
            return;
        }

        _selectedCardId = _targetId = null;
        RefreshPanels();
        _inventory.AskChoice(tool.Id, $"The {tool.Definition.Name}, on their own:", [.. ready.Select(job => (job.Verb, (Action)(() => AimToolAlone(tool, job.Skill))))]);
    }

    /// <summary>Aims the tool alone at its job: a click on the lock, the trap or the one to steal from.</summary>
    private void AimToolAlone(CardInstance tool, Skill skill)
    {
        CancelSelection();
        switch (skill)
        {
            case Skill.PickLock:
                _odAim = OdAim.PickLock;
                break;
            case Skill.Disarm:
                _odAim = OdAim.Disarm;
                break;
            default:
                _stealing = true;
                break;
        }

        _aloneTool = tool.Id;
        string target = skill switch { Skill.PickLock => "a lock", Skill.Disarm => "a trap", _ => "a monster or hero next to you" };
        Log($"The {tool.Definition.Name}, on their own (no Od Hand dice): click {target}. Right-click to cancel.", Palette.DieOdHand);
        RefreshPanels();
    }

    /// <summary>An Od Hand command, made with the tool being played alone (if there is one): no Od Hand dice, just the tool.</summary>
    private GameCommand WithAloneTool(GameCommand command)
    {
        if (_aloneTool is int tool && command is OdHandCommand roll)
        {
            _aloneTool = null;
            return roll with { ToolId = tool, Dice = 0, UseTools = false };
        }

        return command;
    }

    /// <summary>
    /// A card in an equipment slot double-clicked: worn Sticky Gloves (or any tool worn there) play on their own;
    /// an empty accessory slot holding the ghost of what's gone into a hand puts it back on.
    /// </summary>
    private void OnSlotDoubleClicked(EquipSlot slot)
    {
        if (Locked || ActiveHero is not { } hero || _session.State.Turn is null)
        {
            return;
        }

        if (hero.Equipment.GetValueOrDefault(slot) is { Definition.Tool: not null } worn && slot is not (EquipSlot.MainHand or EquipSlot.OffHand))
        {
            PlayToolAlone(hero, worn);
        }
        else if (slot == EquipSlot.Accessory && AccessoryGhost(hero) is { } ghost)
        {
            Execute(new EquipCard(hero.Id, ghost.Id, EquipSlot.Accessory));
        }
    }

    /// <summary>What's gone from the accessory slot into a hand (Sticky Gloves in the off hand), while the slot stands empty: its ghost stays there.</summary>
    internal static CardInstance? AccessoryGhost(Hero hero) =>
        hero.Equipment.ContainsKey(EquipSlot.Accessory) ? null
        : new[] { EquipSlot.OffHand, EquipSlot.MainHand }.Select(hand => hero.Equipment.GetValueOrDefault(hand))
            .FirstOrDefault(card => card?.Definition.Slot == EquipSlot.Accessory);
}
