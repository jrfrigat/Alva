using Flare.Abstractions;
using Flare.Abstractions.Tokens;
using Flare.Abstractions.Tokens.Components;
using Flare.Theme.FluentUI2;
using Flare.Theming;

namespace Alva.Theme.Alva;

/// <summary>
/// The Alva theme: the Stitch design expressed in Flare tokens.
/// </summary>
/// <remarks>
/// The theme is derived from Fluent UI 2 rather than written from scratch: its minimal geometry and
/// state language fit the Stitch design, and Alva overrides what differs. Only what differs from Fluent
/// is overridden:
/// <list type="bullet">
/// <item><description><b>Color</b> - with its own palette (<see cref="AlvaPalettes"/>), because the stock
/// Fluent palette made the screen read as Office rather than Alva.</description></item>
/// <item><description><b>Typography</b> - Inter and the design scale: tight line heights, 10-11px
/// micro labels with letter spacing.</description></item>
/// <item><description><b>Shape</b> - a single 6px radius for data elements instead of Fluent's
/// size-dependent corners.</description></item>
/// <item><description><b>Depth</b> - planes are separated by borders rather than tonal elevation: shadows
/// are kept only for overlays (levels 2-3).</description></item>
/// </list>
/// The base theme's assets are not disabled (<c>inheritStyleAssets</c> is not passed): Alva stays in the
/// Fluent style family, and Alva's rules are only added after its stylesheets.
/// </remarks>
public static class AlvaTheme
{
    /// <summary>The stable theme id; it is also the CSS class that scopes its rules.</summary>
    public const string ThemeId = "alva";

    /// <summary>The interface typeface - the only one named by the design.</summary>
    public const string UiFont = "Inter";

    /// <summary>Builds the theme. Registered once, selected by <see cref="ThemeId"/>.</summary>
    public static ITheme Create() => new FluentUI2Theme().Derive(
        id: ThemeId,
        displayName: "Alva Sapphire",
        design: Apply,
        palettes: AlvaPalettes.All,
        defaultPaletteId: AlvaPalettes.SapphireId,
        // The design typeface and rules that tokens do not express (see the theme stylesheet).
        styleAssets:
        [
            "https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700;800&display=swap",
            $"_content/Alva.Theme.Alva/css/{ThemeId}.css",
        ]);

