using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace FF14Accessibility.Services;

/// <summary>One walk-in volume named by a quest battle, in world coordinates.</summary>
public readonly record struct QuestBattleTrigger(
    uint QuestId, uint LevelId, ushort Territory, uint MapId,
    float X, float Y, float Z, float Radius);

/// <summary>
/// Reads the sheet links once: <c>EObj.Data</c> when it points at a quest,
/// <c>EOBJECT</c> arguments on the quest, and type-45 / type-49 Level rows
/// named by that quest's <c>QuestBattle</c>. Quest acceptance is not cached.
/// </summary>
public sealed class QuestObjectCatalog
{
    private readonly IDataManager _data;
    private readonly IPluginLog _log;
    private bool _ready;
    private List<QuestObjectLink> _links = new();
    private List<QuestBattleTrigger> _triggers = new();
    private List<(uint BaseId, uint QuestId)> _battlePairs = new();

    public QuestObjectCatalog(IDataManager data, IPluginLog log)
    {
        _data = data;
        _log = log;
    }

    public IReadOnlyList<QuestObjectLink> Links
    {
        get
        {
            EnsureBuilt();
            return _links;
        }
    }

    public IReadOnlyList<QuestBattleTrigger> Triggers
    {
        get
        {
            EnsureBuilt();
            return _triggers;
        }
    }

    /// <summary>Quest row of a battle prop, or 0 when none or more than one.</summary>
    public uint BattleQuestId(uint baseId)
    {
        if (baseId == 0) return 0;
        EnsureBuilt();
        return QuestObjectLinks.UniqueQuest(_battlePairs, baseId);
    }

    public IEnumerable<uint> QuestIds()
    {
        EnsureBuilt();
        var ids = new HashSet<uint>();
        foreach (var link in _links)
            if (link.QuestId != 0) ids.Add(link.QuestId);
        foreach (var trigger in _triggers)
            if (trigger.QuestId != 0) ids.Add(trigger.QuestId);
        return ids;
    }

    private void EnsureBuilt()
    {
        if (_ready) return;
        Build();
        _ready = true;
    }

    private void Build()
    {
        var links = new List<QuestObjectLink>();
        var triggers = new List<QuestBattleTrigger>();
        var battlePairs = new List<(uint BaseId, uint QuestId)>();

        foreach (var row in _data.GetExcelSheet<EObj>())
        {
            var data = row.Data;
            if (data.RowId == 0 || !data.Is<Quest>()) continue;
            links.Add(new QuestObjectLink(data.RowId, row.RowId, 0, false));
        }

        foreach (var quest in _data.GetExcelSheet<Quest>())
        {
            foreach (var param in quest.QuestParams)
            {
                var link = QuestObjectLinks.FromQuestArgument(
                    quest.RowId, InstructionText(param.ScriptInstruction), Argument(param.ScriptArg));
                if (link != null) links.Add(link.Value);
            }
        }

        var levels = _data.GetExcelSheet<Level>();
        foreach (var battle in _data.GetExcelSheet<QuestBattle>())
        {
            var questId = battle.Quest.RowId;
            if (questId == 0) continue;

            foreach (var param in battle.QuestBattleParams)
            {
                var levelId = Argument(param.ScriptValue);
                if (levelId == 0 || !levels.TryGetRow(levelId, out var level)) continue;

                var territoryId = level.Territory.RowId;
                if (territoryId == 0 || territoryId > ushort.MaxValue) continue;
                var territory = (ushort)territoryId;

                var link = QuestObjectLinks.FromBattleLevel(
                    questId, level.Type, level.Object.RowId, territory);
                if (link != null)
                {
                    links.Add(link.Value);
                    battlePairs.Add((link.Value.BaseId, questId));
                }

                if (!QuestObjectLinks.IsBattleTrigger(questId, level.Type, territory)) continue;
                triggers.Add(new QuestBattleTrigger(
                    questId, level.RowId, territory, level.Map.RowId,
                    level.X, level.Y, level.Z, level.Radius));
            }
        }

        _links = links;
        _triggers = triggers;
        _battlePairs = battlePairs;
        _log.Info($"[Quest] Blatt-Verknüpfungen: {links.Count} Objekte, {triggers.Count} Auslöser.");
    }

    // ToString() on the sheet's ReadOnlySeString is the instruction itself
    // ("EOBJECT0"). Measured against the installed sheets, not assumed.
    private static string InstructionText(object? value) => value?.ToString()?.Trim() ?? string.Empty;

    private static uint Argument(object? value) => value switch
    {
        uint number => number,
        int number when number > 0 => (uint)number,
        ushort number => number,
        _ => 0,
    };
}
