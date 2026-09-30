using System.Globalization;

namespace Fuse.Shared.Events;

/// <summary>
/// Event colours are stored the legacy way, "r, g, b" (the website writes them into <c>rgb(...)</c>);
/// the admin works in hex. Category colours are plain hex.
/// </summary>
public static class EventColours
{
    /// <summary>"#48bb78" → "72, 187, 120". Black, blank or unparseable → null (no override).</summary>
    public static string? ToRgbTriplet(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;
        var value = hex.Trim().TrimStart('#');
        if (value.Length == 8)
            value = value[..6]; // drop alpha
        if (value.Length != 6 || !int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return null;
        var (r, g, b) = ((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        return r == 0 && g == 0 && b == 0 ? null : $"{r}, {g}, {b}";
    }

    /// <summary>"72, 187, 120" → "#48bb78"; anything else (already hex, blank) passes through.</summary>
    public static string? ToHex(string? triplet)
    {
        if (string.IsNullOrWhiteSpace(triplet) || !triplet.Contains(','))
            return string.IsNullOrWhiteSpace(triplet) ? null : triplet.Trim();
        var parts = triplet.Split(',').Select(p => int.TryParse(p.Trim(), out var v) ? Math.Clamp(v, 0, 255) : 0).ToArray();
        return parts.Length < 3 ? null : $"#{parts[0]:x2}{parts[1]:x2}{parts[2]:x2}";
    }

    /// <summary>
    /// An event's colour (hex): its own (stored as legacy "r, g, b" or hex), else its category's. The same everywhere
    /// it's shown: the site, the calendars and the admin list.
    /// </summary>
    public static string? For(string? own, string? category) =>
        ToHex(own) is { Length: > 0 } hex ? hex : string.IsNullOrWhiteSpace(category) ? null : category;
}
