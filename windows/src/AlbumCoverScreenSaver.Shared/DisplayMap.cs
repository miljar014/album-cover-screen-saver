namespace AlbumCoverScreenSaver.Shared;

/// <summary>One display as the settings window needs to know it.</summary>
/// <param name="Key">
/// The stable key settings are filed under. See <see cref="DisplayKeys"/>.
/// </param>
/// <param name="FriendlyName">"DELL U2720Q". For the person, never for matching.</param>
/// <param name="Rect">Virtual-desktop rectangle, as x, y, width, height.</param>
/// <param name="IsPrimary">Whether Windows calls this the main display.</param>
/// <param name="Connected">False for a display remembered from last time but not plugged in now.</param>
public readonly record struct DisplayCard(
    string Key, string FriendlyName, int[] Rect, bool IsPrimary, bool Connected);

/// <summary>Where one display's rectangle goes when the map is drawn.</summary>
public readonly record struct MapTile(string Key, float X, float Y, float Width, float Height);

/// <summary>
/// Naming a display so its settings survive a reboot.
/// </summary>
/// <remarks>
/// <para>
/// Three identifiers are available and two are traps. A monitor handle is valid
/// only for this session. A <c>\\.\DISPLAY1</c> name is positional and shuffles
/// when a cable moves. Only the device path, which Windows derives from the
/// monitor's own EDID, stays put.
/// </para>
/// <para>
/// <b>The honest limit:</b> Microsoft promises none of these are stable forever.
/// Two identical monitors whose cables are swapped between ports can trade
/// identities, because their EDIDs match and only the connector differs. That is
/// rare and not worth engineering around. What matters is that it fails
/// politely, which is what the rules below are for.
/// </para>
/// </remarks>
public static class DisplayKeys
{
    /// <summary>
    /// The key for a display, falling back when Windows gives no device path.
    /// </summary>
    /// <remarks>
    /// An empty device path happens on virtual and headless targets, which is
    /// exactly what a virtual machine is, so this fallback is not a rare branch
    /// during development: it is the only branch.
    /// </remarks>
    public static string For(
        string? devicePath, uint manufactureId, uint productCodeId, uint connectorInstance)
    {
        if (!string.IsNullOrWhiteSpace(devicePath)) return devicePath.Trim();

        return $"edid-{manufactureId}-{productCodeId}-{connectorInstance}";
    }

    /// <summary>
    /// A last resort when even the EDID fields are missing.
    /// </summary>
    /// <remarks>
    /// Positional, and therefore wrong the moment a cable moves. It exists so a
    /// display always has <em>some</em> key rather than none, because a display
    /// with no key cannot be configured at all. Anything better wins.
    /// </remarks>
    public static string FromDeviceName(string? deviceName, int index) =>
        string.IsNullOrWhiteSpace(deviceName) ? $"display-{index}" : $"name-{deviceName.Trim()}";
}

/// <summary>
/// The Screens section: which displays to show, and where their rectangles go.
/// </summary>
public static class DisplayMap
{
    /// <summary>
    /// Every display the settings window should list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Connected displays first, in reading order, then any the settings
    /// remember that are not plugged in now.
    /// </para>
    /// <para>
    /// <b>Absent displays are kept, not pruned.</b> Unplugging a monitor and
    /// plugging it back in has to restore what the user chose for it, and a
    /// settings window that forgot a display the moment it was unplugged would
    /// also be a settings window that could not be used to fix a monitor which
    /// is currently switched off.
    /// </para>
    /// </remarks>
    public static List<DisplayCard> Listing(
        IReadOnlyList<DisplayCard> connected, IReadOnlyDictionary<string, DisplaySetting> remembered)
    {
        var cards = new List<DisplayCard>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var card in connected ?? [])
        {
            if (string.IsNullOrEmpty(card.Key) || !seen.Add(card.Key)) continue;
            cards.Add(card with { Connected = true });
        }

        foreach (var (key, setting) in remembered ?? new Dictionary<string, DisplaySetting>())
        {
            if (string.IsNullOrEmpty(key) || !seen.Add(key)) continue;

            cards.Add(new DisplayCard(
                Key: key,
                FriendlyName: string.IsNullOrWhiteSpace(setting.FriendlyName)
                    ? "Not connected"
                    : setting.FriendlyName,
                Rect: Rect(setting.LastRect),
                IsPrimary: false,
                Connected: false));
        }

