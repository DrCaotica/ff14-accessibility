// Offline: hunting-log habitat vs MapRange pieces for one monster name.
// Answers whether PlaceNameLocation matches Spots, Blocks, and where Sil'dih sits.
using System.Globalization;
using System.Numerics;
using Lumina;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using Lumina.Excel.Sheets;

var sqpack = Environment.GetEnvironmentVariable("FFXIV_SQPACK")
             ?? @"K:\SteamLibrary\steamapps\common\FINAL FANTASY XIV Online\game\sqpack";

var monsterNeedle = args.Length > 0 ? args[0] : "Grabräuber";
var game = new GameData(sqpack, new LuminaOptions
{
    PanicOnSheetChecksumMismatch = false,
    DefaultExcelLanguage = Language.German,
});

var places = game.GetExcelSheet<PlaceName>()!;
string Place(uint row) => row == 0 ? "-"
    : places.TryGetRow(row, out var p) ? p.Name.ExtractText().Trim() : $"#{row}";

Console.WriteLine($"sqpack: {sqpack}");
Console.WriteLine($"Suche MonsterNoteTarget mit Name enthaltend '{monsterNeedle}'");
Console.WriteLine();

foreach (var row in game.GetExcelSheet<MonsterNoteTarget>()!)
{
    var bn = row.BNpcName.ValueNullable;
    if (bn == null) continue;
    var singular = bn.Value.Singular.ExtractText();
    if (!singular.Contains(monsterNeedle, StringComparison.OrdinalIgnoreCase))
        continue;

    Console.WriteLine($"MonsterNoteTarget {row.RowId}: BNpcName={row.BNpcName.RowId} '{singular}'");
    for (var i = 0; i < row.PlaceNameZone.Count; i++)
    {
        var z = row.PlaceNameZone[i];
        var a = i < row.PlaceNameLocation.Count ? row.PlaceNameLocation[i] : default;
        if (z.RowId == 0 && a.RowId == 0) continue;
        Console.WriteLine($"  habitat[{i}]: Zone={z.RowId} '{Place(z.RowId)}'  Location={a.RowId} '{Place(a.RowId)}'");
    }
    Console.WriteLine();
}

// Central Thanalan = Territory 141
const uint territoryId = 141;
var territory = game.GetExcelSheet<TerritoryType>()!.GetRow(territoryId);
var bg = territory.Bg.ExtractText();
var dir = "bg/" + bg[..(bg.LastIndexOf("/level/", StringComparison.Ordinal) + 7)];
Console.WriteLine($"Territory {territoryId} ({Place(territory.PlaceName.RowId)}) layout {dir}");

var pieces = new List<(uint Block, uint Spot, Vector3 Centre, float Extent, string File)>();
foreach (var file in new[] { "planmap.lgb", "planevent.lgb" })
{
    LgbFile? lgb;
    try { lgb = game.GetFile<LgbFile>(dir + file); }
    catch (Exception ex) { Console.WriteLine($"  {file}: {ex.Message}"); continue; }
    if (lgb == null) continue;
    foreach (var layer in lgb.Layers)
    foreach (var instance in layer.InstanceObjects)
    {
        if (instance.AssetType != LayerEntryType.MapRange) continue;
        if (instance.Object is not LayerCommon.MapRangeInstanceObject range) continue;
        var t = instance.Transform;
        pieces.Add((range.PlaceNameBlock, range.PlaceNameSpot,
            new Vector3(t.Translation.X, t.Translation.Y, t.Translation.Z),
            MathF.Max(t.Scale.X, t.Scale.Z), file));
    }
}
Console.WriteLine($"MapRange pieces: {pieces.Count}");

bool NameHas(uint row, string needle) =>
    Place(row).Contains(needle, StringComparison.OrdinalIgnoreCase);

void DumpMatch(string label, Func<(uint Block, uint Spot, Vector3 Centre, float Extent, string File), bool> pred)
{
    var hits = pieces.Where(pred).ToList();
    Console.WriteLine();
    Console.WriteLine($"=== {label}: {hits.Count} ===");
    foreach (var h in hits
                 .OrderBy(h => MathF.Sqrt(h.Centre.X * h.Centre.X + h.Centre.Z * h.Centre.Z))
                 .Take(40))
    {
        Console.WriteLine($"  Block={h.Block} '{Place(h.Block)}' Spot={h.Spot} '{Place(h.Spot)}' " +
                          $"({h.Centre.X:F0}|{h.Centre.Y:F0}|{h.Centre.Z:F0}) extent={h.Extent:F0} [{h.File}]");
    }
}

