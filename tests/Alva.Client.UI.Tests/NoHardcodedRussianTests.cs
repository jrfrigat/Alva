using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Visible strings live in resources, not in markup: a static check catches Cyrillic in UI code.</summary>
public sealed class NoHardcodedRussianTests
{
    /// <summary>The language name in the switcher is shown in that language itself - a deliberate exception.</summary>
    private static readonly string[] Allowed = ["Русский"];

    private static readonly Regex StaticKey = new(
        "^(Auth|Nav|Tasks|Task|Sprint|Projects|Capacity|Risks|People|Settings|Integrations|Data|Workspace|Overview|Gantt|Common)_[A-Za-z0-9_]+$",
        RegexOptions.Compiled);

    private static readonly Regex DynamicKey = new("^(plural|reason)\\.", RegexOptions.Compiled);
    private static readonly Regex GeneratedProperty = new("public static string ([A-Za-z0-9_]+) =>", RegexOptions.Compiled);
    private static readonly Regex LiteralCall = new("Strings\\.T\\(\\s*\"", RegexOptions.Compiled);

    [Fact]
    public void Ui_code_has_no_hardcoded_russian_outside_resources()
    {
        var root = Path.Combine(Root(), "src", "Alva.Client.UI");
        var offenders = new List<string>();
        foreach (var file in SourceFiles(root))
        {
            var text = File.ReadAllText(file);
            text = BlankComments(text);
            foreach (var allowed in Allowed) text = text.Replace($"\"{allowed}\"", "");
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
                if (lines[i].Any(c => c is >= 'А' and <= 'я' or 'Ё' or 'ё'))
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {lines[i].Trim()}");
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Resource_keys_are_symbolic_and_translated_in_both_languages()
    {
        var en = Read("AppStrings.resx");
        var ru = Read("AppStrings.ru.resx");

        Assert.Equal(en.Keys.Order(), ru.Keys.Order());
        Assert.All(en, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), $"empty en value: {pair.Key}"));
        Assert.All(ru, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), $"empty ru value: {pair.Key}"));
        Assert.DoesNotContain(en.Keys, key => key.Any(c => c is >= 'А' and <= 'я' or 'Ё' or 'ё'));

        var invalid = en.Keys
            .Where(key => !StaticKey.IsMatch(key) && !DynamicKey.IsMatch(key))
            .ToArray();
        Assert.Empty(invalid);
    }

    [Fact]
    public void Generated_class_matches_static_resource_keys()
    {
        var en = Read("AppStrings.resx");
        var staticKeys = en.Keys.Where(key => !DynamicKey.IsMatch(key)).Order().ToArray();
        var designer = File.ReadAllText(Path.Combine(Root(), "src", "Alva.Client.UI", "Resources", "AlvaStrings.Designer.cs"));
        var properties = GeneratedProperty.Matches(designer)
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(staticKeys, properties.Order());
        Assert.Equal(properties.Length, properties.Distinct().Count());
        Assert.Contains("new ResourceManager(\"Alva.Client.AppStrings\", typeof(AppStrings).Assembly)", designer, StringComparison.Ordinal);
    }

    [Fact]
    public void Ui_code_uses_typed_strings_for_literals()
    {
        var root = Path.Combine(Root(), "src", "Alva.Client.UI");
        var offenders = SourceFiles(root)
            .Where(file => LiteralCall.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(root, file))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static string BlankComments(string text)
    {
        text = Regex.Replace(text, "@\\*.*?\\*@", Blank, RegexOptions.Singleline);
        text = Regex.Replace(text, "<!--.*?-->", Blank, RegexOptions.Singleline);
        text = Regex.Replace(text, "/\\*.*?\\*/", Blank, RegexOptions.Singleline);
        text = Regex.Replace(text, "///.*$", Blank, RegexOptions.Multiline);
        return Regex.Replace(text, "//.*$", Blank, RegexOptions.Multiline);
    }

    private static string Blank(Match match) => Regex.Replace(match.Value, "[^\r\n]", " ");

    private static IEnumerable<string> SourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Resources{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static Dictionary<string, string> Read(string file)
    {
        var path = Path.Combine(Root(), "src", "Alva.Client.UI", "Resources", file);
        return XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "", StringComparer.Ordinal);
    }

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Alva.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Не найден корень репозитория.");
    }
}
