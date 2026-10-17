using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using SheetMap = Lumina.Excel.Sheets.Map;

namespace FF14Accessibility.Services;

public sealed partial class UIReaderService
{
    // Dump 2026-10-16 18:04, addon AetherCurrent. The six cards are list id 8.
    // Text 3 is the zone name (empty and hidden while the game shows no info).
    // Text 4 is the status. Text 5 is "Gebündelt", hidden until the zone is done.
    // Page radios are the top-level radio buttons, left to right = page 1..5.
    private const string AetherAddonName = "AetherCurrent";
    private const uint AetherNameNode = 3;
    private const uint AetherStatusNode = 4;
    private const uint AetherCompiledNode = 5;
    private const uint AetherCurrentIdBase = 2818048;
    private const int AetherCurrentBitCount = 448;
    private const int AetherPageWaitFrames = 30;

    private AetherCurrentCatalog? _aetherCatalog;
    private bool _aetherCatalogFailed;
    private bool _aetherUnlockFailed;
    private bool _aetherBitLogged;
    private bool _aetherCompleteFailed;
    private bool _aetherOpen;
    private bool _aetherTabSeen;
    private int _aetherTab = -1;
    private int _aetherWanted = -1;
    private int _aetherWait;
    private string _aetherLastLine = string.Empty;
    private bool _aetherHintSpoken;
    private bool _aetherTabCompared;
    private bool _aetherLayoutLogged;
    private bool _aetherPageLogged;
    private bool _aetherCompiledLogged;
    // Sets the window showed this session, or that already had a found current.
    // Cleared only when the local player id changes from one non-zero id to another.
    private readonly HashSet<uint> _aetherSeenSets = new();
    private ulong _aetherSeenPlayer;

    private readonly struct AetherCardView
    {
        public string Line { get; init; }
        public bool Revealed { get; init; }
        public bool Complete { get; init; }
        public bool ProgressReadable { get; init; }
        public AetherZone? Zone { get; init; }
    }

    private readonly record struct AetherPageButton(nint Node, float X, bool Checked);

    private unsafe void OnAetherCurrentUpdate(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)(nint)args.Addon;
        if (addon == null || !addon->IsVisible)
        {
            if (_aetherOpen) ResetAetherSpeech();
            return;
        }

        if (!_aetherOpen)
        {
            _aetherOpen = true;
            var late = FindListInAddon(addon);
            if (late != null && GetListEntryCount(late) > 0 && _aetherLastLine.Length == 0)
                SpeakAetherOpen(addon, late);
        }

