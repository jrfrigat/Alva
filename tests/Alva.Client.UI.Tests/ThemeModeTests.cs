using System.Text.RegularExpressions;
using Alva.Theme.Alva;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>
/// Theme planes and borders depend on the mode. Light literals in shell tokens or in styles
/// give light text on white in dark mode and in high contrast.
/// </summary>
public sealed partial class ThemeModeTests
{
    // Neutrals and design semantics that must come from the alva.css token table, not from a literal.
    private static readonly string[] Neutrals =
    [
        "#e2e8f0", "#cbd5e1", "#f8fafc", "#f1f5f9", "#f0f4ff",
        "#fee2e2", "#fef2f2", "#fff5f5", "#fca5a5", "#fef3c7", "#fcd34d", "#d1fae5", "#ecfdf5", "#6ee7b7",
        "#ede9fe", "#f5f3ff", "#c4b5fd", "#6d28d9", "#dce1ff", "#0039b5", "#dc2626", "#d97706", "#047857",
    ];

    private static readonly string[] Tones = ["critical", "warning", "ok", "role", "info"];

    // The text of each tone is readable (>= 4.5:1) on its own backgrounds and on the planes of its mode.
    [Theory]
    [InlineData(false, new[] { "#FFFFFF", "#F8F9FF", "#F8FAFC" })]
    [InlineData(true, new[] { "#06121F", "#0B1C30", "#12233A" })]
    public void Semantic_text_is_readable_on_its_backgrounds(bool dark, string[] planes)
    {
        var tokens = TokenBlock(Read("src/Alva.Theme.Alva/wwwroot/css/alva.css"), dark);
        var failures = new List<string>();
        foreach (var tone in Tones)
        {
            var text = tokens[$"--alva-{tone}-text"];
            var backgrounds = planes.Concat(new[] { "bg", "subtle" }.Select(k => $"--alva-{tone}-{k}").Where(tokens.ContainsKey).Select(k => tokens[k]));
            foreach (var bg in backgrounds)
                if (Contrast(text, bg) < 4.5) failures.Add($"{tone}: {text} на {bg} — {Contrast(text, bg):0.00}");
        }
        Assert.Empty(failures);
        Assert.True(Contrast("#FFFFFF", tokens.GetValueOrDefault("--alva-critical-solid", "#B91C1C")) >= 4.5);
    }

    private static Dictionary<string, string> TokenBlock(string css, bool dark)
    {
        var selector = dark ? @"\.flare-theme-alva\.flare-mode-dark,\s*\.flare-theme-alva\.flare-mode-high-contrast\s*\{" : @"\.flare-theme-alva\s*\{";
        var body = Regex.Match(Comments().Replace(css, ""), selector + "([^}]*)\\}").Groups[1].Value;
        return Regex.Matches(body, @"(--alva-[a-z-]+):\s*(#[0-9A-Fa-f]{6})").ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
    }

    private static double Contrast(string a, string b)
    {
        static double Luminance(string hex)
        {
            double Channel(int i) { var v = Convert.ToInt32(hex.Substring(1 + i * 2, 2), 16) / 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
            return 0.2126 * Channel(0) + 0.7152 * Channel(1) + 0.0722 * Channel(2);
        }
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Fact]
    public void Warning_role_uses_the_readable_alva_text_token()
    {
        var css = Comments().Replace(Read("src/Alva.Client.UI/wwwroot/css/app.css"), "");
        Assert.Contains(".flare-color-warning{--fc-main:var(--alva-warning-text)}", css);
    }

    [Fact]
    public void Selected_list_content_keeps_the_selected_color()
    {
        var css = Comments().Replace(Read("src/Alva.Client.UI/wwwroot/css/app.css"), "");
        Assert.Contains(".flare-list-item--selected .flare-color-on-surface-variant{color:inherit}", css);
        Assert.Contains(".flare-list-item--selected .flare-link{color:inherit}", css);
    }

    [Fact]
    public void Shell_card_and_field_tokens_follow_the_mode()
    {
        var design = AlvaTheme.Create().Design;
        string[] tokens =
        [
            design.Layout.AppBarBg, design.Layout.DrawerBg, design.Layout.RailBg, design.Layout.ShellBg,
            design.Card.OutlinedBg, design.Card.OutlinedBorder,
            design.Input.BorderColor, design.Input.OutlinedVariantBorderColor, design.Button.OutlinedBorderColor,
        ];
        Assert.All(tokens, token => Assert.DoesNotMatch(Hex(), token));
    }

    [Fact]
    public void Every_mode_has_its_own_scheme()
    {
        var palette = AlvaPalettes.Sapphire;
        Assert.NotNull(palette.HighContrast);
        Assert.NotEqual(palette.Light.SurfaceContainerLowest, palette.Dark.SurfaceContainerLowest);
        Assert.NotEqual(palette.Light.SurfaceContainerLowest, palette.HighContrast!.SurfaceContainerLowest);
    }

    [Fact]
    public void Theme_tokens_have_dark_and_high_contrast_values()
    {
        var css = Read("src/Alva.Theme.Alva/wwwroot/css/alva.css");
        Assert.Contains(".flare-theme-alva.flare-mode-dark,", css);
        Assert.Contains(".flare-theme-alva.flare-mode-high-contrast {", css);
    }

    [Theory]
    [InlineData("src/Alva.Client.UI/wwwroot/css/app.css")]
    [InlineData("src/Alva.Theme.Alva/wwwroot/css/alva.css")]
    public void Stylesheets_take_neutrals_from_the_tokens(string path)
    {
        var css = Comments().Replace(Read(path), "");
        // The theme token table and the splash before the theme loads are the only places where literals are legitimate.
        css = TokenTable().Replace(css, "");
        css = Regex.Replace(css, @"#flare-splash\{[^}]*\}", "");
        var found = Neutrals.Where(hex => css.Contains(hex, StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.Empty(found);
    }

    private static string Read(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Alva.slnx"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, relative));
    }

    [GeneratedRegex("#[0-9A-Fa-f]{3,8}\\b")]
    private static partial Regex Hex();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comments();

    [GeneratedRegex(@"\.flare-theme-alva[^{]*\{[^}]*--alva-border:[^}]*\}")]
    private static partial Regex TokenTable();
}
