using System;
using System.Collections.Generic;
using System.Numerics;

namespace FF14Accessibility.Services;

/// <summary>One missing current the browser can walk. The name is already the spoken form.</summary>
internal readonly record struct AetherWalkTarget(
    string Name,
    string ZoneName,
    Vector3 Position,
    ushort TerritoryId,
    uint MapId);

/// <summary>Whether the unlock bits could be read, and the currents that are still missing.</summary>
internal sealed record AetherWalkReport(bool Readable, IReadOnlyList<AetherWalkTarget> Targets);

/// <summary>One missing current before its name is chosen. Index is its place in the zone list.</summary>
/// <param name="SpeakZoneName">False when the card is still locked. The sheet place name stays unspoken.</param>
internal readonly record struct AetherWalkRow(
    AetherZone Zone,
    AetherCurrentEntry Current,
    AetherSpot Spot,
    int Index,
    bool SpeakZoneName);

/// <summary>
/// Missing currents the player can walk to. A zone counts when this session's window
/// showed it, or when at least one of its currents is already unlocked.
/// A locked card in another zone stays out. A missing field orb in the player's
/// current territory still counts: the compass names that one, and the window
/// does not. Quest currents on a locked card stay out. Currents that are already
/// unlocked, or that have no sheet spot, stay out.
/// A null unlock answer rejects the whole list, so a failed read is not treated as "all missing".
/// </summary>
internal static class AetherWalkList
{
    public static bool TryCollect(
        IEnumerable<AetherZone> zones,
        IReadOnlySet<uint>? windowRevealed,
        uint playerTerritory,
        Vector2? playerAt,
        Func<uint, bool?> unlocked,
        out List<AetherWalkRow> rows)
    {
        rows = new List<AetherWalkRow>();
        if (zones == null || unlocked == null) return false;

        var pending = new List<AetherWalkRow>();
        foreach (var zone in zones)
        {
            var anyUnlocked = false;
            var missing = new List<(AetherCurrentEntry Current, int Index)>();
            for (var i = 0; i < zone.Currents.Count; i++)
            {
                var state = unlocked(zone.Currents[i].Id);
                if (state == null)
                {
                    rows = new List<AetherWalkRow>();
                    return false;
                }

                if (state.Value) anyUnlocked = true;
                else missing.Add((zone.Currents[i], i));
            }

            var shown = windowRevealed != null && windowRevealed.Contains(zone.SetId);
            var zoneKnown = shown || anyUnlocked;
            foreach (var item in missing)
            {
                if (item.Current.Spot is not { } spot) continue;
                if (spot.TerritoryId == 0) continue;
                var fieldHere = playerTerritory != 0
                    && item.Current.QuestId == 0
                    && spot.TerritoryId == playerTerritory;
                if (!zoneKnown && !fieldHere) continue;
                pending.Add(new AetherWalkRow(zone, item.Current, spot, item.Index, zoneKnown));
            }
        }

        pending.Sort((a, b) => Compare(a, b, playerTerritory, playerAt));
        rows = pending;
        return true;
    }

    /// <summary>Copies the sheet spot, including Y, into the position the walk uses.</summary>
    public static bool TryToTarget(AetherWalkRow row, string name, string zoneName, out AetherWalkTarget target)
    {
        target = default;
        if (row.Spot.TerritoryId > ushort.MaxValue) return false;
        var spot = row.Spot;
        target = new AetherWalkTarget(
            name,
            zoneName,
            new Vector3(spot.X, spot.Y, spot.Z),
            (ushort)spot.TerritoryId,
            spot.MapId);
        return true;
    }

    private static int Compare(AetherWalkRow a, AetherWalkRow b, uint playerTerritory, Vector2? playerAt)
    {
        var aHere = a.Spot.TerritoryId == playerTerritory ? 0 : 1;
        var bHere = b.Spot.TerritoryId == playerTerritory ? 0 : 1;
        var cmp = aHere.CompareTo(bHere);
        if (cmp != 0) return cmp;
        if (playerAt is { } at && aHere == 0)
        {
            cmp = Distance2(a.Spot, at).CompareTo(Distance2(b.Spot, at));
            if (cmp != 0) return cmp;
        }
        cmp = a.Spot.TerritoryId.CompareTo(b.Spot.TerritoryId);
        if (cmp != 0) return cmp;
        cmp = a.Zone.SetId.CompareTo(b.Zone.SetId);
        if (cmp != 0) return cmp;
        return a.Index.CompareTo(b.Index);
    }

    private static float Distance2(AetherSpot spot, Vector2 at)
    {
        var dx = spot.X - at.X;
        var dz = spot.Z - at.Y;
        return dx * dx + dz * dz;
    }
}
