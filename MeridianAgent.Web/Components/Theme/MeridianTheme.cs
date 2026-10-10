namespace MeridianAgent.Web.Components.Theme;

using MudBlazor;

/// <summary>
/// The Meridian palette: warm cream backgrounds with amber/gold accents. Shared by both layouts
/// so the chat, the map and onboarding all feel like the same concierge.
/// </summary>
public static class MeridianTheme
{
    public const string Primary = "#D97706";
    public const string PrimaryDarken = "#B45309";
    public const string PrimaryLighten = "#F59E0B";
    public const string Background = "#FFFBEB";
    public const string Surface = "#FFFDF6";
    public const string TextPrimary = "#451A03";
    public const string TextSecondary = "#92400E";
    public const string Divider = "rgba(217,119,6,0.18)";
    public const string DividerLight = "rgba(217,119,6,0.10)";

    /// <summary>Font stack that stays native on every device while matching the warm look.</summary>
    public const string FontFamily = "system-ui, -apple-system, \"Segoe UI\", Roboto, sans-serif";

    public static MudTheme Meridian => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = Primary,
            PrimaryDarken = PrimaryDarken,
            PrimaryLighten = PrimaryLighten,
            Secondary = "#92400E",
            Tertiary = "#78350F",
            Info = "#B45309",
            Success = "#166534",
            Warning = "#D97706",
            Error = "#B91C1C",
            Dark = "#451A03",
            Background = Background,
            BackgroundGray = "#FEF3C7",
            Surface = Surface,
            AppbarBackground = Primary,
            AppbarText = "#FFFFFF",
            DrawerBackground = Surface,
            DrawerText = TextPrimary,
            TextPrimary = TextPrimary,
            TextSecondary = TextSecondary,
            TextDisabled = "rgba(69,26,3,0.38)",
            ActionDefault = TextSecondary,
            ActionDisabled = "rgba(69,26,3,0.26)",
            ActionDisabledBackground = "rgba(69,26,3,0.12)",
            Divider = Divider,
            DividerLight = DividerLight,
            TableLines = DividerLight,
            LinesDefault = Divider,
            LinesInputs = Divider,
            HoverOpacity = 0.06,
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "12px",
            DrawerWidthLeft = "260px",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = new[] { FontFamily } },
            H4 = new H4Typography { FontWeight = "700" },
            H5 = new H5Typography { FontWeight = "700" },
            H6 = new H6Typography { FontWeight = "600" },
            Subtitle1 = new Subtitle1Typography { FontWeight = "600" },
            Button = new ButtonTypography { FontWeight = "600", TextTransform = "none" },
        },
    };
}
