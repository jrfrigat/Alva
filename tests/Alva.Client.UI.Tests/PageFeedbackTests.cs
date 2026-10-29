using System.Net;
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
