using System.Net;
using Alva.Client.Pages;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Ошибки входа и регистрации стоят у своих полей, связаны с ними и уводят на них фокус.</summary>
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
        Assert.Equal("Повторите пароль.", ErrorOf(cut, "Повторите пароль"));
        Assert.Empty(Api.Authentications);
        // Фокус уходит на первое поле с ошибкой — после yield внутри формы.
        cut.WaitForAssertion(() => JSInterop.VerifyFocusAsyncInvoke());
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

        // The field re-renders through the change handler; wait for it before reading the error back.
        cut.WaitForAssertion(() => Assert.Null(ErrorOf(cut, "Электронная почта")));
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
        Field(cut, "Повторите пароль").Change("достаточно-длинный");

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

    [Fact]
    public void The_password_can_be_shown_and_hidden_without_losing_focus()
    {
        var cut = Page();
        Assert.Equal("password", Field(cut, "Пароль").GetAttribute("type"));

        Button(cut, "Показать пароль").Click();

        cut.WaitForAssertion(() => Assert.Equal("text", Field(cut, "Пароль").GetAttribute("type")));
        Assert.NotNull(Button(cut, "Скрыть пароль"));
        JSInterop.VerifyFocusAsyncInvoke();

        Button(cut, "Скрыть пароль").Click();
        cut.WaitForAssertion(() => Assert.Equal("password", Field(cut, "Пароль").GetAttribute("type")));
    }

    [Fact]
    public void Several_errors_are_summarized_and_each_item_focuses_its_field()
    {
        var cut = Page(register: true);
        Submit(cut);

        cut.WaitForAssertion(() => Assert.Contains("Укажите имя.", cut.Find(".alva-form-errors").TextContent));
        var summary = cut.Find(".alva-form-errors");
        Assert.Equal("-1", summary.GetAttribute("tabindex"));
        Assert.Contains("Укажите название пространства.", summary.TextContent);
        Assert.Contains("Повторите пароль.", summary.TextContent);
        cut.WaitForAssertion(() => Assert.True(FocusCalls() > 0));
        var before = FocusCalls();

        summary.QuerySelectorAll("button").First(b => b.TextContent.Contains("Укажите название пространства.")).Click();

        cut.WaitForAssertion(() => Assert.True(FocusCalls() > before));
    }

    [Fact]
    public void Registration_requires_the_same_password_twice()
    {
        var cut = Page(register: true);
        Field(cut, "Ваше имя").Change("Анна");
        Field(cut, "Название рабочего пространства").Change("Продукт");
        Field(cut, "Электронная почта").Change("anna@company.ru");
        Field(cut, "Пароль").Change("достаточно-длинный");
        Field(cut, "Повторите пароль").Change("другой-достаточно-длинный");

        Submit(cut);

        Assert.Equal("Пароли не совпадают.", ErrorOf(cut, "Повторите пароль"));
        Assert.Empty(Api.Authentications);
    }

    [Fact]
    public void A_returning_user_opens_the_overview_without_choosing_a_team_again()
    {
        Api.SelectedTeamId = Guid.NewGuid();
        var cut = Page();
        Field(cut, "Электронная почта").Change("anna@company.ru");
        Field(cut, "Пароль").Change("пароль");

        Submit(cut);

        cut.WaitForAssertion(() => Assert.EndsWith("/", Navigation.Uri));
        Assert.Single(Api.Authentications);
    }

    [Fact]
    public void A_user_without_a_team_opens_the_workspace_choice()
    {
        Api.SelectedTeamId = null;
        var cut = Page();
        Field(cut, "Электронная почта").Change("anna@company.ru");
        Field(cut, "Пароль").Change("пароль");

        Submit(cut);

        cut.WaitForAssertion(() => Assert.EndsWith("/workspaces", Navigation.Uri));
    }

    [Fact]
    public void After_login_the_user_returns_to_the_page_that_asked_for_it()
    {
        Navigation.NavigateTo("/login?returnUrl=tasks");
        var cut = Render<Login>();
        Field(cut, "Электронная почта").Change("anna@company.ru");
        Field(cut, "Пароль").Change("пароль");

        Submit(cut);

        cut.WaitForAssertion(() => Assert.EndsWith("/tasks", Navigation.Uri));
    }

    [Theory]
    [InlineData("https%3A%2F%2Fevil.example%2Fsteal")]
    [InlineData("%2F%2Fevil.example%2Fsteal")]
    public void An_external_return_url_is_ignored(string encoded)
    {
        Navigation.NavigateTo($"/login?returnUrl={encoded}");
        var cut = Render<Login>();
        Field(cut, "Электронная почта").Change("anna@company.ru");
        Field(cut, "Пароль").Change("пароль");

        Submit(cut);

        cut.WaitForAssertion(() => Assert.EndsWith("/", Navigation.Uri));
        Assert.DoesNotContain("evil.example", Navigation.Uri);
    }

    [Fact]
    public void The_login_explains_how_to_recover_a_password()
    {
        Assert.Contains("администратору рабочего пространства", Page().Markup);
        Assert.DoesNotContain("администратору рабочего пространства", Page(register: true).Markup);
    }

    private int FocusCalls() => JSInterop.Invocations.Count(i => i.Identifier.EndsWith("focus", StringComparison.OrdinalIgnoreCase));
}
