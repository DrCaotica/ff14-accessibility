using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace FF14Accessibility.Services;

/// <summary>
/// One current's world spot. Y comes from the sheet and is kept for the walk.
/// Speech uses X and Z only. The map row belongs to this spot, not to the player's current map.
/// </summary>
internal readonly record struct AetherSpot(uint TerritoryId, uint MapId, float X, float Y, float Z);

/// <summary>One sheet current. Quest id 0 is a field orb. The quest name is the sheet text, resolved again at speech time.</summary>
internal readonly record struct AetherCurrentEntry(uint Id, uint QuestId, string SheetQuestName, AetherSpot? Spot);

/// <summary>One zone set. The spoken place name is chosen when the card is read, so a language switch is not frozen at build time.</summary>
internal sealed class AetherZone
{
    public AetherZone(uint setId, uint territoryId, uint placeNameId, string sheetPlaceName, IReadOnlyList<AetherCurrentEntry> currents)
    {
        SetId = setId;
        TerritoryId = territoryId;
        PlaceNameId = placeNameId;
        SheetPlaceName = sheetPlaceName;
        Currents = currents;
    }

    public uint SetId { get; }
    public uint TerritoryId { get; }
    public uint PlaceNameId { get; }
    public string SheetPlaceName { get; }
    public IReadOnlyList<AetherCurrentEntry> Currents { get; }
}

/// <summary>
/// Aether current sheets, read once. Field spots are the Level of the EObj whose
/// <c>Data.RowId</c> is the current, and only when that Level is in the set's territory.
/// Quest spots are <c>Quest.IssuerLocation</c>. A missing spot stays unknown.
/// </summary>
internal sealed class AetherCurrentCatalog
{
    private readonly Dictionary<uint, AetherZone> _zones;
    private readonly AetherZone[] _ordered;

    private AetherCurrentCatalog(Dictionary<uint, AetherZone> zones)
    {
        _zones = zones;
        var ordered = new List<AetherZone>(zones.Values);
        ordered.Sort(static (a, b) => a.SetId.CompareTo(b.SetId));
        _ordered = ordered.ToArray();
    }

    public AetherZone? Zone(uint setId) => _zones.TryGetValue(setId, out var zone) ? zone : null;

    /// <summary>Every zone that has at least one current, in set-id order.</summary>
    public IReadOnlyList<AetherZone> Zones => _ordered;

    public static AetherCurrentCatalog Build(IDataManager data, IPluginLog log)
    {
        var drafts = new List<DraftZone>();
        var territoryOf = new Dictionary<uint, uint>();
        foreach (var set in data.GetExcelSheet<AetherCurrentCompFlgSet>())
        {
            var draft = ReadZone(set);
            if (draft.Currents.Count == 0) continue;
            drafts.Add(draft);
            foreach (var current in draft.Currents)
                territoryOf[current.Id] = draft.TerritoryId;
        }

        var eobjOf = new Dictionary<uint, uint>();
        foreach (var row in data.GetExcelSheet<EObj>())
        {
            var id = row.Data.RowId;
            if (id == 0 || !territoryOf.ContainsKey(id)) continue;
            eobjOf.TryAdd(row.RowId, id);
        }

        var fieldSpots = new Dictionary<uint, AetherSpot>();
        var extraSpots = 0;
        foreach (var level in data.GetExcelSheet<Level>())
        {
            if (!eobjOf.TryGetValue(level.Object.RowId, out var currentId)) continue;
            if (!territoryOf.TryGetValue(currentId, out var territory) || level.Territory.RowId != territory) continue;
            var spot = new AetherSpot(territory, level.Map.RowId, level.X, level.Y, level.Z);
            if (!fieldSpots.TryAdd(currentId, spot)) extraSpots++;
        }

        var quests = data.GetExcelSheet<Quest>();
        var zones = new Dictionary<uint, AetherZone>();
        var fieldCount = 0;
        var fieldWithSpot = 0;
        var questCount = 0;
        foreach (var draft in drafts)
        {
            var currents = new List<AetherCurrentEntry>(draft.Currents.Count);
            foreach (var current in draft.Currents)
            {
                var spot = current.QuestId == 0
                    ? FieldSpot(fieldSpots, current.Id)
                    : QuestSpot(quests, current.QuestId) ?? FieldSpot(fieldSpots, current.Id);
                if (current.QuestId == 0)
                {
                    fieldCount++;
                    if (spot != null) fieldWithSpot++;
                }
                else questCount++;
                currents.Add(current with { Spot = spot });
            }
            zones[draft.SetId] = new AetherZone(draft.SetId, draft.TerritoryId, draft.PlaceNameId, draft.SheetPlaceName, currents);
        }

        log.Info($"[Aether] Katalog: Zonen={zones.Count} Felder={fieldCount} mitOrt={fieldWithSpot} Auftraege={questCount} extraOrte={extraSpots}");
        return new AetherCurrentCatalog(zones);
    }

    private static AetherSpot? FieldSpot(Dictionary<uint, AetherSpot> spots, uint currentId)
        => spots.TryGetValue(currentId, out var spot) ? spot : null;

    private static AetherSpot? QuestSpot(Lumina.Excel.ExcelSheet<Quest> quests, uint questId)
    {
        if (questId == 0 || !quests.TryGetRow(questId, out var quest)) return null;
        if (quest.IssuerLocation.ValueNullable is not { } level) return null;
        var territory = level.Territory.RowId;
        if (territory == 0) return null;
        if (level.X == 0f && level.Y == 0f && level.Z == 0f) return null;
        return new AetherSpot(territory, level.Map.RowId, level.X, level.Y, level.Z);
    }

    private static DraftZone ReadZone(AetherCurrentCompFlgSet set)
    {
        var placeNameId = 0u;
        var placeName = string.Empty;
        if (set.Territory.ValueNullable is { } territory)
        {
            placeNameId = territory.PlaceName.RowId;
            placeName = territory.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty;
        }

        var currents = new List<AetherCurrentEntry>();
        foreach (var current in set.AetherCurrents)
        {
            if (current.RowId == 0 || current.ValueNullable is not { } row) continue;
            var questId = row.Quest.RowId;
            var questName = questId == 0 ? string.Empty : row.Quest.ValueNullable?.Name.ExtractText() ?? string.Empty;
            currents.Add(new AetherCurrentEntry(current.RowId, questId, questName, null));
        }

        return new DraftZone(set.RowId, set.Territory.RowId, placeNameId, placeName, currents);
    }

    private sealed class DraftZone
    {
        public DraftZone(uint setId, uint territoryId, uint placeNameId, string sheetPlaceName, List<AetherCurrentEntry> currents)
        {
            SetId = setId;
            TerritoryId = territoryId;
            PlaceNameId = placeNameId;
            SheetPlaceName = sheetPlaceName;
            Currents = currents;
        }

        public uint SetId { get; }
        public uint TerritoryId { get; }
        public uint PlaceNameId { get; }
        public string SheetPlaceName { get; }
        public List<AetherCurrentEntry> Currents { get; }
    }
}
