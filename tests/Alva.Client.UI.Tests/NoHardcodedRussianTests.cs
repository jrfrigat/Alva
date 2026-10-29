using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Видимые строки живут в ресурсах, а не в разметке: статическая проверка ловит кириллицу в UI-коде.</summary>
public sealed class NoHardcodedRussianTests
{
    /// <summary>Название языка в переключателе показывается на нём самом — это осознанное исключение.</summary>
    private static readonly string[] Allowed = ["Русский"];

    [Fact]
    public void Ui_code_has_no_hardcoded_russian_outside_resources()
    {
        var root = Path.Combine(Root(), "src", "Alva.Client.UI");
        var offenders = new List<string>();
        foreach (var file in SourceFiles(root))
        {
            var text = File.ReadAllText(file);
            text = BlankCommentsAndKeys(text);
            foreach (var allowed in Allowed) text = text.Replace($"\"{allowed}\"", "");
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
                if (lines[i].Any(c => c is >= 'А' and <= 'я' or 'Ё' or 'ё'))
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {lines[i].Trim()}");
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Every_key_used_in_the_ui_has_both_translations()
    {
        var en = Read("AppStrings.resx");
        var ru = Read("AppStrings.ru.resx");
        var missing = new List<string>();
        foreach (var file in SourceFiles(Path.Combine(Root(), "src", "Alva.Client.UI")))
            foreach (Match match in Regex.Matches(File.ReadAllText(file), "Strings\\.T\\(\\s*\"([^\"]+)\"\\s*\\)"))
            {
                var key = match.Groups[1].Value;
                if (!en.ContainsKey(key)) missing.Add($"en: {key}");
                if (!ru.ContainsKey(key)) missing.Add($"ru: {key}");
            }

        Assert.Empty(missing.Distinct());
    }

    private static string BlankCommentsAndKeys(string text)
    {
        text = Regex.Replace(text, "@\\*.*?\\*@", Blank, RegexOptions.Singleline);
        text = Regex.Replace(text, "<!--.*?-->", Blank, RegexOptions.Singleline);
        text = Regex.Replace(text, "/\\*.*?\\*/", Blank, RegexOptions.Singleline);
        text = Regex.Replace(text, "///.*$", Blank, RegexOptions.Multiline);
        text = Regex.Replace(text, "//.*$", Blank, RegexOptions.Multiline);
        return Regex.Replace(text, "Strings\\.T\\(\\s*\"[^\"]*\"\\s*\\)", Blank);
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
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "", StringComparer.OrdinalIgnoreCase);
    }

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Alva.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Не найден корень репозитория.");
    }
}
