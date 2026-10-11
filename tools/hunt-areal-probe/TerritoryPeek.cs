using Lumina;
using Lumina.Data;
using Lumina.Excel.Sheets;
var sqpack = @"K:\SteamLibrary\steamapps\common\FINAL FANTASY XIV Online\game\sqpack";
var game = new GameData(sqpack, new LuminaOptions { PanicOnSheetChecksumMismatch = false, DefaultExcelLanguage = Language.German });
foreach (var id in new uint[] { 171u, 1330u })
{
    if (!game.GetExcelSheet<TerritoryType>()!.TryGetRow(id, out var t)) { Console.WriteLine(id + " missing"); continue; }
    var name = t.PlaceName.ValueNullable?.Name.ExtractText() ?? "?";
    Console.WriteLine($"{id}: '{name}' CFC={t.ContentFinderCondition.RowId} Bg={t.Bg.ExtractText()}");
}
