namespace FF14Accessibility.Services;

/// <summary>
/// Card position in the Aether Current window to <c>AetherCurrentCompFlgSet</c> row.
/// Five pages, six cards. Set 19 is the shared A Realm Reborn row and is not a card.
/// Measured against the German sheets on 2026-10-16: sets 1-18 and 20-31 are the 30 cards.
/// </summary>
internal static class AetherCurrentIndex
{
    /// <summary>
    /// Set id for a zero-based page and card, or 0 when the position is outside the window.
    /// <c>raw = card + 6 * tab</c>, then skip set 19: ids below 19 stay <c>raw + 1</c>,
    /// ids from 19 upward are <c>raw + 2</c>.
    /// </summary>
    internal static uint CompFlgSetId(int tab, int card)
    {
        if (tab is < 0 or > 4 || card is < 0 or > 5) return 0;
        var raw = card + 6 * tab;
        var index = raw + 1;
        if (index < 19) index = raw;
        return (uint)(index + 1);
    }

    /// <summary>
    /// World X/Z to the map numbers of one <c>Map</c> row.
    /// Same inverse as <see cref="PlacesService.WorldToMapCoord"/>, which only
    /// knows the map the player is standing on. pixel = (world + offset) * scale / 100 + 1024,
    /// coord = 2 * pixel / scale + 1.
    /// </summary>
    internal static (float X, float Y)? WorldToMap(float worldX, float worldZ, ushort sizeFactor, short offsetX, short offsetY)
    {
        if (sizeFactor == 0) return null;
        var pixelX = (worldX + offsetX) * sizeFactor / 100f + 1024f;
        var pixelY = (worldZ + offsetY) * sizeFactor / 100f + 1024f;
        return (pixelX * 2f / sizeFactor + 1f, pixelY * 2f / sizeFactor + 1f);
    }
}