        WatchAetherTab(addon);
    }

    private void OnAetherCurrentClose(AddonEvent type, AddonArgs args) => ResetAetherSpeech();

    private void ResetAetherSpeech()
    {
        _aetherOpen = false;
        _aetherTabSeen = false;
        _aetherTab = -1;
        _aetherWanted = -1;
        _aetherWait = 0;
        _aetherLastLine = string.Empty;
        _aetherHintSpoken = false;
        _aetherTabCompared = false;
        _aetherLayoutLogged = false;
        _aetherPageLogged = false;
        _aetherCompiledLogged = false;
        _lastListAnnounce.Remove(AetherAddonName);
        // Seen zones stay. Closing the window must not hide a card the game already showed.
    }

    private unsafe void SpeakAetherOpen(AtkUnitBase* addon, AtkComponentList* list)
    {
        _aetherOpen = true;
        var view = ReadAetherCard(addon, list);
        SpeakAetherLine(view.Line, interrupt: false);
        RememberAetherTab(addon);
        if (_aetherHintSpoken) return;
        _aetherHintSpoken = true;
        var hint = AccessibilityStrings.AetherPageHint(KeyNames.Speak(_config.KeyReadUI));
        _log.Info($"[Aether] {hint}");
        _tolk.Speak(hint);
    }

    private unsafe void AnnounceAetherListMove(AtkComponentList* list)
    {
        var addon = VisibleAetherAddon();
        if (addon == null) return;
        var view = ReadAetherCard(addon, list);
        _lastListAnnounce[AetherAddonName] = $"{AetherCardIndex(list)}|{view.Line}";
        SpeakAetherLine(view.Line, interrupt: true);
    }

    private unsafe void AnnounceAetherRadioFocus(AtkResNode* node)
    {
        var addon = FindAddonForNode(node);
        if (addon == null) return;
        var pages = ReadAetherPages(addon);
        if (pages.Count == 0) return;
        var owner = AetherRadioOwner(node);
        if (owner == null) return;
        var index = -1;
        for (var i = 0; i < pages.Count; i++)
        {
            if (pages[i].Node != (nint)owner) continue;
            index = i;
            break;
        }
        if (index < 0) return;
        var line = AccessibilityStrings.AetherPage(index + 1, pages.Count);
        _log.Info($"[Aether] {line}");
        _tolk.SpeakFocusInterrupt(line, (nint)node);
    }

    /// <summary>
    /// Numpad plus or minus, only while this window holds keyboard focus.
    /// The click is the radio's own ButtonClick. Speech waits until the agent tab moves.
    /// </summary>
    public unsafe bool TryChangeAetherPage(int delta)
    {
        if (delta == 0 || !AetherOwnsFocus()) return false;
        var addon = VisibleAetherAddon();
        if (addon == null) return false;
        var pages = ReadAetherPages(addon);
        if (pages.Count == 0)
        {
            _tolk.SpeakInterrupt(AccessibilityStrings.AetherPageUnchanged);
            _log.Info("[Aether] Keine Seitenknöpfe.");
            return true;
        }

        var current = CurrentAetherTab(pages, out _, out _);
        if (current < 0)
        {
            _tolk.SpeakInterrupt(AccessibilityStrings.AetherPageUnchanged);
            _log.Info("[Aether] Aktuelle Seite nicht lesbar.");
            return true;
        }

        var target = current + delta;
        if (target < 0)
        {
            _tolk.SpeakInterrupt(AccessibilityStrings.AetherFirstPage);
            return true;
        }
        if (target >= pages.Count)
        {
            _tolk.SpeakInterrupt(AccessibilityStrings.AetherLastPage);
            return true;
        }
        if (!ClickAetherPage(pages[target].Node))
        {
            _tolk.SpeakInterrupt(AccessibilityStrings.AetherPageUnchanged);
            return true;
        }

        _aetherWanted = target;
        _aetherWait = AetherPageWaitFrames;
        return true;
    }

    /// <summary>Ctrl+F10 while this window is focused. Does not consume the short card line, so the same card can be read again.</summary>
    private unsafe bool TryReadAetherDetail()
    {
        if (!AetherOwnsFocus()) return false;
        var addon = VisibleAetherAddon();
        if (addon == null)
        {
            _tolk.SpeakInterrupt(AccessibilityStrings.AetherNoInformation);
            return true;
        }

        var list = FindListInAddon(addon);
        if (list == null)
        {
            _tolk.SpeakInterrupt(AccessibilityStrings.AetherNoInformation);
            return true;
        }

        var detail = BuildAetherDetail(ReadAetherCard(addon, list));
        _log.Info($"[Aether] Detail: {detail}");
        _tolk.SpeakInterrupt(detail);
        return true;
    }

    private void SpeakAetherLine(string line, bool interrupt)
    {
        if (string.IsNullOrWhiteSpace(line) || line == _aetherLastLine) return;
        _aetherLastLine = line;
        _log.Info($"[Aether] {line}");
        if (interrupt) _tolk.SpeakInterrupt(line);
        else _tolk.Speak(line);
    }

    private unsafe void WatchAetherTab(AtkUnitBase* addon)
    {
        var pages = ReadAetherPages(addon);
        var tab = CurrentAetherTab(pages, out var agent, out var radio);
        var list = FindListInAddon(addon);
        if (!_aetherTabCompared)
        {
            _aetherTabCompared = true;
            _log.Info($"[Aether] Agent-Tab={agent} Radio={radio} Seiten={pages.Count} Liste={(list == null ? -1 : list->ListLength)}");
        }

        if (!_aetherTabSeen)
        {
            _aetherTabSeen = true;
            _aetherTab = tab;
            if (_aetherWanted >= 0 && tab == _aetherWanted && list != null)
            {
                _aetherWanted = -1;
                _aetherWait = 0;
                SpeakAetherLine(ReadAetherCard(addon, list).Line, interrupt: true);
            }
            return;
        }

        if (tab >= 0 && tab != _aetherTab)
        {
            _aetherTab = tab;
            _aetherWanted = -1;
            _aetherWait = 0;
            if (list != null) SpeakAetherLine(ReadAetherCard(addon, list).Line, interrupt: true);
            return;
        }

        if (_aetherWanted < 0) return;
        if (--_aetherWait > 0) return;
        var wanted = _aetherWanted;
        _aetherWanted = -1;
        _tolk.SpeakInterrupt(AccessibilityStrings.AetherPageUnchanged);
        _log.Info($"[Aether] Seite blieb {tab}, Ziel {wanted}. Agent-Tab und Radio stehen in der Zeile oben.");
    }

    private unsafe void RememberAetherTab(AtkUnitBase* addon)
    {
        var pages = ReadAetherPages(addon);
        _aetherTab = CurrentAetherTab(pages, out _, out _);
        _aetherTabSeen = true;
    }

    private unsafe int CurrentAetherTab(List<AetherPageButton> pages, out int agent, out int radio)
    {
        agent = ReadAetherAgentTab();
        radio = CheckedAetherPage(pages);
        if (!_aetherPageLogged && pages.Count > 0 && pages.Count != 5)
        {
            _aetherPageLogged = true;
            _log.Warning($"[Aether] {pages.Count} Seitenknöpfe, erwartet 5. Blattnamen bleiben aus.");
        }
        return agent >= 0 ? agent : radio;
    }

    private unsafe AetherCardView ReadAetherCard(AtkUnitBase* addon, AtkComponentList* list)
    {
        var count = GetListEntryCount(list);
        if (!_aetherLayoutLogged && count > 0 && count != 6)
        {
            _aetherLayoutLogged = true;
            _log.Warning($"[Aether] ListLength={count}, erwartet 6. Blattnamen bleiben aus.");
        }

        var card = AetherCardIndex(list);
        var pages = ReadAetherPages(addon);
        var tab = CurrentAetherTab(pages, out _, out _);
        var page = tab + 1;
        var pageCount = pages.Count;
        var trusted = count == 6 && pageCount == 5 && tab >= 0;
        var setId = trusted ? AetherCurrentIndex.CompFlgSetId(tab, card) : 0;
        var zone = setId == 0 ? null : Catalog()?.Zone(setId);

        var uiName = RendererText(list, card, AetherNameNode, visibleOnly: false);
        var status = RendererText(list, card, AetherStatusNode, visibleOnly: true);
        var compiledText = RendererText(list, card, AetherCompiledNode, visibleOnly: true);
        if (compiledText.Length > 0 && !_aetherCompiledLogged)
        {
            _aetherCompiledLogged = true;
            _log.Info($"[Aether] Knoten Gebündelt: '{compiledText}'");
        }

        var readable = true;
        var found = 0;
        if (zone != null)
            found = CountUnlocked(zone.Currents, out readable);
        var complete = compiledText.Length > 0 || (zone != null && ZoneComplete(setId));
        var revealed = uiName.Length > 0 || (readable && found > 0) || complete;
        if (revealed && setId != 0)
            NoteAetherSet(setId);
        var total = zone?.Currents.Count ?? 0;
        _log.Info($"[Aether] Tab={tab} Karte={card} Satz={setId} sichtbar={revealed} gebuendelt={complete} fund={(readable ? found.ToString() : "?")}/{total}");

        var entry = card + 1;
        string line;
        if (!revealed)
        {
            var shown = status.Length > 0 ? status : AccessibilityStrings.AetherNoInformation;
            line = page >= 1 && pageCount >= 1 && count >= 1
                ? AccessibilityStrings.AetherHiddenCard(page, pageCount, entry, count, shown)
                : shown;
        }
        else
        {
            var zoneName = uiName.Length > 0 ? uiName : SpeakAetherPlace(zone);
            if (page < 1 || pageCount < 1 || count < 1)
                line = complete ? $"{zoneName}. {AccessibilityStrings.AetherCompiled}" : zoneName;
            else if (!readable || zone == null)
                line = AccessibilityStrings.AetherZoneNoCount(page, pageCount, zoneName, entry, count, complete);
            else
                line = AccessibilityStrings.AetherZoneCard(page, pageCount, zoneName, entry, count, found, total, complete);
        }

        return new AetherCardView
        {
            Line = line,
            Revealed = revealed,
            Complete = complete,
            ProgressReadable = readable,
            Zone = revealed ? zone : null,
        };
    }

    private string BuildAetherDetail(AetherCardView view)
    {
        if (!view.Revealed || view.Complete || !view.ProgressReadable || view.Zone == null)
            return view.Line.Length > 0 ? view.Line : AccessibilityStrings.AetherNoInformation;

        var quests = new List<string>();
        var fields = new List<string>();
        foreach (var current in view.Zone.Currents)
        {
            var unlocked = TryUnlocked(current.Id);
            if (unlocked == null) return view.Line;
            if (unlocked.Value) continue;
            var place = DescribeAetherSpot(current.Spot);
            if (current.QuestId != 0)
                quests.Add($"{AccessibilityStrings.AetherQuestSpot(quests.Count + 1, SpeakAetherQuest(current))} {place}");
            else
                fields.Add($"{AccessibilityStrings.AetherFieldSpot(fields.Count + 1)} {place}");
        }

        if (quests.Count == 0 && fields.Count == 0)
            return $"{view.Line} {AccessibilityStrings.AetherNoneMissing}";

        var parts = new List<string> { view.Line, AccessibilityStrings.AetherMissingHeader };
        parts.AddRange(quests);
        parts.AddRange(fields);
        return string.Join(" ", parts);
    }

    private string DescribeAetherSpot(AetherSpot? spot)
    {
        if (spot is not { } place) return AccessibilityStrings.AetherPlaceUnknown;
        var coord = AetherMapCoord(place);
        var player = _objectTable.LocalPlayer;
        var territory = PlayerTerritoryId();
        if (player != null && territory != 0 && territory == place.TerritoryId)
        {
            var from = player.Position;
            var dx = place.X - from.X;
            var dz = place.Z - from.Z;
            var metres = MathF.Sqrt(dx * dx + dz * dz);
            var compass = RouteService.CompassAdjective(from, new Vector3(place.X, from.Y, place.Z));
            return coord is { } here
                ? AccessibilityStrings.AetherHere(metres, compass, here.X, here.Y)
                : AccessibilityStrings.AetherHereNoMap(metres, compass);
        }

        return coord is { } map
            ? AccessibilityStrings.AetherMap(map.X, map.Y)
            : AccessibilityStrings.AetherPlaceUnknown;
    }

    private (float X, float Y)? AetherMapCoord(AetherSpot spot)
    {
        if (spot.MapId == 0) return null;
        if (!_data.GetExcelSheet<SheetMap>().TryGetRow(spot.MapId, out var map)) return null;
        return AetherCurrentIndex.WorldToMap(spot.X, spot.Z, map.SizeFactor, map.OffsetX, map.OffsetY);
    }

    private static string SpeakAetherPlace(AetherZone? zone)
    {
        if (zone == null) return AccessibilityStrings.AetherUnnamedZone;
        var spoken = TolkService.Sanitize(RussianAuthorText.PlaceName(zone.PlaceNameId, zone.SheetPlaceName));
        return spoken.Length == 0 ? AccessibilityStrings.AetherUnnamedZone : spoken;
    }

    private static string SpeakAetherQuest(AetherCurrentEntry entry)
    {
        if (Loc.IsRussian && RussianQuestNames.QuestName(entry.QuestId) is { } russian)
        {
            var clean = TolkService.Sanitize(russian);
            if (clean.Length > 0) return clean;
        }

        var sheet = TolkService.Sanitize(entry.SheetQuestName);
        return sheet.Length == 0 ? AccessibilityStrings.AetherUnnamedQuest : sheet;
    }

    private AetherCurrentCatalog? Catalog()
    {
        if (_aetherCatalogFailed) return null;
        if (_aetherCatalog != null) return _aetherCatalog;
        try
        {
            var started = Environment.TickCount64;
            _aetherCatalog = AetherCurrentCatalog.Build(_data, _log);
            _log.Info($"[Aether] Katalog fertig in {Environment.TickCount64 - started} ms.");
            return _aetherCatalog;
        }
        catch (Exception ex)
        {
            _aetherCatalogFailed = true;
            _log.Warning(ex, "[Aether] Katalog nicht gebaut. Es bleibt beim Fenstertext.");
            return null;
        }
    }

    /// <summary>
    /// Missing currents for the object browser. <paramref name="playerTerritory"/> is the
    /// territory Num3 uses, so the list order and the hop check stay on the same number.
    /// </summary>
    internal AetherWalkReport MissingAetherWalks(uint playerTerritory)
    {
        RememberAetherPlayer();
        try
        {
            var catalog = Catalog();
            if (catalog == null) return UnreadableAetherWalks();
            Vector2? playerAt = null;
            var player = _objectTable.LocalPlayer;
            if (player != null) playerAt = new Vector2(player.Position.X, player.Position.Z);
            var readable = AetherWalkList.TryCollect(
                catalog.Zones, _aetherSeenSets, playerTerritory, playerAt, TryUnlocked, out var rows);
            if (!readable) return UnreadableAetherWalks();

            var targets = new List<AetherWalkTarget>(rows.Count);
            foreach (var row in rows)
            {
                var name = row.Current.QuestId != 0
                    ? AccessibilityStrings.AetherWalkQuest(SpeakAetherQuest(row.Current))
                    : AccessibilityStrings.AetherWalkField;
                var zoneName = row.SpeakZoneName ? SpeakAetherPlace(row.Zone) : string.Empty;
                if (!AetherWalkList.TryToTarget(row, name, zoneName, out var target))
                    continue;
                targets.Add(target);
            }

            _log.Info($"[Aether] Lauf lesbar ziele={targets.Count} gesehen={_aetherSeenSets.Count}");
            return new AetherWalkReport(true, targets);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[Aether] Laufliste nicht gebaut.");
            return UnreadableAetherWalks();
        }
    }

    private AetherWalkReport UnreadableAetherWalks()
    {
        _log.Info("[Aether] Lauf nicht lesbar.");
        return new AetherWalkReport(false, Array.Empty<AetherWalkTarget>());
    }

    private void NoteAetherSet(uint setId)
    {
        if (setId == 0 || !RememberAetherPlayer()) return;
        if (_aetherSeenSets.Add(setId))
            _log.Info($"[Aether] Gebiet {setId} fuer den Lauf gemerkt.");
    }

    /// <summary>
    /// Tracks the local player. A change from one non-zero id to another forgets
    /// the zones this window showed. Id 0 during a zone load does not clear them.
    /// </summary>
    /// <returns>false while the player id is 0, so a reveal is not stored yet.</returns>
    private bool RememberAetherPlayer()
    {
        var id = _objectTable.LocalPlayer?.GameObjectId ?? 0;
        if (id == 0) return false;
        if (_aetherSeenPlayer != 0 && _aetherSeenPlayer != id)
        {
            _aetherSeenSets.Clear();
            _log.Info("[Aether] Andere Figur. Gesehene Gebiete vergessen.");
        }

        _aetherSeenPlayer = id;
        return true;
    }

    private int CountUnlocked(IReadOnlyList<AetherCurrentEntry> currents, out bool readable)
    {
        readable = true;
        var found = 0;
        foreach (var current in currents)
        {
            var state = TryUnlocked(current.Id);
            if (state == null)
            {
                readable = false;
                return 0;
            }
            if (state.Value) found++;
        }
        return found;
    }

    // The bit array is managed. A failed native zone-complete signature is not.
    // That call is guarded like FlightService: a broken signature must not be spoken as "not collected".
    private unsafe bool? TryUnlocked(uint id)
    {
        if (_aetherUnlockFailed) return null;
        if (id < AetherCurrentIdBase) return false;
        if (id - AetherCurrentIdBase >= AetherCurrentBitCount)
        {
            if (!_aetherBitLogged)
            {
                _aetherBitLogged = true;
                _log.Warning($"[Aether] Quelle {id} liegt ausserhalb der {AetherCurrentBitCount} Bits.");
            }
            return false;
        }

        try
        {
            var state = PlayerState.Instance();
            if (state == null) return null;
            return state->IsAetherCurrentUnlocked(id);
        }
        catch (Exception ex)
        {
            _aetherUnlockFailed = true;
            _log.Warning(ex, "[Aether] IsAetherCurrentUnlocked fehlgeschlagen. Fundzahlen bleiben aus.");
            return null;
        }
    }

    private unsafe bool ZoneComplete(uint setId)
    {
        if (_aetherCompleteFailed || setId == 0) return false;
        try
        {
            var state = PlayerState.Instance();
            if (state == null) return false;
            return state->IsAetherCurrentZoneComplete(setId);
        }
        catch (Exception ex)
        {
            _aetherCompleteFailed = true;
            _log.Warning(ex, "[Aether] IsAetherCurrentZoneComplete fehlgeschlagen. Gebündelt kommt nur noch aus dem Fenster.");
            return false;
        }
    }

    private unsafe uint PlayerTerritoryId()
    {
        var main = GameMain.Instance();
        return main == null ? 0 : main->CurrentTerritoryTypeId;
    }

    private unsafe bool AetherOwnsFocus()
    {
        var stage = AtkStage.Instance();
        if (stage == null || stage->AtkInputManager == null) return false;
        var node = stage->AtkInputManager->FocusedNode;
        return node != null && FindAddonNameForNode(node) == AetherAddonName;
    }

    private unsafe AtkUnitBase* VisibleAetherAddon()
    {
        var ptr = _gameGui.GetAddonByName(AetherAddonName);
        if (ptr.IsNull) return null;
        var addon = (AtkUnitBase*)(nint)ptr;
        return addon->IsVisible ? addon : null;
    }

    private unsafe int ReadAetherAgentTab()
    {
        var agent = AgentAetherCurrent.Instance();
        return agent == null ? -1 : agent->TabIndex;
    }

    private unsafe List<AetherPageButton> ReadAetherPages(AtkUnitBase* addon)
    {
        var pages = new List<AetherPageButton>();
        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null || (int)node->Type < 1000 || !node->IsVisible()) continue;
            var comp = ((AtkComponentNode*)node)->Component;
            if (comp == null || comp->GetComponentType() != ComponentType.RadioButton) continue;
            pages.Add(new AetherPageButton((nint)node, node->ScreenX, ((AtkComponentButton*)comp)->IsChecked));
        }
        pages.Sort((a, b) => a.X.CompareTo(b.X));
        return pages;
    }

    private static int CheckedAetherPage(List<AetherPageButton> pages)
    {
        for (var i = 0; i < pages.Count; i++)
            if (pages[i].Checked) return i;
        return -1;
    }

    private unsafe bool ClickAetherPage(nint nodePtr)
    {
        var node = (AtkResNode*)nodePtr;
        var evt = FindEventOfType(node, AtkEventType.ButtonClick, new List<string>(), 2);
        if (evt == null || evt->Listener == null || !IsReadable(evt->Listener))
        {
            _log.Info("[Aether] Seite ohne ButtonClick.");
            return false;
        }

        var data = default(AtkEventData);
        evt->Listener->ReceiveEvent(AtkEventType.ButtonClick, (int)evt->Param, evt, &data);
        _log.Info($"[Aether] ButtonClick Seite, param={evt->Param}.");
        return true;
    }

    private unsafe bool IsInsideAetherList(AtkResNode* node)
    {
        for (var depth = 0; node != null && depth < 12; depth++, node = node->ParentNode)
        {
            if ((int)node->Type < 1000) continue;
            var comp = ((AtkComponentNode*)node)->Component;
            if (comp != null && IsListComponent(comp->GetComponentType())) return true;
        }
        return false;
    }

    private unsafe AtkResNode* AetherRadioOwner(AtkResNode* node)
    {
        for (var depth = 0; node != null && depth < 8; depth++, node = node->ParentNode)
        {
            if ((int)node->Type < 1000) continue;
            var comp = ((AtkComponentNode*)node)->Component;
            if (comp != null && comp->GetComponentType() == ComponentType.RadioButton)
                return node;
        }
        return null;
    }

    private static unsafe int AetherCardIndex(AtkComponentList* list)
    {
        var count = GetListEntryCount(list);
        var idx = list->HoveredItemIndex >= 0 ? list->HoveredItemIndex
            : list->HoveredItemIndex2 >= 0 ? list->HoveredItemIndex2 : 0;
        if (idx < 0) return 0;
        if (count > 0 && idx >= count) return count - 1;
        return idx;
    }

    private unsafe string RendererText(AtkComponentList* list, int card, uint nodeId, bool visibleOnly)
    {
        if (card < 0 || card >= list->AllocatedItemRendererListLength) return string.Empty;
        try
        {
            var slot = &list->ItemRendererList[card];
            if (!IsReadable(slot)) return string.Empty;
            var renderer = (*slot).AtkComponentListItemRenderer;
            if (renderer == null || !IsReadable(renderer)) return string.Empty;
            var comp = (AtkComponentBase*)renderer;
            for (var i = 0; i < comp->UldManager.NodeListCount; i++)
            {
                var node = comp->UldManager.NodeList[i];
                if (node == null || node->Type != NodeType.Text || node->NodeId != nodeId) continue;
                if (visibleOnly && !node->IsVisible()) continue;
                var text = TolkService.Sanitize(AtkText.Read((AtkTextNode*)node)).Trim();
                if (text.Length > 0) return text;
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"[Aether] Kartentext {nodeId}: {ex.Message}");
        }
        return string.Empty;
    }
}
