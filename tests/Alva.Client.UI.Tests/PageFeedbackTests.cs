using System.Net;
using Alva.Client.Services;
using Alva.Client.Shared;
using Bunit;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Классы ошибок переводятся в понятный текст, а временный сбой можно повторить.</summary>
public sealed class PageFeedbackTests : UiTestBase
{
    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "Проверьте введённые данные.")]
    [InlineData(HttpStatusCode.Unauthorized, "Сессия истекла. Войдите снова.")]
    [InlineData(HttpStatusCode.Forbidden, "Нет доступа: у вашей роли нет прав на эти данные. Попросите владельца пространства выдать доступ.")]
    [InlineData(HttpStatusCode.NotFound, "Запись не найдена: возможно, её уже удалили.")]
    [InlineData(HttpStatusCode.Conflict, "Данные уже изменились. Обновите страницу и повторите.")]
    [InlineData(HttpStatusCode.TooManyRequests, "Слишком много запросов. Подождите и повторите.")]
    [InlineData(HttpStatusCode.InternalServerError, "Сервер не смог выполнить действие. Повторите позже.")]
    public void Http_statuses_become_clear_text(HttpStatusCode status, string expected)
    {
        var error = new HttpRequestException("raw server text", null, status);

        Assert.Equal(expected, PageFeedback.Describe(error));
        Assert.DoesNotContain("raw server text", PageFeedback.Describe(error));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "Сначала переместите или архивируйте подзадачи.")]
    [InlineData(HttpStatusCode.NotFound, "Пользователь должен сначала зарегистрироваться.")]
    [InlineData(HttpStatusCode.Conflict, "Пользователь уже связан с участником команды.")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "Сначала снимите фиксацию дат.")]
    public void A_refusal_the_server_explained_keeps_its_words(HttpStatusCode status, string message)
    {
        Assert.Equal(message, PageFeedback.Describe(new ServerRefusalException(message, status)));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Сессия истекла. Войдите снова.")]
    [InlineData(HttpStatusCode.Forbidden, "Нет доступа: у вашей роли нет прав на эти данные. Попросите владельца пространства выдать доступ.")]
    [InlineData(HttpStatusCode.InternalServerError, "Сервер не смог выполнить действие. Повторите позже.")]
    public void Access_and_server_failures_stay_generic_even_when_explained(HttpStatusCode status, string expected)
    {
        Assert.Equal(expected, PageFeedback.Describe(new ServerRefusalException("System.NullReferenceException at ...", status)));
    }

    [Fact]
    public void Demo_api_rules_keep_their_words()
    {
        const string rule = "Даты задачи «Релиз» зафиксированы: изменение вышло за её окно. Снимите фиксацию, чтобы расширить окно.";

        Assert.Equal(rule, PageFeedback.Describe(new InvalidOperationException(rule)));
        Assert.Contains("требуют сервера", PageFeedback.Describe(new NotSupportedException("Demo: режимы требуют сервера.")));
    }

    [Fact]
    public void Network_and_timeout_errors_have_their_own_text()
    {
        Assert.Contains("связаться", PageFeedback.Describe(new HttpRequestException("connection refused")));
        Assert.Contains("вовремя", PageFeedback.Describe(new TaskCanceledException("timeout")));
    }

    [Fact]
    public void A_retry_button_reruns_the_action()
    {
        var calls = 0;
        var cut = Render<PageFeedback>(p => p
            .Add(x => x.Error, "Сбой")
            .Add(x => x.OnRetry, EventCallback.Factory.Create(this, (Action)(() => calls++))));

        Button(cut, "Повторить").Click();

        Assert.Equal(1, calls);
    }
}
