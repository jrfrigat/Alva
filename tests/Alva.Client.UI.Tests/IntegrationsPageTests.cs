using Alva.Client.Models;
using Alva.Client.Pages;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Flare.Components;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Экран интеграций: форма по схеме модуля, секреты, синхронизация, удаление и ссылка на внешний ключ.</summary>
public sealed class IntegrationsPageTests : UiTestBase
{
    private static readonly IntegrationModuleDto Tracker = new("yandex-tracker", "Яндекс Трекер", "Задачи очередей", ["Tasks"],
    [
        new("organizationId", "Идентификатор организации", "Text", true, null, null, null),
        new("token", "Токен", "Secret", true, null, "OAuth-токен", null),
        new("readLinks", "Читать связи", "Flag", false, "true", null, null)
    ]);

    private static IntegrationConnectionDto Connection(string[]? secrets = null) => new(Guid.NewGuid(), "yandex-tracker", "Яндекс Трекер", "Трекер команды",
        new() { ["organizationId"] = "42" }, secrets ?? ["token"], true, 30, DateTimeOffset.UtcNow, true, "Получено задач: 3.", 3);

    private IRenderedComponent<FlareConfirmDialogProvider> Page(params IntegrationConnectionDto[] connections)
    {
        Api.Answer("integrations/modules", _ => new[] { Tracker })
           .Answer("integrations", r => r == "integrations" ? connections : null!);
        return Render<FlareConfirmDialogProvider>(p => p.AddChildContent<Integrations>());
    }

    [Fact]
    public void The_demo_explains_that_integrations_need_a_server()
    {
        Api.SupportsDataTransfer = false;

        var cut = Page();

        Assert.Contains("Интеграции недоступны в демо", cut.Markup);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Добавить подключение"));
    }

    [Fact]
    public void A_new_connection_is_built_from_the_module_schema_and_sends_the_secret_apart()
    {
        var cut = Page();
        Button(cut, "Добавить подключение").Click();

        Assert.Equal("password", Field(cut, "Токен *").GetAttribute("type"));
        Field(cut, "Идентификатор организации *").Change("42");
        Field(cut, "Токен *").Change("secret");
        Button(cut, "Сохранить").Click();

        cut.WaitForAssertion(() => Assert.Contains(Api.Sent, s => s.Method == HttpMethod.Post && s.Path.EndsWith("/integrations")));
        var request = (IntegrationConnectionRequest)Api.Sent.Single(s => s.Path.EndsWith("/integrations")).Value;
        Assert.Equal("yandex-tracker", request.Module);
        Assert.Equal("Яндекс Трекер", request.Name);
        Assert.Equal("42", request.Settings!["organizationId"]);
        Assert.Equal("secret", request.Secrets!["token"]);
        Assert.False(request.Settings.ContainsKey("token"));
    }

    [Fact]
    public void Required_fields_stop_the_form_before_sending()
    {
        var cut = Page();
        Button(cut, "Добавить подключение").Click();

        Button(cut, "Сохранить").Click();

        cut.WaitForAssertion(() => Assert.Contains("Заполните поле «Токен»", cut.Markup));
        Assert.DoesNotContain(Api.Sent, s => s.Path.EndsWith("/integrations"));
    }

    [Fact]
    public void A_saved_secret_is_not_required_again_and_not_sent_empty()
    {
        var connection = Connection();
        var cut = Page(connection);
        Button(cut, "Изменить").Click();

        Assert.Contains("Сохранён. Оставьте поле пустым, чтобы не менять.", cut.Markup);
        Button(cut, "Сохранить").Click();

        cut.WaitForAssertion(() => Assert.Contains(Api.Sent, s => s.Method == HttpMethod.Put));
        var request = (IntegrationConnectionRequest)Api.Sent.Single(s => s.Method == HttpMethod.Put).Value;
        Assert.Empty(request.Secrets!);
        Assert.Equal(30, request.AutoSyncMinutes);
    }

    [Fact]
    public void Sync_shows_what_was_created_and_updated()
    {
        var connection = Connection();
        Api.SaveResult = new IntegrationRunDto(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, true, 2, 1, 5, 0, "Получено задач: 8.");
        var cut = Page(connection);

        Button(cut, "Синхронизировать").Click();

        cut.WaitForAssertion(() => Assert.Contains("создано 2, обновлено 1, без изменений 5, пропущено 0.", cut.Markup));
        Assert.Contains(Api.Sent, s => s.Path.EndsWith($"integrations/{connection.Id}/sync"));
    }

    [Fact]
    public void Deleting_says_the_tasks_stay_and_can_be_cancelled()
    {
        var connection = Connection();
        var cut = Page(connection);

        Button(cut, "Удалить").Click();
        cut.WaitForAssertion(() => Assert.Contains("останутся в Alva", cut.Find("[role='alertdialog']").TextContent));
        cut.Find("[role='alertdialog']").QuerySelectorAll("button").First(b => b.TextContent.Trim() == "Отмена").Click();

        Assert.DoesNotContain(Api.Sent, s => s.Method == HttpMethod.Delete);
    }

    [Fact]
    public void A_task_from_a_tracker_links_to_its_key()
    {
        var task = new TaskModel { Id = Guid.NewGuid(), Title = "Отчёт", Status = "Planned", Version = 1, ExternalKey = "TEST-7", ExternalUrl = "https://tracker.yandex.ru/TEST-7" };
        Api.Answer($"tasks/{task.Id}/history", _ => Array.Empty<HistoryDto>());

        var cut = Render<FlareConfirmDialogProvider>(p => p.AddChildContent<TaskDetails>(d => d.Add(x => x.Item, task)));

        var link = cut.Find("a.alva-task__external");
        Assert.Equal("https://tracker.yandex.ru/TEST-7", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Contains("TEST-7", link.TextContent);
    }
}
