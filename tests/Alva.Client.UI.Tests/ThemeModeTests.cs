using System.Text.RegularExpressions;
using Alva.Theme.Alva;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>
/// TASK-67: плоскости и рамки темы зависят от режима. Светлые литералы в токенах оболочки или в стилях
/// дают в тёмном режиме и в высокой контрастности светлый текст на белом.
/// </summary>
public sealed partial class ThemeModeTests
{
    // Нейтрали дизайна, которые должны приходить из таблицы токенов alva.css, а не литералом.
    private static readonly string[] Neutrals = ["#e2e8f0", "#cbd5e1", "#f8fafc", "#f1f5f9", "#f0f4ff"];

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
        // Таблица токенов темы и заставка до загрузки темы — единственные места, где литералы законны.
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

    [GeneratedRegex(@"\.flare-theme-alva\s*\{[^}]*--alva-border:[^}]*\}")]
    private static partial Regex TokenTable();
}
