using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FF14Accessibility.Services;

/// <summary>
/// Line-by-line cursor over the Profile tab of the character window
/// (addon <c>CharacterProfile</c>).
/// <para>
/// The tab shows six facts (title, Grand Company, Free Company, race/tribe/
/// gender, starting city, name day, guardian deity), but only two of them are
/// buttons the game's cursor can reach: the title button and the Grand Company
/// button. NUM8 and NUM2 just toggle between those two (log 2026-10-06
/// 19:52:00-19:52:09, 24 presses, never anywhere else), so a blind player never
/// reached name day, city or deity at all.
/// </para>
/// <para>
/// While the game's focus is inside this tab, the plugin takes NUM8/NUM2 (user
/// decision 2026-10-06) and walks every visible line top to bottom, wrapping
/// at both ends. On the two button lines the game's own focus is moved onto
/// the button (<see cref="AtkUnitBase.SetFocusNode"/>), so NUM0 still presses
/// it. On plain text lines NUM0 is swallowed - there is nothing to press, and
/// letting it through would press whichever button still holds the focus.
/// </para>
/// <para>
/// Node ids (dump 2026-10-06 06:32, Desktop\FFXIV_UI_Dump_Profil.txt), all in
/// the addon's flat NodeList: each line is a Res group with a heading text and
/// a value text. The Free Company group (Res id=12) is hidden for a character
/// without one and is skipped then; its filled layout is NOT measured.
/// </para>
/// </summary>
public sealed unsafe class CharacterProfileHandler
{
    private const string AddonName = "CharacterProfile";

    /// <summary>One profile line: group to check visibility on, heading, value
    /// texts (joined), and the button component that line owns, if any.</summary>
    private readonly record struct Line(uint Group, uint Heading, uint[] Values, uint Button, ButtonKind Kind);

    private enum ButtonKind { None, Title, GrandCompany }

    // Screen order, top to bottom (y from the dump: 190, 245, 245, 306, 356, 416, 466).
    private static readonly Line[] Lines =
    [
        // Title: "Titel" id=3, name/title text id=6 (and id=5, hidden when no
        // title is set), button "Andere Titel" Comp id=7 (label = its tooltip).
        new(2,  3,  [5, 6], 7,  ButtonKind.Title),
        // Grand Company: heading id=9, button Comp id=11 carries the company
        // (text id=4) and the rank (text id=5) itself.
        new(8,  9,  [],     11, ButtonKind.GrandCompany),
        // Free Company: heading id=13, value id=16. Hidden without a company.
        new(12, 13, [16],   0,  ButtonKind.None),
        new(17, 18, [20],   0,  ButtonKind.None),   // Volk / Stamm / Geschlecht
        new(21, 22, [25],   0,  ButtonKind.None),   // Anfangsstadt
        new(26, 27, [29],   0,  ButtonKind.None),   // Namenstag
        new(30, 31, [34],   0,  ButtonKind.None),   // Schutzgottheit
    ];

    private readonly IGameGui _gameGui;
    private readonly TolkService _tolk;
    private readonly TooltipService _tooltips;
    private readonly IPluginLog _log;

    // Position in Lines, -1 = not placed yet in this tab instance.
    private int _index = -1;
    // The tab is rebuilt on every switch to it; a new instance starts fresh.
    private nint _addon;

    /// <summary>Creates the handler.</summary>
    public CharacterProfileHandler(IGameGui gameGui, TolkService tolk, TooltipService tooltips, IPluginLog log)
    {
        _gameGui  = gameGui;
        _tolk     = tolk;
        _tooltips = tooltips;
        _log      = log;
    }

    /// <summary>
    /// True while the profile tab is visible AND the game's focus sits inside
    /// it. Only then do NUM8/NUM2 belong to this cursor - with the title list or
    /// the Grand Company rank window open on top, the focus is there and the
    /// keys go back to the game.
    /// </summary>
    public bool IsActive
    {
        get
        {
            var addon = GetAddon();
            if (addon == null) return false;
            var focus = GetGameFocus();
            return focus != null && IsInside(addon, focus);
        }
    }

    /// <summary>True when the cursor stands on a plain text line, where NUM0
    /// has nothing to press.</summary>
    public bool IsOnTextLine => _index >= 0 && _index < Lines.Length && Lines[_index].Kind == ButtonKind.None;

