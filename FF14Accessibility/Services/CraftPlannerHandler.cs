using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LuminaCraftAction = Lumina.Excel.Sheets.CraftAction;

namespace FF14Accessibility.Services;

/// <summary>
/// Reads the synthesis planner (addon "CraftActionSimulator", the window behind
/// the "Synthese-Planer" button of a running craft).
///
/// WHY A READER OF ITS OWN: a row of the planner shows an action ICON and one
/// line under it - the effect ("Fortschritt +6, wenn Kommando-Effizienz 100")
/// or "Noch nicht erlernt." - but never the action's name (dump 2026-10-07,
/// "FFXIV_UI_Dump_planer (2).txt"), and the game binds no tooltip or action to
/// the rows (probe 2026-10-07 20:21:45: 0 bindings). The generic focus reader
/// could therefore only say "Noch nicht erlernt." eight times in a row (user
/// 2026-10-07: "mir wird nicht gesagt, was das fuer Dinge sind").
///
/// Where the name comes from: the planner's agent keeps the rows it shows as
/// two vectors (ClientStructs AgentCraftActionSimulator.Progress / .Quality,
/// one EfficiencyCalculation with an ActionId per row). The probe of 2026-10-07
/// 20:27:07 confirmed both: 8 progress rows, the first "Bearbeiten" (CraftAction
/// 100001, the only one Available, progress increase 6 = the row's "+6"), and
/// 13 quality rows starting with "Veredelung" - the names come from the
/// CraftAction sheet, the Action sheet has none of these ids.
///
/// A row is joined to its action by ICON: the row's icon id must match the
/// CraftAction icon of exactly one action in the agent's vectors. No match or
/// more than one = no name (logged), never a guess - a wrong action name sends
/// a blind player to the hotbar for the wrong skill.
/// </summary>
public sealed unsafe class CraftPlannerHandler
{
    /// <summary>The planner's addon name (log 2026-10-07 20:10:46 "Addon: CraftActionSimulator").</summary>
    public const string AddonName = "CraftActionSimulator";

    // Dump 2026-10-07: radio buttons id=3 "Favoriten" (left), id=4 "Synthese",
    // id=5 "Veredelung" (right) - listed in screen order, which is the order
    // Num7/Num9 walk them. Each carries its name as a text node.
    private static readonly uint[] TabNodeIds = [3, 4, 5];
    private const uint SynthesisTabId  = 4;
    private const uint RefinementTabId = 5;

    /// <summary>The action list (dump: id=6, List, ListLen=8 on the Synthese tab).</summary>
    private const uint ListNodeId = 6;

    private readonly IDataManager _data;
    private readonly TolkService _tolk;
    private readonly IPluginLog _log;

    private bool   _opened;
    private uint   _lastTabId;
    private nint   _lastRenderer;
    private string _lastRow = string.Empty;
    private string _pendingTab = string.Empty;
    private uint   _lastUnresolvedIcon;

    public CraftPlannerHandler(IDataManager data, TolkService tolk, IPluginLog log)
    {
        _data = data;
        _tolk = tolk;
        _log  = log;
    }

    /// <summary>
    /// Per-frame reader: on opening it speaks title, tab and the row under the
    /// cursor in one line; afterwards every tab switch and every row change.
    /// </summary>
    public void OnUpdate(AtkUnitBase* unit, string title)
    {
        if (unit == null) return;
        if (!unit->IsVisible) { Reset(); return; }
        // The first PostUpdate arrives before OnSetup (see OnCharacterUpdate).
        if (!unit->IsReady) return;

        var tabId = FindCheckedTab(unit, out var tabIndex, out var tabLabel);
        if (tabId != 0 && tabId != _lastTabId)
        {
            _lastTabId = tabId;
            _pendingTab = tabLabel.Length > 0
                ? AccessibilityStrings.CraftPlannerTab(tabLabel, tabIndex, TabNodeIds.Length)
                : AccessibilityStrings.CraftTabUnnamed;
            _log.Info($"[Planer] Reiter id={tabId} '{tabLabel}'");
        }

        var renderer = FocusedRow(unit);
        var row = string.Empty;
        if (renderer != null)
        {
            // Right after a tab switch the list still shows the old tab's rows
            // for a frame (probe 2026-10-07 20:21:52: tab checked at .326, row
            // text replaced at .351). A row whose action is not in the new
            // tab's vector is such a leftover - wait for the refill instead of
            // announcing the old row under the new tab's name.
            if (!RowBelongsToTab(renderer, tabId)) return;
            row = DescribeRow(unit, renderer);
        }

        var rowChanged = (nint)renderer != _lastRenderer || row != _lastRow;
        if (_opened && _pendingTab.Length == 0 && !rowChanged) return;
        _lastRenderer = (nint)renderer;
        _lastRow = row;

        var parts = new List<string>();
        if (!_opened && title.Length > 0) parts.Add(title);
        if (_pendingTab.Length > 0) parts.Add(_pendingTab);
        if (row.Length > 0) parts.Add(row);
        _opened = true;
        _pendingTab = string.Empty;
        if (parts.Count == 0) return;

        var line = string.Join(". ", parts);
        _log.Info($"[Planer] {line}");
        _tolk.SpeakInterrupt(line);
    }