        return cards;
    }

    /// <summary>
    /// A usable rectangle, whatever was stored.
    /// </summary>
    /// <remarks>
    /// A rectangle of no size counts as missing, not as stored. The default for
    /// a display setting is four zeroes, so a display remembered before it was
    /// ever seen connected has one, and a rectangle of no size on the map is a
    /// rectangle nobody can click.
    /// </remarks>
    private static int[] Rect(int[]? stored) =>
        stored is { Length: 4 } && stored[2] > 0 && stored[3] > 0
            ? stored
            : [0, 0, 1920, 1080];

    /// <summary>
    /// Where each display's rectangle goes inside a box, at its real proportions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately the same picture as the Windows Display settings page,
    /// because everyone has already seen and understood that one. So the whole
    /// arrangement is scaled by one factor and centred, which is what keeps a
    /// screen sitting above and to the left actually looking like it is above
    /// and to the left.
    /// </para>
    /// <para>
    /// Scaling each rectangle independently to fill the box would be easier and
    /// would destroy the only thing the picture is for.
    /// </para>
    /// </remarks>
    public static List<MapTile> Layout(
        IReadOnlyList<DisplayCard> cards, float boxWidth, float boxHeight, float padding = 8f)
    {
        var tiles = new List<MapTile>();

        if (cards is null || cards.Count == 0) return tiles;
        if (boxWidth <= padding * 2 || boxHeight <= padding * 2) return tiles;

        var left = int.MaxValue;
        var top = int.MaxValue;
        var right = int.MinValue;
        var bottom = int.MinValue;

        foreach (var card in cards)
        {
            var r = Rect(card.Rect);
            var width = Math.Max(1, r[2]);
            var height = Math.Max(1, r[3]);

            left = Math.Min(left, r[0]);
            top = Math.Min(top, r[1]);
            right = Math.Max(right, r[0] + width);
            bottom = Math.Max(bottom, r[1] + height);
        }

        var spanX = Math.Max(1, right - left);
        var spanY = Math.Max(1, bottom - top);

        var room = new { Width = boxWidth - (padding * 2), Height = boxHeight - (padding * 2) };
        var scale = Math.Min(room.Width / spanX, room.Height / spanY);

        // Centred in whatever room is left over, so a wide arrangement in a tall
        // box does not sit against the top edge.
        var offsetX = padding + ((room.Width - (spanX * scale)) / 2f);
        var offsetY = padding + ((room.Height - (spanY * scale)) / 2f);

        foreach (var card in cards)
        {
            var r = Rect(card.Rect);

            tiles.Add(new MapTile(
                Key: card.Key,
                X: offsetX + ((r[0] - left) * scale),
                Y: offsetY + ((r[1] - top) * scale),
                Width: Math.Max(2f, Math.Max(1, r[2]) * scale),
                Height: Math.Max(2f, Math.Max(1, r[3]) * scale)));
        }

        return tiles;
    }

    /// <summary>The tile under a point, or null.</summary>
    /// <remarks>Searched last first, so a tile drawn on top is the one picked.</remarks>
    public static string? HitTest(IReadOnlyList<MapTile> tiles, float x, float y)
    {
        if (tiles is null) return null;

        for (var i = tiles.Count - 1; i >= 0; i--)
        {
            var tile = tiles[i];

            if (x >= tile.X && x <= tile.X + tile.Width &&
                y >= tile.Y && y <= tile.Y + tile.Height)
            {
                return tile.Key;
            }
        }

        return null;
    }

    /// <summary>What the per-display dropdown offers, in order.</summary>
    /// <remarks>
    /// "Same as main" first, because it is the default and the answer for almost
    /// everybody, then the styles, then Off at the bottom where a destructive
    /// choice belongs.
    /// </remarks>
    public static List<(string Id, string Title)> Choices()
    {
        var choices = new List<(string, string)> { (DisplaySetting.Inherit, "Same as main") };

        foreach (var mode in CollageModes.All) choices.Add((mode.Id(), mode.Title()));

        choices.Add((DisplaySetting.Off, "Off (black screen)"));

        return choices;
    }
}
