using System.Numerics;
using FF14Accessibility;
using FF14Accessibility.Services;

namespace Regression.Tests;

public sealed class AetherWalkListTests : IDisposable
{
    private readonly LanguageMode _previous = Loc.Mode;

    public AetherWalkListTests() => Loc.Mode = LanguageMode.German;

    public void Dispose() => Loc.Mode = _previous;

    [Fact]
    public void Hidden_zone_stays_out_even_when_it_has_a_spot_and_a_name()
    {
        var hidden = Zone(4, "Geheim", Field(10, Spot(200, 1f, 2f, 3f)));
        var shown = Zone(5, "Offen", Field(11, Spot(397, 4f, 5f, 6f)));

        var ok = AetherWalkList.TryCollect(
            new[] { hidden, shown },
            new HashSet<uint> { 5 },
            playerTerritory: 397,
            playerAt: null,
            id => false,
            out var rows);

        Assert.True(ok);
        Assert.Single(rows);
        Assert.Equal(11u, rows[0].Current.Id);
        Assert.DoesNotContain(rows, row => row.Zone.SheetPlaceName == "Geheim");
    }

    [Fact]
    public void A_locked_card_lists_field_orbs_in_the_current_zone_and_keeps_the_name()
    {
        var locked = Zone(4, "Geheim",
            Field(10, Spot(397, 100f, 2f, 100f)),
            Field(11, Spot(397, 1f, 2f, 1f)),
            Quest(12, 5, "Beispiel", Spot(397, 0f, 2f, 0f)));
        var elsewhere = Zone(8, "Fern", Field(80, Spot(200, 0f, 0f, 0f)));

        var ok = AetherWalkList.TryCollect(
            new[] { locked, elsewhere },
            windowRevealed: null,
            playerTerritory: 397,
            playerAt: new Vector2(0f, 0f),
            id => false,
            out var rows);

        Assert.True(ok);
        Assert.Equal(new uint[] { 11, 10 }, rows.Select(row => row.Current.Id));
        Assert.All(rows, row => Assert.False(row.SpeakZoneName));
        Assert.DoesNotContain(rows, row => row.Zone.SheetPlaceName == "Fern");
    }

    [Fact]
    public void Window_reveal_includes_the_missing_quest_and_the_field_orb()
    {
        var zone = Zone(2, "Offen",
            Quest(20, 67328, "Beispiel", Spot(130, 8f, -1.5f, 9f, map: 4)),
            Field(21, Spot(130, 1f, 4.5f, 2f, map: 4)));

        var ok = AetherWalkList.TryCollect(
            new[] { zone },
            new HashSet<uint> { 2 },
            playerTerritory: 1,
            playerAt: null,
            id => false,
            out var rows);

        Assert.True(ok);
        Assert.Equal(2, rows.Count);
        Assert.Equal(20u, rows[0].Current.Id);
        Assert.Equal(21u, rows[1].Current.Id);
        Assert.True(AetherWalkList.TryToTarget(rows[0], "Auftrag Beispiel", "Offen", out var quest));
        Assert.Equal(new Vector3(8f, -1.5f, 9f), quest.Position);
        Assert.Equal((ushort)130, quest.TerritoryId);
        Assert.Equal(4u, quest.MapId);
        Assert.True(AetherWalkList.TryToTarget(rows[1], "Quelle", "Offen", out var field));
        Assert.Equal(4.5f, field.Position.Y);
    }

    [Fact]
    public void One_unlocked_current_lists_only_the_locked_remainder_without_the_window()
    {
        var zone = Zone(7, "Halb",
            Field(1, Spot(10, 1f, 1f, 1f)),
            Field(2, Spot(10, 2f, 2f, 2f)),
            Field(3, Spot(10, 3f, 3f, 3f)));

        var ok = AetherWalkList.TryCollect(
            new[] { zone },
            windowRevealed: null,
            playerTerritory: 10,
            playerAt: null,
            id => id == 2,
            out var rows);

        Assert.True(ok);
        Assert.Equal(new uint[] { 1, 3 }, rows.Select(row => row.Current.Id));
    }

