using System.Globalization;
using System.Xml.Linq;
using Alva.Client.Models;
using Alva.Client.Pages;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Причины приходят кодами и переводятся одним слоем; английский текст не содержит кириллицы.</summary>
public sealed class ReasonTextTests : UiTestBase
{
    private static readonly Dictionary<string, string[]> SampleArgs = new()
    {
        [ReasonCodes.RiskRequiredPerson] = ["Backend"],
        [ReasonCodes.RiskAbsence] = ["Vacation", "2026-10-12", "2026-10-13"],
        [ReasonCodes.RiskOverload] = ["Anna"],
        [ReasonCodes.BalanceOverloadDrop] = ["Anna", "12", "4"],
        [ReasonCodes.BalanceFreeHours] = ["Boris", "6"],
        [ReasonCodes.BalanceLateDays] = ["2", "0"]
    };

    [Fact]
    public void Reasons_with_the_same_code_and_arguments_are_equal()
    {
        Assert.Equal(new ReasonDto(ReasonCodes.RiskOverload, ["Anna"]), new ReasonDto(ReasonCodes.RiskOverload, ["Anna"]));
        Assert.NotEqual(new ReasonDto(ReasonCodes.RiskOverload, ["Anna"]), new ReasonDto(ReasonCodes.RiskOverload, ["Boris"]));
    }

    [Fact]
    public void Every_reason_code_has_a_translation_in_both_languages()
    {
        var en = Read("AppStrings.resx");
        var ru = Read("AppStrings.ru.resx");

        foreach (var code in ReasonCodes.All)
        {
            Assert.True(en.ContainsKey($"reason.{code}"), $"Нет английского текста для {code}.");
            Assert.True(ru.ContainsKey($"reason.{code}"), $"Нет русского текста для {code}.");
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void Every_reason_renders_without_leftover_placeholders(string culture)
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            foreach (var code in ReasonCodes.All)
            {
                var text = ReasonText.Describe(new ReasonDto(code, SampleArgs.GetValueOrDefault(code)));
                Assert.DoesNotContain("{", text);
                Assert.NotEqual($"reason.{code}", text);
            }
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    [Fact]
    public void English_reasons_contain_no_cyrillic()
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            foreach (var code in ReasonCodes.All)
            {
                var text = ReasonText.Describe(new ReasonDto(code, SampleArgs.GetValueOrDefault(code)));
                Assert.DoesNotMatch("[А-Яа-яЁё]", text);
            }
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    [Fact]
    public void The_reason_filter_selects_by_code_in_english()
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            var overdue = new TaskModel { Id = Guid.NewGuid(), Title = "Overdue task", Deadline = new DateOnly(2026, 10, 1) };
            var overloaded = new TaskModel { Id = Guid.NewGuid(), Title = "Overloaded task", Deadline = new DateOnly(2026, 10, 20) };
            Api.Answer("risks", _ => new[]
               {
                   new RiskDto(overdue.Id, overdue.Title, "High", [new ReasonDto(ReasonCodes.RiskOverdue)], null),
                   new RiskDto(overloaded.Id, overloaded.Title, "Medium", [new ReasonDto(ReasonCodes.RiskOverload, ["Anna"])], null)
               })
               .Answer("tasks", _ => new[] { overdue, overloaded })
               .Answer("actors", _ => Array.Empty<ActorModel>());

            var cut = Render<Analytics>();

            cut.WaitForAssertion(() => Assert.Equal(2, Rows(cut)));
            cut.FindAll(".alva-quickfilter__item").First(b => b.TextContent.Contains("Past due")).Click();
            cut.WaitForAssertion(() => Assert.Equal(1, Rows(cut)));
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    private static int Rows<T>(IRenderedComponent<T> cut) where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll(".alva-risk-table__row:not(.alva-risk-table__row--head)").Count;

    [Fact]
    public void The_risk_table_renders_reasons_in_english_without_cyrillic()
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            var task = new TaskModel { Id = Guid.NewGuid(), Title = "Report", Deadline = new DateOnly(2026, 10, 20) };
            var reasons = ReasonCodes.All.Select(code => new ReasonDto(code, SampleArgs.GetValueOrDefault(code))).ToArray();
            Api.Answer("risks", _ => new[] { new RiskDto(task.Id, task.Title, "High", reasons, new DateOnly(2026, 10, 27), reasons) })
               .Answer("tasks", _ => new[] { task })
               .Answer("actors", _ => Array.Empty<ActorModel>());

            var cut = Render<Analytics>();

            cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".alva-risk-table__reasons .alva-chip")));
            Assert.DoesNotMatch("[А-Яа-яЁё]", cut.Find(".alva-risk-table__reasons").TextContent);
        }
        finally { CultureInfo.CurrentUICulture = before; }
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
