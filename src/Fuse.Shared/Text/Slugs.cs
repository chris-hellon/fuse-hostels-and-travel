using System.Globalization;
using System.Text;

namespace Fuse.Shared.Text;

/// <summary>
/// URL slugs ("Best Bars &amp; Clubs in Nha Trang" → "best-bars-and-clubs-in-nha-trang"), exactly as legacy
/// StringExtensions.UrlFriendly, so every address made from a name stays the same: "&amp;" becomes "and" with no
/// dashes of its own ("A&amp;B" → "aandb"), accents are dropped, and letters that don't decompose (ø, ł, ß, þ…)
/// map to ASCII; any other non-ASCII letter is dropped.
/// </summary>
public static class Slugs
{
    public static string From(string? text)
    {
        if (text == null)
            return "";

        var builder = new StringBuilder(text.Length);
        var dash = false;
        foreach (var c in text.Replace("&", "and").ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            switch (CharUnicodeInfo.GetUnicodeCategory(c))
            {
                case UnicodeCategory.LowercaseLetter or UnicodeCategory.UppercaseLetter or UnicodeCategory.DecimalDigitNumber:
                    builder.Append(c < 128 ? c.ToString() : Ascii(c));
                    dash = false;
                    break;
                case UnicodeCategory.SpaceSeparator or UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation
                    or UnicodeCategory.OtherPunctuation or UnicodeCategory.MathSymbol:
                    if (!dash)
                    {
                        builder.Append('-');
                        dash = true;
                    }
                    break;
            }
        }

        return builder.ToString().Trim('-');
    }

    /// <summary>Legacy RemapInternationalCharToAscii (accents are already split off by the decomposition).</summary>
    private static string Ascii(char c)
    {
        var s = c.ToString().ToLowerInvariant();
        if ("àåáâäãåą".Contains(s)) return "a";
        if ("èéêëę".Contains(s)) return "e";
        if ("ìíîïı".Contains(s)) return "i";
        if ("òóôõöøőð".Contains(s)) return "o";
        if ("ùúûüŭů".Contains(s)) return "u";
        if ("çćčĉ".Contains(s)) return "c";
        if ("żźž".Contains(s)) return "z";
        if ("śşšŝ".Contains(s)) return "s";
        if ("ñń".Contains(s)) return "n";
        if ("ýÿ".Contains(s)) return "y";
        if ("ğĝ".Contains(s)) return "g";
        return c switch
        {
            'ř' => "r",
            'ł' => "l",
            'đ' => "d",
            'ß' => "ss",
            'þ' => "th",
            'ĥ' => "h",
            'ĵ' => "j",
            _ => "",
        };
    }
}