    [Fact]
    public void Unlocked_and_spotless_currents_stay_out()
    {
        var zone = Zone(3, "Offen",
            Field(1, Spot(10, 1f, 1f, 1f)),
            Field(2, null),
            Field(3, Spot(0, 9f, 9f, 9f)));

        var ok = AetherWalkList.TryCollect(
            new[] { zone },
            new HashSet<uint> { 3 },
            playerTerritory: 10,
            playerAt: null,
            id => id == 1,
            out var rows);

        Assert.True(ok);
        Assert.Empty(rows);
    }

    [Fact]
    public void A_null_unlock_answer_rejects_the_whole_list()
    {
        var known = Zone(1, "Offen", Field(1, Spot(10, 1f, 1f, 1f)));
        var broken = Zone(2, "Auch", Field(99, Spot(10, 2f, 2f, 2f)));

        var ok = AetherWalkList.TryCollect(
            new[] { known, broken },
            new HashSet<uint> { 1, 2 },
            playerTerritory: 10,
            playerAt: null,
            id => id == 99 ? null : false,
            out var rows);

        Assert.False(ok);
        Assert.Empty(rows);
    }

    [Fact]
    public void The_player_territory_comes_first_then_territory_set_and_index()
    {
        var far = Zone(1, "Fern", Field(1, Spot(200, 1f, 0f, 1f)));
        var hereLate = Zone(5, "Nah",
            Field(50, Spot(397, 5f, 0f, 5f)),
            Field(51, Spot(397, 6f, 0f, 6f)));
        var hereEarly = Zone(2, "NochNah", Field(20, Spot(397, 2f, 0f, 2f)));
        var issuerHere = Zone(9, "AuftragWoanders",
            Quest(90, 1, "Beispiel", Spot(397, 9f, 3f, 9f)));

        var ok = AetherWalkList.TryCollect(
            new[] { far, hereLate, issuerHere, hereEarly },
            new HashSet<uint> { 1, 2, 5, 9 },
            playerTerritory: 397,
            playerAt: null,
            id => false,
            out var rows);

        Assert.True(ok);
        Assert.Equal(new uint[] { 20, 50, 51, 90, 1 }, rows.Select(row => row.Current.Id));
    }

    [Fact]
    public void A_finished_zone_adds_nothing()
    {
        var zone = Zone(8, "Fertig",
            Field(1, Spot(10, 1f, 1f, 1f)),
            Quest(2, 5, "Beispiel", Spot(10, 2f, 2f, 2f)));

        var ok = AetherWalkList.TryCollect(
            new[] { zone },
            new HashSet<uint> { 8 },
            playerTerritory: 10,
            playerAt: null,
            id => true,
            out var rows);

        Assert.True(ok);
        Assert.Empty(rows);
    }

    [Fact]
    public void Spoken_lines_name_the_category_and_not_a_hidden_place()
    {
        Assert.Equal("Windäther", AccessibilityStrings.CategoryLabel(NavCategory.AetherCurrents));
        Assert.Equal("Quelle", AccessibilityStrings.AetherWalkField);
        Assert.Equal("Auftrag Beispiel", AccessibilityStrings.AetherWalkQuest("Beispiel"));
        Assert.Equal("Windäther. 2 hier, 3 in anderen Gebieten.", AccessibilityStrings.CategoryAetherCount(2, 3));
        Assert.Equal("Windäther. 2 hier.", AccessibilityStrings.CategoryAetherCount(2, 0));
        Assert.Equal(
            "Keine anlaufbare Windätherquelle. Im jetzigen Gebiet ist keine Feldquelle übrig. Andere Gebiete nennt nur das Fenster, oder eines, in dem du schon eine Quelle hast.",
            AccessibilityStrings.NoAetherWalks);
        var hint = AccessibilityStrings.AetherPageHint("Strg F10");
        Assert.Contains("Windäther", hint);
        Assert.Contains("Nummernblock 3", hint);
        Assert.DoesNotContain("Geheim", hint);
    }

    private static AetherZone Zone(uint setId, string name, params AetherCurrentEntry[] currents)
        => new(setId, territoryId: 1, placeNameId: 0, name, currents);

    private static AetherCurrentEntry Field(uint id, AetherSpot? spot)
        => new(id, 0, string.Empty, spot);

    private static AetherCurrentEntry Quest(uint id, uint questId, string name, AetherSpot? spot)
        => new(id, questId, name, spot);

    private static AetherSpot Spot(uint territory, float x, float y, float z, uint map = 1)
        => new(territory, map, x, y, z);
}
