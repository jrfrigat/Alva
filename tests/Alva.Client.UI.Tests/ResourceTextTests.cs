using System.Xml.Linq;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Тексты интерфейса — обычные символы: Blazor выводит строку как текст, и «&amp;lt;» в ресурсе видно на экране.</summary>
public sealed class ResourceTextTests
{
    [Theory]
    [InlineData("AppStrings.resx")]
    [InlineData("AppStrings.ru.resx")]
    public void No_resource_key_or_value_carries_an_html_entity(string file)
    {
        var path = Path.Combine(Root(), "src", "Alva.Client.UI", "Resources", file);
        var entries = XDocument.Load(path).Root!.Elements("data")
            .SelectMany(d => new[] { (string)d.Attribute("name")!, (string?)d.Element("value") ?? "" });

        Assert.DoesNotContain(entries, text => text.Contains("&lt;") || text.Contains("&gt;") || text.Contains("&amp;"));
    }

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Alva.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Не найден корень репозитория.");
    }
}
