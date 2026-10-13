using System.Net;
using Alva.Client.Pages;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>TASK-71: ошибки входа и регистрации стоят у своих полей, связаны с ними и уводят на них фокус.</summary>
public sealed class LoginFormTests : UiTestBase
{
    private BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

    private IRenderedComponent<Login> Page(bool register = false)
    {
        var cut = Render<Login>();
        if (register) Button(cut, "Создать аккаунт").Click();
        return cut;
    }

    private static void Submit(IRenderedComponent<Login> cut) => cut.Find("form").Submit();

    // Ошибка поля видна под ним и объявляется через aria-describedby поля.
    private static string? ErrorOf(IRenderedComponent<Login> cut, string label)
    {
        var input = Field(cut, label);
        var described = input.GetAttribute("aria-describedby");
        return described is null ? null : cut.FindAll($"#{described}").FirstOrDefault()?.TextContent.Trim() is { Length: > 0 } text && input.GetAttribute("aria-invalid") == "true" ? text : null;
    }

    [Fact]
    public void An_empty_registration_marks_every_field_and_sends_nothing()
    {
        var cut = Page(register: true);

        Submit(cut);

        Assert.Equal("Укажите имя.", ErrorOf(cut, "Ваше имя"));
        Assert.Equal("Укажите название пространства.", ErrorOf(cut, "Название рабочего пространства"));
        Assert.Equal("Укажите электронную почту.", ErrorOf(cut, "Электронная почта"));
        Assert.Equal("Введите пароль.", ErrorOf(cut, "Пароль"));
        Assert.Empty(Api.Authentications);
        // Фокус уходит на первое поле с ошибкой.
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public void A_short_password_and_a_bad_email_are_reported_at_their_fields()
    {
        var cut = Page(register: true);
        Field(cut, "Ваше имя").Change("Анна");
        Field(cut, "Название рабочего пространства").Change("Продукт");
        Field(cut, "Электронная почта").Change("anna@");
        Field(cut, "Пароль").Change("short");

        Submit(cut);

        Assert.Contains("неверном формате", ErrorOf(cut, "Электронная почта"));
        Assert.Contains("от 12 до 128", ErrorOf(cut, "Пароль"));
        Assert.Null(ErrorOf(cut, "Ваше имя"));
        Assert.Empty(Api.Authentications);
    }

    [Fact]
    public void Editing_a_field_clears_its_error()
    {
        var cut = Page();
        Submit(cut);
        Assert.NotNull(ErrorOf(cut, "Электронная почта"));

        Field(cut, "Электронная почта").Change("anna@company.ru");

        Assert.Null(ErrorOf(cut, "Электронная почта"));
        Assert.NotNull(ErrorOf(cut, "Пароль"));
    }

    [Fact]
    public void A_taken_email_is_reported_at_the_email_field()
    {
        Api.OnAuthenticate = _ => new HttpRequestException("Аккаунт с этим email уже существует.", null, HttpStatusCode.Conflict);
        var cut = Page(register: true);
        Field(cut, "Ваше имя").Change("Анна");
        Field(cut, "Название рабочего пространства").Change("Продукт");
        Field(cut, "Электронная почта").Change("anna@company.ru");
        Field(cut, "Пароль").Change("достаточно-длинный");

        Submit(cut);

        cut.WaitForAssertion(() => Assert.Equal("Аккаунт с этим email уже существует.", ErrorOf(cut, "Электронная почта")));
        Assert.DoesNotContain("Не удалось выполнить действие", cut.Markup);
    }

    [Fact]
    public void The_register_route_opens_the_registration_form()
    {
        Navigation.NavigateTo("/register");
        var cut = Render<Login>();

        // Маршрут /register показывает форму регистрации, без переключения кнопкой.
        Assert.NotNull(Field(cut, "Ваше имя"));
        Assert.NotNull(Field(cut, "Название рабочего пространства"));
    }


    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Неверный email или пароль, либо вход временно заблокирован.")]
    [InlineData(HttpStatusCode.InternalServerError, "Запрос не выполнен (500).")]
    public void Other_server_refusals_go_to_the_summary_with_the_server_text(HttpStatusCode status, string message)
    {
        Api.OnAuthenticate = _ => new HttpRequestException(message, null, status);
        var cut = Page();
        Field(cut, "Электронная почта").Change("anna@company.ru");
        Field(cut, "Пароль").Change("пароль");

        Submit(cut);

        cut.WaitForAssertion(() => Assert.Contains(message, cut.Find(".flare-alert").TextContent));
        Assert.Null(ErrorOf(cut, "Электронная почта"));
        Assert.Single(Api.Authentications);
    }
}
