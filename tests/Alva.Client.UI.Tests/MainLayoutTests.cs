using Alva.Client.Layout;
using Alva.Client.Models;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>В оболочке ровно один основной landmark, и skip-link ведёт именно в него.
/// Вошедшему пользователю выход доступен прямо из шапки.</summary>
public sealed class MainLayoutTests : UiTestBase
{
    private BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

    [Fact]
    public void The_shell_has_one_main_landmark_that_is_the_skip_link_target()
    {
        var cut = Render<MainLayout>();

        // FlareLayoutContent рендерит единственный <main>; своей обёртки <main> быть не должно.
        var main = cut.FindAll("main");
        Assert.Single(main);
        Assert.Equal("main-content", main[0].Id);
        // Контейнер программно фокусируем, иначе skip-link не переводит фокус в контент.
        Assert.Equal("-1", main[0].GetAttribute("tabindex"));

        // Цель ссылки «К содержимому» — тот же основной landmark.
        Assert.Equal("#main-content", cut.Find("a.skip-link").GetAttribute("href"));
    }

    [Fact]
    public void The_signed_in_shell_offers_a_way_to_log_out()
    {
        Api.User = new UserInfo(Guid.NewGuid(), "anna@company.ru", "Анна");
        var cut = Render<MainLayout>();

        Button(cut, "Выйти").Click();

        Assert.EndsWith("/login", Navigation.Uri);
    }

    // Нижняя панель на телефоне несёт короткие подписи, а полные остаются доступным именем и подсказкой:
    // в отчёте она обрезала длинные названия ёмкости, а смысл разделов терялся.
    [Fact]
    public void The_phone_navigation_is_short_while_the_drawer_keeps_the_full_names()
    {
        // Четыре пункта — предел нижней панели; каждый обязан иметь короткую подпись, которая помещается.
        var primary = NavigationItems.Primary;
        Assert.InRange(primary.Length, 1, 5);
        Assert.All(primary, item => Assert.False(string.IsNullOrWhiteSpace(item.ShortLabel)));
        Assert.All(primary, item => Assert.True(item.ShortLabel!.Length <= 10, $"«{item.ShortLabel}» не поместится на узком экране"));
        Assert.Equal(primary.Length, primary.Select(item => item.ShortLabel).Distinct().Count());

        // Полные названия никуда не делись: их показывает боковое меню и подсказка пункта.
        var cut = Render<MainLayout>();
        foreach (var item in primary) Assert.Contains(item.Label, cut.Markup);
    }
}