    private static DesignTokens Apply(DesignTokens d) => d with
    {
        FocusRing = "2px solid var(--flare-color-primary)",

        // The design scale: headings 30/22/18, body 14/13/12, micro labels 13/11/10.
        Typography = d.Typography with
        {
            DisplayLarge = Type("700", "1.875rem", "2.375rem", "-0.02em"),
            HeadlineLarge = Type("700", "1.875rem", "2.375rem", "-0.02em"),
            HeadlineMedium = Type("600", "1.375rem", "1.75rem", "-0.015em"),
            HeadlineSmall = Type("600", "1.125rem", "1.5rem", "-0.01em"),
            TitleLarge = Type("600", "1.125rem", "1.5rem", "-0.01em"),
            TitleMedium = Type("600", "0.9375rem", "1.25rem", "-0.005em"),
            TitleSmall = Type("600", "0.875rem", "1.125rem", "0"),
            BodyLarge = Type("400", "0.875rem", "1.25rem", "0"),
            BodyMedium = Type("400", "0.8125rem", "1.125rem", "0"),
            BodySmall = Type("400", "0.75rem", "1rem", "0.01em"),
            LabelLarge = Type("600", "0.8125rem", "1.125rem", "0.01em"),
            LabelMedium = Type("600", "0.6875rem", "0.875rem", "0.03em"),
            LabelSmall = Type("700", "0.625rem", "0.75rem", "0.04em"),
        },

        // A single radius for data elements; the pill stays only for statuses and counters.
        Shape = d.Shape with
        {
            None = "0px",
            ExtraSmall = "4px",
            Small = "4px",
            Medium = "6px",
            Large = "8px",
            ExtraLarge = "12px",
            Full = "9999px",
        },

        // Borders instead of elevation: data lies on flat planes, only overlay raises a shadow.
        Elevation = d.Elevation with
        {
            Level0 = "none",
            Level1 = "none",
            Level2 = "0 2px 4px -1px rgba(15, 23, 42, 0.06), 0 1px 2px -1px rgba(15, 23, 42, 0.04)",
            Level3 = "0 10px 15px -3px rgba(15, 23, 42, 0.08), 0 4px 6px -4px rgba(15, 23, 42, 0.04)",
        },

        // Components are defined by Flare tokens, not CSS: the theme writes them into its stylesheet after the
        // asset stylesheets, and a variable from a style asset with the same selector loses to it.

        // "Primary: ... height 32px (compact), radius 6px". The MD3E pill is replaced by the shape radius.
        Button = d.Button with
        {
            ContainerRadius = Radius,
            RadiusXs = CornerRadiusTokens.All(Radius),
            RadiusSm = CornerRadiusTokens.All(Radius),
            RadiusMd = CornerRadiusTokens.All(Radius),
            RadiusLg = CornerRadiusTokens.All(Radius),
            RadiusXl = CornerRadiusTokens.All(Radius),
            IconOnlyRadiusXs = Radius,
            IconOnlyRadiusSm = Radius,
            IconOnlyRadiusMd = Radius,
            IconOnlyRadiusLg = Radius,
            IconOnlyRadiusXl = Radius,
            HeightXs = "28px",
            HeightSm = "32px",
            HeightMd = "32px",
            HeightLg = "40px",
            HeightXl = "48px",
            IconOnlySizeXs = "28px",
            IconOnlySizeSm = "32px",
            IconOnlySizeMd = "32px",
            IconOnlySizeLg = "40px",
            IconOnlySizeXl = "48px",
            PaddingInlineXs = "10px",
            PaddingInlineSm = "12px",
            PaddingInlineMd = "12px",
            PaddingInlineLg = "16px",
            PaddingInlineXl = "20px",
            IconSizeSm = "18px",
            IconSizeMd = "18px",
            OutlinedBorderColor = BorderStrong,
        },

        // "Standard Inputs: 30px compact height, #FFFFFF background, 1px border #CBD5E1".
        Input = d.Input with
        {
            HeightSm = "28px",
            HeightMd = "30px",
            OutlinedRadius = Radius,
            FilledVariantRadius = Radius,
            OutlinedVariantRadius = Radius,
            BorderColor = BorderStrong,
            OutlinedVariantBorderColor = BorderStrong,
        },

        // The design's menu item: about a 30px row, 4px radius; the active one is filled with primary-container,
        // the text is white and bold, the icon is 18px.
        Nav = d.Nav with
        {
            ItemHeight = "32px",
            ItemRadius = "4px",
            IndicatorRadius = "4px",
            ActiveIndicator = "#1D4ED8",
            ActiveColor = "#FFFFFF",
            ActiveWeight = "700",
            // Fluent draws a left accent bar; Stitch fills the whole active item, so the bar would double it.
            ActiveLeftBar = "none",
            IconSize = "18px",
        },

        // The design shell: a white 56px app bar and a white side panel on a #F8F9FF canvas,
        // separated by a light shadow rather than MD3 tonal fill.
        Layout = d.Layout with
        {
            AppBarHeight = "56px",
            AppBarBg = Plane,
            AppBarShadow = "0 1px 8px rgba(0, 0, 0, 0.04)",
            DrawerBg = Plane,
            RailBg = Plane,
            ShellBg = "var(--flare-color-surface)",
            ContentPadding = "1rem 1.25rem",
        },

        // "Level 1: Pure #FFFFFF cards ... bounded by an explicit 1px outline of #E2E8F0".
        Card = d.Card with
        {
            OutlinedBg = Plane,
            OutlinedBorder = "1px solid var(--alva-border)",
            Radius = Radius,
        },

        Alert = d.Alert with { Radius = Radius },

        // "Tree Indentation: 16px per hierarchy level"; the dashed guide is in alva.css.
        Tree = d.Tree with { Indent = "16px" },
    };

    /// <summary>The design's single radius for buttons, fields, cards and badges.</summary>
    private const string Radius = "6px";

    // Plane and border colors are palette roles and theme variables (alva.css), not hex: that way each mode has
    // its own values, and the light one matches the design (#FFFFFF, #CBD5E1, #E2E8F0).

    /// <summary>The plane of the app bar, menu and cards: white in the light scheme, darkest in the dark one.</summary>
    private const string Plane = "var(--flare-color-surface-container-lowest)";

    /// <summary>The border of fields and outlined buttons.</summary>
    private const string BorderStrong = "var(--alva-border-strong)";

    /// <summary>One step of the scale: typeface, weight, size, line height and letter spacing.</summary>
    private static TypeStyle Type(string weight, string size, string lineHeight, string spacing) => new()
    {
        FontFamily = UiFont,
        FontWeight = weight,
        FontSize = size,
        LineHeight = lineHeight,
        LetterSpacing = spacing,
    };
}