    /// <summary>
    /// Moves the cursor one visible line up (-1) or down (+1), wrapping at
    /// both ends, and speaks that line.
    /// </summary>
    public void Move(int delta)
    {
        var addon = GetAddon();
        if (addon == null) return;
        if ((nint)addon != _addon)
        {
            _addon = (nint)addon;
            _index = -1;
        }

        var visible = new List<int>();
        for (var i = 0; i < Lines.Length; i++)
            if (IsGroupVisible(addon, Lines[i].Group)) visible.Add(i);
        if (visible.Count == 0)
        {
            _log.Warning("[Profil] Keine sichtbare Zeile gefunden.");
            return;
        }

        // Start from where the game's focus is, so the first press continues
        // from the button the player already heard.
        if (_index < 0) _index = FindLineOfFocus(addon);

        var pos = visible.IndexOf(_index);
        if (pos < 0) pos = delta > 0 ? -1 : visible.Count;
        pos = ((pos + delta) % visible.Count + visible.Count) % visible.Count;
        _index = visible[pos];

        var line = Lines[_index];
        var text = DescribeLine(addon, line);
        _log.Info($"[Profil] Zeile {pos + 1}/{visible.Count}: '{text}'");
        _tolk.SpeakInterrupt(text);

        if (line.Kind != ButtonKind.None) FocusButton(addon, line);
    }

    /// <summary>
    /// Full line text for a focused node inside the profile tab when it belongs
    /// to one of the two buttons ("Staatliche Gesellschaft: Bruderschaft der
    /// Morgenviper, Zweiter Novize"), else null. Used by the global focus
    /// reader, which said only the company name or "Andere Titel" before. Also
    /// keeps this cursor on that line, so a mouse or game move is followed.
    /// </summary>
    public string? DescribeFocusedButton(AtkResNode* node)
    {
        var addon = GetAddon();
        if (addon == null || node == null || !IsInside(addon, node)) return null;

        for (var i = 0; i < Lines.Length; i++)
        {
            var line = Lines[i];
            if (line.Kind == ButtonKind.None) continue;
            var comp = addon->GetNodeById(line.Button);
            if (comp == null || !IsSelfOrAncestor(comp, node)) continue;
            if ((nint)addon != _addon) _addon = (nint)addon;
            _index = i;
            return DescribeLine(addon, line);
        }
        return null;
    }

    private string DescribeLine(AtkUnitBase* addon, Line line)
    {
        var heading = ReadText(addon->GetNodeById(line.Heading));
        string value;
        switch (line.Kind)
        {
            case ButtonKind.GrandCompany:
            {
                var comp = GetComponent(addon, line.Button);
                var name = comp != null ? ReadText(FindInComponent(comp, 4)) : string.Empty;
                var rank = comp != null ? ReadText(FindInComponent(comp, 5)) : string.Empty;
                value = Join(", ", name, rank);
                break;
            }
            default:
            {
                var parts = new List<string>();
                foreach (var id in line.Values) parts.Add(ReadText(addon->GetNodeById(id)));
                value = Join(", ", parts.ToArray());
                break;
            }
        }

        var text = value.Length > 0 ? AccessibilityStrings.ProfileEntry(heading, value) : heading;
        if (line.Kind == ButtonKind.Title)
        {
            // The button has no text of its own; the game binds "Andere Titel"
            // to it as a tooltip (log 2026-10-06 18:06:22).
            var label = _tooltips.TryGetTooltipDeep(addon->GetNodeById(line.Button)) ?? string.Empty;
            if (label.Length > 0) text = $"{text}. {label}";
        }
        return text;
    }