DumpMatch("Block OR Spot contains Kohlenstaub",
    p => NameHas(p.Block, "Kohlenstaub") || NameHas(p.Spot, "Kohlenstaub"));
DumpMatch("Spot contains Kohlenstaub only",
    p => NameHas(p.Spot, "Kohlenstaub"));
DumpMatch("Block==Kohlenstaub AND Spot empty",
    p => NameHas(p.Block, "Kohlenstaub") && p.Spot == 0);
DumpMatch("Sil'dih / Sildih / Ruinen",
    p => NameHas(p.Block, "Sil") || NameHas(p.Spot, "Sil")
         || NameHas(p.Block, "Ruin") || NameHas(p.Spot, "Ruin")
         || NameHas(p.Block, "Sildih") || NameHas(p.Spot, "Sildih"));

// Map markers on Central Thanalan map for Kohlenstaub / Sil'dih
var mapId = territory.Map.RowId;
Console.WriteLine();
Console.WriteLine($"Map {mapId} markers containing Kohlenstaub / Sil / Ruin:");
if (game.GetExcelSheet<Map>()!.TryGetRow(mapId, out var map)
    && game.GetSubrowExcelSheet<MapMarker>()!.TryGetRow(map.MapMarkerRange, out var markers))
{
    static float PixelToWorld(float pixel, ushort sizeFactor, short offset)
        => (pixel - 1024f) * 100f / sizeFactor - offset;

    foreach (var m in markers)
    {
        var text = m.PlaceNameSubtext.ValueNullable?.Name.ExtractText().Trim() ?? "";
        if (text.Length == 0) continue;
        if (!text.Contains("Kohlenstaub", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("Sil", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("Ruin", StringComparison.OrdinalIgnoreCase))
            continue;
        var wx = PixelToWorld(m.X, map.SizeFactor, map.OffsetX);
        var wz = PixelToWorld(m.Y, map.SizeFactor, map.OffsetY);
        Console.WriteLine($"  marker PlaceName={m.PlaceNameSubtext.RowId} '{text}' " +
                          $"pixel=({m.X},{m.Y}) world=({wx:F0}|{wz:F0})");
    }

    // Wiki spawn for Spriggan Graverobber: map X:17 Y:23
    var wikiX = (17f - 1f) * map.SizeFactor / 2f;
    var wikiY = (23f - 1f) * map.SizeFactor / 2f;
    var wikiWx = PixelToWorld(wikiX, map.SizeFactor, map.OffsetX);
    var wikiWz = PixelToWorld(wikiY, map.SizeFactor, map.OffsetY);
    Console.WriteLine();
    Console.WriteLine($"Wiki spawn map 17/23 => world ({wikiWx:F0}|{wikiWz:F0})");
    Console.WriteLine("Nearest MapRange pieces to wiki spawn:");
    foreach (var h in pieces
                 .Select(p => (p, Dist: MathF.Sqrt(
                     (p.Centre.X - wikiWx) * (p.Centre.X - wikiWx)
                     + (p.Centre.Z - wikiWz) * (p.Centre.Z - wikiWz))))
                 .OrderBy(x => x.Dist)
                 .Take(12))
    {
        Console.WriteLine($"  {h.Dist:F0} m  Block='{Place(h.p.Block)}' Spot='{Place(h.p.Spot)}' " +
                          $"({h.p.Centre.X:F0}|{h.p.Centre.Z:F0})");
    }
}

Console.WriteLine();
Console.WriteLine("(Level/BNpcBase-Verknuepfung uebersprungen: BNpcBase traegt keinen BNpcName-Verweis.)");

// Named Spots under Block 248, unique, distance from Kohlenstaub map marker (-70|-288)
var kohlenMarker = new Vector2(-70f, -288f);
Console.WriteLine();
Console.WriteLine("Unique named Spots under Block Kohlenstaub, by distance to Kohlenstaub map marker:");
foreach (var g in pieces.Where(p => p.Block == 248 && p.Spot != 0)
             .GroupBy(p => p.Spot)
             .Select(g =>
             {
                 var c = g.First().Centre;
                 var d = MathF.Sqrt((c.X - kohlenMarker.X) * (c.X - kohlenMarker.X)
                                    + (c.Z - kohlenMarker.Y) * (c.Z - kohlenMarker.Y));
                 return (Spot: g.Key, Name: Place(g.Key), Count: g.Count(), Dist: d, c.X, c.Z);
             })
             .OrderBy(x => x.Dist))
{
    Console.WriteLine($"  {g.Dist:F0} m  Spot={g.Spot} '{g.Name}' x{g.Count} ({g.X:F0}|{g.Z:F0})");
}
