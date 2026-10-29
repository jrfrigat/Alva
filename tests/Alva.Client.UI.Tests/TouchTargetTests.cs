using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>На сенсорном экране цели не меньше 44px, поля 16px, а desktop-плотность не тронута.</summary>
public sealed class TouchTargetTests
{
    [Fact]
    public void Coarse_pointer_gets_44px_targets_and_phone_inputs_get_16px()
    {
        var css = File.ReadAllText(Path.Combine(Root(), "src", "Alva.Client.UI", "wwwroot", "css", "app.css"));

        var coarse = Block(css, "@media (pointer:coarse)");
        Assert.Contains("min-height:44px", coarse);
        Assert.Contains(".flare-btn", coarse);
        Assert.Contains(".alva-quickfilter__item", coarse);

        Assert.Contains("input,select,textarea{font-size:16px}", css);
        Assert.Contains(".alva-chip{font-size:12px", css);
    }

    private static string Block(string css, string selector)
    {
        var start = css.LastIndexOf(selector, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Нет {selector}");
        var open = css.IndexOf('{', start);
        var close = css.IndexOf('}', open);
        Assert.True(open >= 0 && close > open, $"Нет тела {selector}");
        return css[(open + 1)..close];
    }

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Alva.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Не найден корень репозитория.");
    }
}
