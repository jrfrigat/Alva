using System.Globalization;
using System.Xml.Linq;
using Alva.Client.Shared;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Склонения берутся из ресурсов и следуют грамматике текущего языка, а не русской по умолчанию.</summary>
public sealed class PluralTests
{
    [Theory]
    [InlineData("en", 0, "tasks")]
    [InlineData("en", 1, "task")]
    [InlineData("en", 2, "tasks")]
    [InlineData("en", 5, "tasks")]
    [InlineData("ru", 1, "задача")]
    [InlineData("ru", 2, "задачи")]
    [InlineData("ru", 5, "задач")]
    [InlineData("ru", 11, "задач")]
    [InlineData("ru", 21, "задача")]
    public void A_noun_takes_the_grammar_of_the_current_language(string culture, int count, string expected)
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            Assert.Equal(expected, AlvaFormat.Plural(count, "task"));
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    [Fact]
    public void Every_plural_noun_has_all_three_forms_in_both_resources()
    {
        var en = Read("AppStrings.resx");
        var ru = Read("AppStrings.ru.resx");

        Assert.Equal(en.Keys.Order(), ru.Keys.Order());
        string[] nouns = ["task", "person", "team", "sprint", "risk", "week", "day", "employee", "subtask", "project", "subteam", "taskIncluded"];
        foreach (var noun in nouns)
            foreach (var form in new[] { "one", "few", "many" })
                Assert.True(en.ContainsKey($"plural.{noun}.{form}"), $"Нет ключа plural.{noun}.{form} в английских ресурсах.");
    }

    [Fact]
    public void Resource_keys_do_not_differ_only_by_case()
    {
        foreach (var file in new[] { "AppStrings.resx", "AppStrings.ru.resx" })
        {
            var keys = Read(file).Keys;
            var duplicates = keys.GroupBy(k => k, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => string.Join(" / ", g));
            Assert.Empty(duplicates);
        }
    }

    [Fact]
    public void The_english_resources_are_english()
    {
        foreach (var (key, value) in Read("AppStrings.resx"))
        {
            Assert.DoesNotMatch("[А-Яа-яЁё]", value);
            Assert.NotEqual(key, value);
        }
    }

    [Fact]
    public void No_plural_call_passes_a_russian_form()
    {
        var root = Path.Combine(Root(), "src", "Alva.Client.UI");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            for (var at = text.IndexOf("AlvaFormat.Plural(", StringComparison.Ordinal); at >= 0;
                 at = text.IndexOf("AlvaFormat.Plural(", at + 1, StringComparison.Ordinal))
            {
                var arguments = Arguments(text, at + "AlvaFormat.Plural".Length);
                if (arguments.Any(c => c is >= 'А' and <= 'я' or 'Ё' or 'ё'))
                    offenders.Add($"{Path.GetRelativePath(root, file)}: {arguments}");
            }
        }

        Assert.Empty(offenders);
    }

    private static string Arguments(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0) return text[(open + 1)..i];
        return text[open..];
    }

    private static Dictionary<string, string> Read(string file)
    {
        var path = Path.Combine(Root(), "src", "Alva.Client.UI", "Resources", file);
        return XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");
    }

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Alva.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Не найден корень репозитория.");
    }
}
