using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace FF14Accessibility.Services;

/// <summary>
/// While the game is in area-target mode, Numpad 0 asks it to place that
/// action on the current hard target. The placement is the game's own
/// <c>AreaTargetingExecuteAtObject</c> field, not a position computed here.
/// </summary>
public sealed class GroundTargetService
{
    private readonly ITargetManager _targets;
    private readonly GameDescriptionService _descriptions;
    private readonly TolkService _tolk;
    private readonly IPluginLog _log;
    private readonly GroundTargetSession _session = new();
    private uint _seenId;
    private string _label = "";
    private string? _pendingEnter;
    private bool _readFailed;

    public GroundTargetService(ITargetManager targets, GameDescriptionService descriptions, TolkService tolk, IPluginLog log)
    {
        _targets = targets;
        _descriptions = descriptions;
        _tolk = tolk;
        _log = log;
    }

    public void Update(bool maySpeak)
    {
        var area = Read();
        Remember(area);
        Apply(_session.Advance(area.ActionId, false, 0, _label, ""), maySpeak);
    }

    /// <summary>True when area-target mode owned this key.</summary>
    public bool TryThrowOnTarget()
    {
        var area = Read();
        if (area.ActionId == 0) return false;
        Remember(area);
        var target = _targets.Target;
        var name = target == null ? "" : TolkService.Sanitize(target.Name.TextValue);
        var decision = _session.Advance(area.ActionId, true, target?.GameObjectId ?? 0, _label, name);
        if (decision.WriteObjectId != 0 && !Write(decision.WriteObjectId))
        {
            _session.NoteWriteFailed();
            _tolk.SpeakInterrupt(AccessibilityStrings.GroundTargetUnreadable);
            return true;
        }
        Apply(decision, true);
        return decision.Consume;
    }

    private void Remember(AreaRead area)
    {
        if (area.ActionId == _seenId) return;
        _seenId = area.ActionId;
        _label = area.ActionId == 0 ? "" : Label(area);
    }

    private void Apply(GroundTargetDecision decision, bool maySpeak)
    {
        switch (decision.Notice)
        {
            case GroundTargetNotice.Entered:
                _log.Info($"[Ground] aktiv id={_seenId} name={_label}");
                _pendingEnter = decision.Speech;
                break;
            case GroundTargetNotice.NoTarget:
                _log.Info("[Ground] kein Ziel");
                _pendingEnter = null;
                break;
            case GroundTargetNotice.Aimed:
                _log.Info($"[Ground] Objekt 0x{decision.WriteObjectId:X}");
                _pendingEnter = null;
                break;
            case GroundTargetNotice.Cancelled:
                _log.Info("[Ground] aus");
                _pendingEnter = null;
                break;
            case GroundTargetNotice.StillThere:
                _log.Info("[Ground] nach 30 Frames noch im Bodenziel");
                break;
        }

        if (!maySpeak) return;
        if (_pendingEnter != null)
        {
            _tolk.SpeakInterrupt(_pendingEnter);
            _pendingEnter = null;
            if (decision.Notice is GroundTargetNotice.Entered or GroundTargetNotice.None)
                return;
        }
        if (!string.IsNullOrEmpty(decision.Speech))
            _tolk.SpeakInterrupt(decision.Speech);
    }

    private string Label(AreaRead area)
    {
        var name = area.Type switch
        {
            ActionType.Action => _descriptions.ActionName(area.ActionId),
            ActionType.Item => _descriptions.ItemName(area.ActionId),
            ActionType.EventItem => _descriptions.EventItemName(area.ActionId),
            ActionType.GeneralAction => _descriptions.GeneralActionName(area.ActionId),
            ActionType.BuddyAction => _descriptions.BuddyActionName(area.ActionId),
            ActionType.PetAction => _descriptions.PetActionName(area.ActionId),
            ActionType.CraftAction => _descriptions.CraftActionName(area.ActionId),
            _ => "",
        };
        return string.IsNullOrWhiteSpace(name) ? "" : TolkService.Sanitize(name);
    }

    private unsafe bool Write(ulong objectId)
    {
        try
        {
            var am = ActionManager.Instance();
            if (am == null || am->AreaTargetingActionId == 0) return false;
            // AreaTargetingExecuteAtObject is offset 0xA0. The matching struct source
            // says a value other than 0xE0000000 executes the area action on that
            // object at the next update. The cursor bool at 0xC0 is the other path.
            if (am->AreaTargetingExecuteAtCursor)
                _log.Info("[Ground] Cursor-Flag war an und wird geloescht.");
            am->AreaTargetingExecuteAtCursor = false;
            am->AreaTargetingExecuteAtObject = objectId;
            return true;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[Ground] Objektfeld nicht geschrieben.");
            return false;
        }
    }

    private readonly record struct AreaRead(uint ActionId, ActionType Type);

    private unsafe AreaRead Read()
    {
        try
        {
            var am = ActionManager.Instance();
            if (am == null) return default;
            return new AreaRead(am->AreaTargetingActionId, am->AreaTargetingActionType);
        }
        catch (Exception ex)
        {
            if (_readFailed) return default;
            _readFailed = true;
            _log.Warning(ex, "[Ground] ActionManager nicht lesbar.");
            return default;
        }
    }
}
