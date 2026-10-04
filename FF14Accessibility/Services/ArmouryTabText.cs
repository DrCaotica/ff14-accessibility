using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LuminaAddon = Lumina.Excel.Sheets.Addon;

namespace FF14Accessibility.Services;

/// <summary>
/// Names the armoury chest's category button under the focus ("Kopf",
/// "Jobkristall").
///
/// WHY THIS EXISTS: the twelve category buttons are icon-only RadioButtons with
/// no text node and no tooltip binding, so stepping onto them with the arrow keys
/// was silent ("[Focus] STUMM addon='ArmouryBoard' id=7", log 2026-10-04
/// 17:34:54) - the user took them for empty slots that failed to speak. Only the
/// SELECTED category was announced, from the window title.
///
/// WHICH BUTTON: AddonArmouryBoard holds the selected TabIndex but no button
/// array (ilspycmd 2026-10-04). Measured with [ArmTabProbe] on 2026-10-04 by
/// focusing each of the twelve buttons and selecting it: component nodes 7..12
/// (left column, top to bottom) are TabIndex 0..5, nodes 13..18 (right column)
/// TabIndex 6..11, every pair confirmed by the category title that followed.
///
/// WHAT IT IS CALLED: the armoury's own words, Addon rows 1373..1385 - the same
/// text the window shows as its title once the category is selected (compared
/// for all twelve). The rows run in sheet order, not button order (waist sits in
/// between, neck comes before ears), hence the table.
/// </summary>
internal static unsafe class ArmouryTabText
{
    private const string AddonName = "ArmouryBoard";

    /// <summary>Node id of the first category button (TabIndex 0).</summary>
    private const uint FirstTabNodeId = 7;

    /// <summary>Addon row of each TabIndex's category name.</summary>
    private static readonly uint[] TabNameRows =
    {
        1373, // 0  Haupthand
        1375, // 1  Kopf
        1376, // 2  Rumpf
        1377, // 3  Hände
        1379, // 4  Beine
        1380, // 5  Füße
        1374, // 6  Nebenhand
        1382, // 7  Ohren
        1381, // 8  Hals
        1383, // 9  Handgelenke
        1384, // 10 Ring
        1385, // 11 Jobkristall
    };

    /// <summary>
    /// The category name of the armoury button the focus sits on, or false when
    /// the focus is anywhere else.
    /// </summary>
    public static bool TryDescribe(AtkResNode* focus, string addonName, IDataManager data, out string name)
    {
        name = string.Empty;
        if (focus == null || addonName != AddonName) return false;

        // The focus sits on the button's collision child; the button is the
        // nearest component node above it.
        var cur = focus->ParentNode;
        for (var up = 0; up < 3 && cur != null; up++, cur = cur->ParentNode)
        {
            if ((int)cur->Type < 1000) continue;
            var comp = ((AtkComponentNode*)cur)->Component;
            if (comp == null || comp->GetComponentType() != ComponentType.RadioButton) return false;

            var tab = (int)cur->NodeId - (int)FirstTabNodeId;
            if (tab < 0 || tab >= TabNameRows.Length) return false;

            name = data.GetExcelSheet<LuminaAddon>().TryGetRow(TabNameRows[tab], out var row)
                ? TolkService.Sanitize(row.Text.ExtractText()).Trim()
                : string.Empty;
            return name.Length > 0;
        }
        return false;
    }
}
