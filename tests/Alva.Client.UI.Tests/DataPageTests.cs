using Alva.Client.Pages;
using Alva.Contracts;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Страница «Данные» не сообщает об импорте, которого не было.</summary>
public sealed class DataPageTests : UiTestBase
{
    private IRenderedComponent<Data> Page() => Render<Data>();

    private static void ChooseFile(IRenderedComponent<Data> cut) =>
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("{\"tasks\":[]}", "team.json"));

    [Fact]
    public void Without_data_transfer_the_page_says_so_before_any_action_and_blocks_it()
    {
        Api.SupportsDataTransfer = false;
        var cut = Page();

        Assert.Contains("Импорт и экспорт недоступны в демо", cut.Markup);
        Assert.True(Button(cut, "Импортировать").HasAttribute("disabled"));
        // Экспорт — не ссылки на несуществующий сервер, а отключённые кнопки.
        Assert.All(new[] { "JSON", "CSV", "XLSX" }, f => Assert.DoesNotContain(cut.FindAll("a"), a => a.TextContent.Contains(f)));
        Assert.Equal(0, Api.Imports);
    }

    [Fact]
    public void A_failed_import_shows_the_reason_and_no_success()
    {
        Api.OnImport = () => new NotSupportedException("Импорт не поддерживается");
        var cut = Page();
        ChooseFile(cut);

        Button(cut, "Импортировать").Click();

        cut.WaitForAssertion(() => Assert.Contains("Не удалось выполнить действие. Повторите.", cut.Markup));
        Assert.DoesNotContain("Импорт завершён", cut.Markup);
    }

    [Fact]
    public void Success_is_shown_only_after_the_import_went_through()
    {
        Api.ImportResult = new DataImportResult(7, [], ["Связи пользователей не переносятся."]);
        var cut = Page();
        ChooseFile(cut);
        Assert.DoesNotContain("Импорт завершён", cut.Markup);

        Button(cut, "Импортировать").Click();

        cut.WaitForAssertion(() => Assert.Contains("Импорт завершён", cut.Markup));
        Assert.Contains("Импортировано записей: 7.", cut.Markup);
        Assert.Contains("Связи пользователей не переносятся.", cut.Markup);
        Assert.Equal(1, Api.Imports);
    }

    [Fact]
    public void The_file_name_and_preview_are_visible_before_import()
    {
        var cut = Page();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("title,status\nЗадача,Planning", "team.csv"));

        cut.WaitForAssertion(() => Assert.Contains("Файл: team.csv", cut.Markup));
        Assert.Contains("title,status", cut.Markup);
        Assert.Contains("Задача,Planning", cut.Markup);
        Assert.Equal(0, Api.Imports);
    }
}
