using FF14Accessibility;
using FF14Accessibility.Services;

namespace Regression.Tests;

public sealed class AetherCurrentTests : IDisposable
{
    private readonly LanguageMode _previous = Loc.Mode;

    public AetherCurrentTests() => Loc.Mode = LanguageMode.German;

    public void Dispose() => Loc.Mode = _previous;

    [Fact]
    public void Thirty_cards_skip_the_shared_set()
    {
        var ids = new List<uint>();
        for (var tab = 0; tab < 5; tab++)
            for (var card = 0; card < 6; card++)
                ids.Add(AetherCurrentIndex.CompFlgSetId(tab, card));

        var expected = Enumerable.Range(1, 18).Select(i => (uint)i)
            .Concat(Enumerable.Range(20, 12).Select(i => (uint)i));
        Assert.Equal(expected, ids);
        Assert.DoesNotContain(19u, ids);
        Assert.Equal(1u, AetherCurrentIndex.CompFlgSetId(0, 0));
        Assert.Equal(6u, AetherCurrentIndex.CompFlgSetId(0, 5));
        Assert.Equal(18u, AetherCurrentIndex.CompFlgSetId(2, 5));
        Assert.Equal(20u, AetherCurrentIndex.CompFlgSetId(3, 0));
        Assert.Equal(31u, AetherCurrentIndex.CompFlgSetId(4, 5));
        Assert.Equal(0u, AetherCurrentIndex.CompFlgSetId(5, 0));
        Assert.Equal(0u, AetherCurrentIndex.CompFlgSetId(-1, 0));
        Assert.Equal(0u, AetherCurrentIndex.CompFlgSetId(0, 6));
        Assert.Equal(0u, AetherCurrentIndex.CompFlgSetId(0, -1));
    }

    [Fact]
    public void Hidden_card_keeps_the_game_status_and_no_zone_name()
    {
        var line = AccessibilityStrings.AetherHiddenCard(1, 5, 1, 6, "Keine Informationen verfügbar.");

        Assert.Equal("Seite 1 von 5. Eintrag 1 von 6. Keine Informationen verfügbar.", line);
        Assert.DoesNotContain("Coerthas", line);
        Assert.DoesNotContain("von 9", line);
    }

    [Fact]
    public void Compiled_zone_names_the_place_and_says_flight_is_free()
    {
        var line = AccessibilityStrings.AetherZoneCard(
            1, 5, "Westliches Hochland von Coerthas", 1, 6, 4, 9, compiled: true);

        Assert.Equal(
            "Seite 1 von 5. Westliches Hochland von Coerthas, Eintrag 1 von 6. 4 von 9. Gebündelt. Fliegen ist frei.",
            line);
    }

    [Fact]
    public void Map_formula_is_the_inverse_used_for_the_current_map()
    {
        var coord = AetherCurrentIndex.WorldToMap(0f, 0f, 100, 0, 0);

        Assert.NotNull(coord);
        Assert.Equal(21.48f, coord.Value.X, 2);
        Assert.Equal(21.48f, coord.Value.Y, 2);
    }
}
