using System.Globalization;

namespace Fuse.Shared.Globalization;

/// <summary>
/// UK English abbreviates September as "Sept" (other months keep three letters). Dates everywhere read
/// "Sep", so the current culture's abbreviations are cut to three letters at startup.
/// </summary>
public static class MonthAbbreviations
{
    public static void UseThreeLetterMonths()
    {
        var culture = (CultureInfo)CultureInfo.CurrentCulture.Clone();
        var format = culture.DateTimeFormat;
        format.AbbreviatedMonthNames = [.. format.AbbreviatedMonthNames.Select(Trim)];
        format.AbbreviatedMonthGenitiveNames = [.. format.AbbreviatedMonthGenitiveNames.Select(Trim)];
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.CurrentCulture = culture;
    }

    private static string Trim(string name) => name == "Sept" ? "Sep" : name;
}