    /// <summary>
    /// True when <paramref name="node"/> sits in a row of the planner's action
    /// list - the generic focus reader stays silent there, this reader speaks.
    /// </summary>
    public bool IsPlannerRow(AtkResNode* node, AtkUnitBase* unit)
    {
        if (node == null || unit == null || !unit->IsVisible) return false;
        var renderer = ClimbToRenderer(node);
        return renderer != null && IsInList(renderer, unit);
    }

    /// <summary>Forgets everything, so the next opening is spoken in full.</summary>
    public void Reset()
    {
        _opened = false;
        _lastTabId = 0;
        _lastRenderer = 0;
        _lastRow = string.Empty;
        _pendingTab = string.Empty;
    }

    private static uint FindCheckedTab(AtkUnitBase* unit, out int index, out string label)
    {
        index = 0;
        label = string.Empty;
        for (var i = 0; i < TabNodeIds.Length; i++)
        {
            var node = unit->GetNodeById(TabNodeIds[i]);
            if (node == null || (int)node->Type < 1000) continue;
            var comp = ((AtkComponentNode*)node)->Component;
            if (comp == null || comp->GetComponentType() != ComponentType.RadioButton) continue;
            if (!((AtkComponentButton*)comp)->IsChecked) continue;
            index = i + 1;
            label = FirstVisibleText(comp, (AtkResNode*)((AtkComponentNode*)node));
            return TabNodeIds[i];
        }
        return 0;
    }

    private AtkComponentListItemRenderer* FocusedRow(AtkUnitBase* unit)
    {
        var stage = AtkStage.Instance();
        if (stage == null || stage->AtkInputManager == null) return null;
        var focus = stage->AtkInputManager->FocusedNode;
        if (focus == null) return null;
        var renderer = ClimbToRenderer(focus);
        return renderer != null && IsInList(renderer, unit) ? renderer : null;
    }

    /// <summary>
    /// The row as one line: action name, the game's own text under it, then
    /// the position in the list.
    /// </summary>
    private string DescribeRow(AtkUnitBase* unit, AtkComponentListItemRenderer* renderer)
    {
        var parts = new List<string>();
        var name = ActionNameForRow(renderer);
        if (name.Length > 0) parts.Add(name);

        // Only texts that are really on screen: the row keeps both of its lines
        // ("Noch nicht erlernt." and the effect) and hides one through its
        // PARENT. Checking the text node alone read both at once (log
        // 2026-10-07 20:21:58: "Noch nicht erlernt., Qualität +35, ...").
        var comp = (AtkComponentBase*)renderer;
        var root = (AtkResNode*)comp->OwnerNode;
        for (var i = 0; i < comp->UldManager.NodeListCount; i++)
        {
            var n = comp->UldManager.NodeList[i];
            if (n == null || n->Type != NodeType.Text || !IsShown(n, root)) continue;
            var t = AtkText.ReadClean((AtkTextNode*)n).Trim();
            if (t.Length > 0) parts.Add(t);
        }
        if (parts.Count == 0) return string.Empty;

        var text = string.Join(", ", parts);
        var list = ListOf(unit);
        if (list != null && list->ListLength > 0 && renderer->ListItemIndex >= 0)
            text = AccessibilityStrings.RowWithPosition(text, renderer->ListItemIndex + 1, list->ListLength);
        return text;
    }

    /// <summary>The CraftAction name for the row's icon, or empty (see class doc).</summary>
    private string ActionNameForRow(AtkComponentListItemRenderer* renderer)
    {
        var icon = RowIconId(renderer);
        if (icon == 0) return string.Empty;

        var matches = ActionsWithIcon(icon);
        if (matches.Count == 1 && _data.GetExcelSheet<LuminaCraftAction>().TryGetRow(matches[0], out var row))
            return row.Name.ExtractText().Trim();

        if (icon != _lastUnresolvedIcon)
        {
            _lastUnresolvedIcon = icon;
            _log.Warning($"[Planer] Icon {icon}: {matches.Count} passende Aktionen im Agenten - kein Name angesagt.");
        }
        return string.Empty;
    }