    /// <summary>
    /// Puts the game's own focus on the line's button, so NUM0 presses it.
    /// The target is the button's Collision child - that is the node the
    /// game's focus sits on when it reaches the button itself (log 2026-10-06:
    /// id=3 "Andere Titel", id=6 "Bruderschaft der Morgenviper").
    /// SetFocusNode is used nowhere else in the plugin yet: before/after are
    /// logged until the first in-game test shows it moves the real cursor.
    /// </summary>
    private void FocusButton(AtkUnitBase* addon, Line line)
    {
        var comp = GetComponent(addon, line.Button);
        if (comp == null) return;
        AtkResNode* target = null;
        for (var i = 0; i < comp->UldManager.NodeListCount; i++)
        {
            var n = comp->UldManager.NodeList[i];
            if (n != null && n->Type == NodeType.Collision) { target = n; break; }
        }
        if (target == null)
        {
            _log.Warning($"[Profil] Knopf id={line.Button} ohne Collision-Knoten.");
            return;
        }

        var before = (nint)GetGameFocus();
        if (before == (nint)target) return;
        try
        {
            var ok = addon->SetFocusNode(target, true);
            _log.Info($"[Profil] SetFocusNode id={line.Button}: ok={ok} vorher={before:X} " +
                      $"nachher={(nint)GetGameFocus():X} ziel={(nint)target:X} " +
                      $"FocusNode={(nint)addon->FocusNode:X} CursorTarget={(nint)addon->CursorTarget:X}");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[Profil] SetFocusNode fehlgeschlagen.");
            _tolk.Speak(AccessibilityStrings.ProfileFocusFailed);
        }
    }

    /// <summary>Line whose button holds the game's focus, else -1.</summary>
    private int FindLineOfFocus(AtkUnitBase* addon)
    {
        var focus = GetGameFocus();
        if (focus == null) return -1;
        for (var i = 0; i < Lines.Length; i++)
        {
            if (Lines[i].Kind == ButtonKind.None) continue;
            var comp = addon->GetNodeById(Lines[i].Button);
            if (comp != null && IsSelfOrAncestor(comp, focus)) return i;
        }
        return -1;
    }

    private AtkUnitBase* GetAddon()
    {
        var handle = _gameGui.GetAddonByName(AddonName);
        if (handle.IsNull) return null;
        var addon = (AtkUnitBase*)(nint)handle;
        return addon != null && addon->IsVisible && addon->IsReady ? addon : null;
    }

    private static AtkResNode* GetGameFocus()
    {
        var stage = AtkStage.Instance();
        if (stage == null || stage->AtkInputManager == null) return null;
        return stage->AtkInputManager->FocusedNode;
    }

    private static AtkComponentBase* GetComponent(AtkUnitBase* addon, uint id)
    {
        var node = addon->GetNodeById(id);
        if (node == null || (int)node->Type < 1000) return null;
        return ((AtkComponentNode*)node)->Component;
    }

    /// <summary>A node belongs to the addon when its topmost ancestor is the
    /// addon's root node.</summary>
    private static bool IsInside(AtkUnitBase* addon, AtkResNode* node)
    {
        var root = node;
        for (var guard = 0; root->ParentNode != null && guard < 64; guard++) root = root->ParentNode;
        return root == addon->RootNode;
    }

    private static bool IsSelfOrAncestor(AtkResNode* ancestor, AtkResNode* node)
    {
        for (var cur = node; cur != null; cur = cur->ParentNode)
            if (cur == ancestor) return true;
        return false;
    }

    /// <summary>The group and every parent above it must be visible - a
    /// hidden group's texts stay flagged visible (dump: Free Company id=12).</summary>
    private static bool IsGroupVisible(AtkUnitBase* addon, uint id)
    {
        var node = addon->GetNodeById(id);
        if (node == null) return false;
        for (var cur = node; cur != null; cur = cur->ParentNode)
            if (!cur->IsVisible()) return false;
        return true;
    }

    /// <summary>Node with this id in a component's flat node list, or null.</summary>
    private static AtkResNode* FindInComponent(AtkComponentBase* comp, uint id)
    {
        for (var i = 0; i < comp->UldManager.NodeListCount; i++)
        {
            var n = comp->UldManager.NodeList[i];
            if (n != null && n->NodeId == id) return n;
        }
        return null;
    }

    private static string ReadText(AtkResNode* node)
    {
        if (node == null || node->Type != NodeType.Text || !node->IsVisible()) return string.Empty;
        return TolkService.Sanitize(AtkText.ReadClean((AtkTextNode*)node)).Trim();
    }

    private static string Join(string sep, params string[] parts)
    {
        var kept = new List<string>();
        foreach (var p in parts) if (!string.IsNullOrWhiteSpace(p)) kept.Add(p);
        return string.Join(sep, kept);
    }
}
