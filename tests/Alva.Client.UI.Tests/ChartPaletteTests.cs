using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Серии графика ёмкости проходят 3:1 к подложке в светлой, тёмной и высококонтрастной темах.</summary>
public sealed class ChartPaletteTests
{
    private static readonly string[] Series = ["fund", "planned", "deficit"];

    [Fact]
    public void Every_series_keeps_three_to_one_contrast_on_its_theme_surface()
    {
        var css = File.ReadAllText(Path.Combine(Root(), "src", "Alva.Theme.Alva", "wwwroot", "css", "alva.css"));
        var themes = new[]
        {
            (Name: "light", Block: Block(css, "\\.flare-theme-alva\\s*\\{"), Surface: "#FFFFFF"),
            (Name: "dark", Block: Block(css, "\\.flare-theme-alva\\.flare-mode-dark,\\s*\\.flare-theme-alva\\.flare-mode-high-contrast\\s*\\{"), Surface: "#06121F"),
            (Name: "high contrast", Block: Block(css, "\\.flare-theme-alva\\.flare-mode-high-contrast\\s*\\{(?=[^}]*--alva-chart-fund)"), Surface: "#000000"),
        };

        foreach (var theme in themes)
            foreach (var series in Series)
            {
                var color = Color(theme.Block, series, theme.Name);
                var ratio = Contrast(color, theme.Surface);
                Assert.True(ratio >= 3, $"{theme.Name}: {series} {color} on {theme.Surface} is {ratio:F2}:1");
            }
    }

    [Fact]
    public void The_heat_bar_track_is_a_theme_token_and_not_a_fixed_dark_overlay()
    {
        var css = File.ReadAllText(Path.Combine(Root(), "src", "Alva.Client.UI", "wwwroot", "css", "app.css"));
        Assert.Contains("background:var(--alva-heat-track)", css);
        Assert.DoesNotContain("rgba(15,23,42,.08)", css);
    }

    private static string Color(string block, string series, string theme)
    {
        var match = Regex.Match(block, "--alva-chart-" + series + @":\s*(#[0-9A-Fa-f]{6})");
        if (!match.Success) throw new InvalidOperationException($"{theme}: нет токена --alva-chart-{series}.");
        return match.Groups[1].Value;
    }

    private static string Block(string css, string selector)
    {
        var match = Regex.Match(css, selector + "(?<body>.*?)\\}", RegexOptions.Singleline);
        return match.Groups["body"].Value is { Length: > 0 } body
            ? body : throw new InvalidOperationException($"Нет блока {selector}.");
    }

    private static double Contrast(string first, string second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(string hex)
    {
        var channels = new[] { 1, 3, 5 }
            .Select(i => int.Parse(hex.AsSpan(i, 2), NumberStyles.HexNumber) / 255d)
            .Select(v => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4))
            .ToArray();
        return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
    }

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Alva.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Не найден корень репозитория.");
    }
}
