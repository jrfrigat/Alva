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
}
