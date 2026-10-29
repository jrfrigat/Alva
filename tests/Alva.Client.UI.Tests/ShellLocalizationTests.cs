using System.Text.Json;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Оболочка и manifest следуют языку: html lang, splash, ошибка и нужный manifest.</summary>
public sealed class ShellLocalizationTests
{
    [Theory]
    [InlineData("src/Alva.Client/wwwroot/index.html")]
    [InlineData("src/Alva.Client.Demo/wwwroot/index.html")]
    public void The_shell_switches_language_and_manifest(string file)
    {
        var html = File.ReadAllText(Path.Combine(Root(), file));

        Assert.Contains("document.documentElement.lang", html);
        Assert.Contains("manifest.ru.webmanifest", html);
        Assert.Contains("manifest.webmanifest", html);
        Assert.DoesNotContain("<html lang=\"ru\">", html);
        Assert.DoesNotContain("Загружаем Alva", html.Split("<script>")[0]);
    }

    [Theory]
    [InlineData("src/Alva.Client/wwwroot", "en", "Alva — team planning")]
    [InlineData("src/Alva.Client.Demo/wwwroot", "en", "Alva — demo")]
    [InlineData("src/Alva.Client/wwwroot", "ru", "Alva — планирование команды")]
    [InlineData("src/Alva.Client.Demo/wwwroot", "ru", "Alva — демо")]
    public void The_manifest_uses_the_alva_palette_and_language(string folder, string language, string name)
    {
        var file = language == "ru" ? "manifest.ru.webmanifest" : "manifest.webmanifest";
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), folder, file)));
        var root = document.RootElement;

        Assert.Equal(language, root.GetProperty("lang").GetString());
        Assert.Equal(name, root.GetProperty("name").GetString());
        Assert.Equal("#F8F9FF", root.GetProperty("background_color").GetString());
        Assert.Equal("#1D4ED8", root.GetProperty("theme_color").GetString());
    }

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Alva.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Не найден корень репозитория.");
    }
}