    /// <summary>
    /// False only when the row's action is known AND sits in neither of the
    /// vectors that belong to the checked tab - i.e. the list still shows the
    /// previous tab. The favourites tab has no vector of its own, so its rows
    /// are always taken.
    /// </summary>
    private bool RowBelongsToTab(AtkComponentListItemRenderer* renderer, uint tabId)
    {
        if (tabId != SynthesisTabId && tabId != RefinementTabId) return true;
        var icon = RowIconId(renderer);
        if (icon == 0) return true;
        var agent = Agent();
        if (agent == null) return true;

        var own   = tabId == SynthesisTabId ? agent->Progress : agent->Quality;
        var other = tabId == SynthesisTabId ? agent->Quality  : agent->Progress;
        return HasIcon(own, icon) || !HasIcon(other, icon);
    }

    private List<uint> ActionsWithIcon(uint icon)
    {
        var result = new List<uint>();
        var agent = Agent();
        if (agent == null) return result;
        Collect(agent->Progress, icon, result);
        Collect(agent->Quality, icon, result);
        return result.Distinct().ToList();
    }

    private void Collect(FFXIVClientStructs.STD.StdVector<AgentCraftActionSimulator.EfficiencyCalculation> rows,
                         uint icon, List<uint> into)
    {
        var count = rows.LongCount;
        if (count is < 0 or > 64) return; // not a list the planner could show
        var sheet = _data.GetExcelSheet<LuminaCraftAction>();
        for (long i = 0; i < count; i++)
        {
            var id = rows[i].ActionId;
            if (sheet.TryGetRow(id, out var action) && action.Icon == icon) into.Add(id);
        }
    }

    private bool HasIcon(FFXIVClientStructs.STD.StdVector<AgentCraftActionSimulator.EfficiencyCalculation> rows, uint icon)
    {
        var found = new List<uint>();
        Collect(rows, icon, found);
        return found.Count > 0;
    }

    private static AgentCraftActionSimulator* Agent()
    {
        var module = AgentModule.Instance();
        return module == null
            ? null
            : (AgentCraftActionSimulator*)module->GetAgentByInternalId(AgentId.CraftActionSimulator);
    }

    /// <summary>Icon id of the row's icon component (dump: Comp id=2, Icon), 0 if none.</summary>
    private static uint RowIconId(AtkComponentListItemRenderer* renderer)
    {
        var comp = (AtkComponentBase*)renderer;
        for (var i = 0; i < comp->UldManager.NodeListCount; i++)
        {
            var n = comp->UldManager.NodeList[i];
            if (n == null || (int)n->Type < 1000) continue;
            var inner = ((AtkComponentNode*)n)->Component;
            if (inner != null && inner->GetComponentType() == ComponentType.Icon)
                return ((AtkComponentIcon*)inner)->IconId;
        }
        return 0;
    }

    private static AtkComponentList* ListOf(AtkUnitBase* unit)
    {
        var node = unit->GetNodeById(ListNodeId);
        if (node == null || (int)node->Type < 1000) return null;
        var comp = ((AtkComponentNode*)node)->Component;
        return comp != null && comp->GetComponentType() == ComponentType.List ? (AtkComponentList*)comp : null;
    }

    private static bool IsInList(AtkComponentListItemRenderer* renderer, AtkUnitBase* unit)
    {
        var list = ListOf(unit);
        if (list == null) return false;
        var listNode = (AtkResNode*)((AtkComponentBase*)list)->OwnerNode;
        var node = (AtkResNode*)((AtkComponentBase*)renderer)->OwnerNode;
        for (var up = 0; up < 8 && node != null; up++, node = node->ParentNode)
            if (node == listNode) return true;
        return false;
    }

    private static AtkComponentListItemRenderer* ClimbToRenderer(AtkResNode* node)
    {
        for (var up = 0; up < 8 && node != null; up++, node = node->ParentNode)
        {
            if ((int)node->Type < 1000) continue;
            var comp = ((AtkComponentNode*)node)->Component;
            if (comp != null && comp->GetComponentType() == ComponentType.ListItemRenderer)
                return (AtkComponentListItemRenderer*)comp;
        }
        return null;
    }

    /// <summary>Visible itself and every parent up to <paramref name="root"/>.</summary>
    private static bool IsShown(AtkResNode* node, AtkResNode* root)
    {
        for (var up = 0; up < 16 && node != null; up++, node = node->ParentNode)
        {
            if (!node->IsVisible()) return false;
            if (node == root) return true;
        }
        return true;
    }

    private static string FirstVisibleText(AtkComponentBase* comp, AtkResNode* root)
    {
        for (var i = 0; i < comp->UldManager.NodeListCount; i++)
        {
            var n = comp->UldManager.NodeList[i];
            if (n == null || n->Type != NodeType.Text || !IsShown(n, root)) continue;
            var t = AtkText.ReadClean((AtkTextNode*)n).Trim();
            if (t.Length > 0) return t;
        }
        return string.Empty;
    }
}
