using MudBlazor;

namespace Fuse.Admin.Theme;

/// <summary>The legacy back-office theme with Fuse's brand colours (from the tenant record) baked in.</summary>
public static class AdminTheme
{
    // DIN, the brand's face, always in capitals, for headings, buttons, menu and column names; Inter for the data
    // itself (rows, figures, fields, descriptions), so it stays easy to read.
    private static readonly string[] Inter = ["Inter", "Helvetica", "Arial", "sans-serif"];
    private static readonly string[] Din = ["DIN 2014 Narrow Bold", "DIN 2014 Demi", "Helvetica", "Arial", "sans-serif"];

    public static MudTheme Fuse { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#090511",
            Secondary = "#ab3dff",
            Tertiary = "#d1ac00",
            Info = "#f66",
            Success = "#ab3dff",
            Background = "#F7F7F8",
            AppbarBackground = "#FFF",
            AppbarText = "#18181B",
            DrawerBackground = "#FFF",
            DrawerText = "#3F3F46",
            TableLines = "#EEEEF0",
            OverlayDark = "hsl(0deg 0% 0% / 75%)",
        },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "6px" },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = Inter, FontSize = ".875rem", FontWeight = "400", LineHeight = "1.5", LetterSpacing = "0" },
            H1 = new H1Typography { FontFamily = Din, FontSize = "2rem", FontWeight = "400", LineHeight = "1.2", LetterSpacing = ".02em", TextTransform = "uppercase" },
            H2 = new H2Typography { FontFamily = Din, FontSize = "1.75rem", FontWeight = "400", LineHeight = "1.2", LetterSpacing = ".02em", TextTransform = "uppercase" },
            H3 = new H3Typography { FontFamily = Din, FontSize = "1.5rem", FontWeight = "400", LineHeight = "1.2", LetterSpacing = ".02em", TextTransform = "uppercase" },
            H4 = new H4Typography { FontFamily = Din, FontSize = "1.25rem", FontWeight = "400", LineHeight = "1.2", LetterSpacing = ".02em", TextTransform = "uppercase" },
            H5 = new H5Typography { FontFamily = Din, FontSize = "1.125rem", FontWeight = "400", LineHeight = "1.2", LetterSpacing = ".02em", TextTransform = "uppercase" },
            H6 = new H6Typography { FontFamily = Din, FontSize = "1rem", FontWeight = "400", LineHeight = "1.2", LetterSpacing = ".02em", TextTransform = "uppercase" },
            Button = new ButtonTypography { FontFamily = Din, FontSize = ".9375rem", FontWeight = "400", LineHeight = "1.75", LetterSpacing = ".04em", TextTransform = "uppercase" },
            Body1 = new Body1Typography { FontFamily = Inter, FontSize = ".9375rem", FontWeight = "400", LineHeight = "1.5", LetterSpacing = "0" },
            Body2 = new Body2Typography { FontFamily = Inter, FontSize = ".875rem", FontWeight = "400", LineHeight = "1.5", LetterSpacing = "0" },
            Caption = new CaptionTypography { FontFamily = Inter, FontSize = ".75rem", FontWeight = "400", LineHeight = "1.5", LetterSpacing = "0" },
            Subtitle1 = new Subtitle1Typography { FontFamily = Inter, FontSize = ".9375rem", FontWeight = "500", LineHeight = "1.5", LetterSpacing = "0" },
            Subtitle2 = new Subtitle2Typography { FontFamily = Inter, FontSize = ".875rem", FontWeight = "500", LineHeight = "1.5", LetterSpacing = "0" },
            Overline = new OverlineTypography { FontFamily = Inter, FontSize = ".75rem", FontWeight = "500", LineHeight = "1.5", LetterSpacing = ".02em", TextTransform = "none" },
        },
        ZIndex = new ZIndex { Drawer = 1300 },
    };
}
