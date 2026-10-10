namespace MeridianAgent.Web.Components.Theme;

using MudBlazor;

/// <summary>
/// The Meridian palette, modelled on the Dubai Chamber of Commerce digital identity: deep navy for
/// trust, premium gold for service quality, on a soft light-grey canvas. Light and dark palettes are
/// both defined so the layout theme providers can switch without losing contrast.
/// </summary>
public static class MeridianTheme
{
    // Dubai Chamber inspired palette (kept as constants so the CSS in app.css can match them).
    public const string Primary = "#0F2B46";
    public const string Secondary = "#C5A059";
    public const string Background = "#F8F9FA";
    public const string Surface = "#FFFFFF";
    public const string TextPrimary = "#1E293B";
    public const string TextSecondary = "#64748B";
    public const string Divider = "rgba(15, 43, 70, 0.12)";
    public const string DividerLight = "rgba(15, 43, 70, 0.07)";

    /// <summary>
    /// The Dubai font, served from wwwroot/fonts/Dubai (self-hosted so no third-party request
    /// is needed and the app keeps rendering without network access).
    /// </summary>
    public static readonly string[] FontFamily = { "Dubai", "sans-serif" };

    public static MudTheme Create() => new()
    {
        PaletteLight = new PaletteLight
        {
            // Dubai Chamber inspired — deep navy and premium gold on a crisp grey canvas.
            Primary = Primary,
            Secondary = Secondary,
            AppbarBackground = Surface,
            AppbarText = TextPrimary,
            DrawerBackground = Surface,
            DrawerText = TextPrimary,
            Background = Background,
            BackgroundGray = "#EEF1F5",
            Surface = Surface,
            TextPrimary = TextPrimary,
            TextSecondary = TextSecondary,
            TextDisabled = "rgba(30, 41, 59, 0.38)",
            ActionDefault = TextSecondary,
            ActionDisabled = "rgba(30, 41, 59, 0.26)",
            ActionDisabledBackground = "rgba(30, 41, 59, 0.12)",
            Divider = Divider,
            DividerLight = DividerLight,
            TableLines = DividerLight,
            LinesDefault = Divider,
            LinesInputs = "rgba(15, 43, 70, 0.22)",
            Success = "#10B981",
            Warning = "#F59E0B",
            Error = "#EF4444",
            Info = "#0F2B46",
            HoverOpacity = 0.06,
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#38BDF8",
            Secondary = "#D4AF37",
            AppbarBackground = "#0F172A",
            DrawerBackground = "#0F172A",
            DrawerText = "#F8FAFC",
            Background = "#020617",
            BackgroundGray = "#0B1220",
            Surface = "#1E293B",
            TextPrimary = "#F8FAFC",
            TextSecondary = "#94A3B8",
            Divider = "rgba(148, 163, 184, 0.24)",
            DividerLight = "rgba(148, 163, 184, 0.12)",
            Success = "#10B981",
            Warning = "#F59E0B",
            Error = "#EF4444",
            Info = "#38BDF8",
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "8px",
            DrawerWidthLeft = "260px",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = FontFamily },
            H4 = new H4Typography { FontSize = "1.875rem", FontWeight = "600", LetterSpacing = "-0.025em" },
            H6 = new H6Typography { FontSize = "1.25rem", FontWeight = "600" },
            Subtitle1 = new Subtitle1Typography { FontWeight = "600" },
            Button = new ButtonTypography { FontFamily = FontFamily, FontWeight = "600", TextTransform = "none" },
        },
    };
}
