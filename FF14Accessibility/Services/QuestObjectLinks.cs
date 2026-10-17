using System.Collections.Generic;

namespace FF14Accessibility.Services;

/// <summary>
/// One event object the game's own sheets name as part of a quest.
/// <paramref name="LimitToTerritory"/> is set for a prop that lives in a
/// quest battle. The same data id must not light up in another zone.
/// A link from <c>EObj.Data</c> is not limited: that row is the object,
/// and it is only loaded where it stands.
/// </summary>
public readonly record struct QuestObjectLink(
    uint QuestId, uint BaseId, ushort Territory, bool LimitToTerritory);

/// <summary>
/// Decides which sheet rows belong in the quest-object list.
/// The sheets themselves are read elsewhere. This class only applies the
/// measured rule, so it can be checked without the game client.
/// </summary>
public static class QuestObjectLinks
{
    /// <summary>Lumina <c>Level.Type</c> for an event object.</summary>
    public const byte EventObject = 45;

    /// <summary>Lumina <c>Level.Type</c> for a walk-in volume.</summary>
    public const byte EventRange = 49;

    /// <summary>
    /// A quest script argument whose instruction starts with <c>EOBJECT</c>
    /// names an <c>EObj</c> row. Measured on quest 67124: <c>EOBJECT0</c>
    /// is 2005408, the footprint whose <c>EObj.Data</c> is that quest.
    /// A battle argument such as <c>EOBJ_FOOTPRINT_01</c> is not this form.
    /// Its value is a Level row and is resolved separately.
    /// </summary>
    public static QuestObjectLink? FromQuestArgument(uint questId, string? instruction, uint argument)
    {
        if (questId == 0 || argument == 0 || string.IsNullOrEmpty(instruction)) return null;
        if (!instruction.StartsWith("EOBJECT", System.StringComparison.Ordinal)) return null;
        return new QuestObjectLink(questId, argument, 0, false);
    }

    /// <summary>
    /// A quest-battle script value that resolves to a Level row of type 45
    /// names that row's object. Measured on QuestBattle 111: the three duty
    /// footprints and the hiding place are type 45, and their <c>EObj.Data</c>
    /// is empty. Enemies on the same battle are type 9 and are not objects.
    /// </summary>
    public static QuestObjectLink? FromBattleLevel(
        uint questId, byte levelType, uint objectId, ushort territory)
    {
        if (questId == 0 || levelType != EventObject || objectId == 0 || territory == 0) return null;
        return new QuestObjectLink(questId, objectId, territory, true);
    }

    /// <summary>
    /// A quest-battle Level row of type 49 is a walk-in volume of that quest.
    /// Measured: the hiding-place range of QuestBattle 111 has no
    /// <c>Level.EventId</c>, so the marker-circle index never sees it.
    /// </summary>
    public static bool IsBattleTrigger(uint questId, byte levelType, ushort territory)
        => questId != 0 && levelType == EventRange && territory != 0;

    /// <summary>
    /// Data ids that belong to an accepted quest and apply in
    /// <paramref name="territory"/>. An empty accepted set adds nothing.
    /// </summary>
    public static HashSet<uint> BaseIds(
        IEnumerable<QuestObjectLink> links, IReadOnlySet<uint> accepted, ushort territory)
    {
        var ids = new HashSet<uint>();
        if (accepted.Count == 0) return ids;

        foreach (var link in links)
        {
            if (link.BaseId == 0 || link.QuestId == 0) continue;
            if (!accepted.Contains(link.QuestId)) continue;
            if (link.LimitToTerritory && link.Territory != territory) continue;
            ids.Add(link.BaseId);
        }

        return ids;
    }

    /// <summary>
    /// The quest a battle prop belongs to, or 0 when several quests claim it.
    /// Used only for the spoken name. The category still checks each quest.
    /// </summary>
    public static uint UniqueQuest(IEnumerable<(uint BaseId, uint QuestId)> pairs, uint baseId)
    {
        var found = 0u;
        foreach (var (id, questId) in pairs)
        {
            if (id != baseId || questId == 0) continue;
            if (found == 0) found = questId;
            else if (found != questId) return 0;
        }

        return found;
    }
}
