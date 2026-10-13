using Alva.Client.Layout;
using Alva.Client.Models;
using Alva.Client.Pages;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Демо публикуется в подпапке хоста (https://jrfrigat.github.io/Alva/), поэтому адреса внутри
/// приложения относительные: их резолвит базовый адрес страницы, а текущий путь читается от того же
/// базового адреса. Путь с ведущим «/» теряет подпапку и уводит в корень хостинга — это и видел
/// пользователь, когда пункт меню открывал /sprints вместо /Alva/sprints.</summary>
public sealed class BasePathTests : UiTestBase
{
    // Так выглядит хост демо: и адрес страницы, и базовый адрес лежат в подпапке /Alva/.
    private static readonly Uri Host = new("https://jrfrigat.github.io/Alva/");

    private BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

    // Менеджер навигации с произвольным адресом: подменяет то, что в приложении задаёт <base href>.
    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation(string baseUri, string uri) => Initialize(baseUri, uri);
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    [Theory]
    [InlineData("https://host/", "https://host/tasks", "tasks")]
    [InlineData("https://host/Alva/", "https://host/Alva/tasks", "tasks")]
    [InlineData("https://host/Alva/", "https://host/Alva/", "")]
    [InlineData("https://host/Alva/", "https://host/Alva/sprints/1/plan?x=1", "sprints/1/plan")]
    public void The_current_path_is_read_from_the_host_base(string baseUri, string uri, string expected) =>
        Assert.Equal(expected, AppRoutes.Current(new TestNavigation(baseUri, uri)));

    [Fact]
    public void The_menu_opens_its_sections_inside_the_host_base_path()
    {
        var cut = Render<MainLayout>();

        AssertInsideHostBase(cut.FindAll("a[href]").Select(link => link.GetAttribute("href")), "меню");
    }

    [Fact]
    public void The_page_opens_its_sections_inside_the_host_base_path()
    {
        Api.Answer("tasks", _ => Array.Empty<TaskModel>())
           .Answer("projects", _ => Array.Empty<ProjectModel>())
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("sprints", _ => Array.Empty<SprintModel>());
        Navigation.NavigateTo("/tasks");
        var cut = Render<Tasks>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("a[href]")));

        AssertInsideHostBase(cut.FindAll("a[href]").Select(link => link.GetAttribute("href")), "страница задач");
    }

    // Класс дефекта, которого рендер-тесты не видят по построению: в тестах базовый адрес равен корню,
    // поэтому «/tasks» и «tasks» дают один и тот же Uri, а на хосте в подпапке — разные страницы. Сторож
    // читает исходники приложения и ищет адрес раздела с ведущим «/» в ссылке или в переходе: именно так
    // в этой карточке четыре раза подряд ускользал адрес, собранный тернарным выражением.
    [Fact]
    public void No_source_links_to_a_section_from_the_origin_root()
    {
        // Сторож обязан кусаться: правило ловит ссылку и переход, но не объявление маршрута и не
        // сравнение текущего пути — иначе он молча пропускал бы ровно то, ради чего написан.
        Assert.True(LinksToRoot("<FlareButton Href=\"/tasks\">"));
        Assert.True(LinksToRoot("Navigation.NavigateTo(flag ? \"/gantt/2d\" : \"/gantt/people\");"));
        Assert.False(LinksToRoot("@page \"/tasks\""));
        Assert.False(LinksToRoot("private bool People => Path.EndsWith(\"/people\");"));

        var offenders = new List<string>();
        foreach (var file in SourceFiles())
        {
            offenders.AddRange(File.ReadLines(file)
                .Where(LinksToRoot)
                .Select(line => $"{Path.GetFileName(file)}: {line.Trim()}"));
        }

        Assert.Empty(offenders);
    }

    // Ссылка нарушает правило, если адрес раздела начинается с «/». Объявления маршрутов (@page),
    // маршруты сервера (MapGet/MapPost) и сравнения текущего пути (StartsWith/EndsWith/Contains) — не
    // ссылки, их не считаем.
    private static bool LinksToRoot(string line) =>
        !line.Contains("@page ") && !line.Contains("MapGet(") && !line.Contains("MapPost(") && !line.Contains("MapPut(") && !line.Contains("MapDelete(")
        && !line.Contains("StartsWith(\"/") && !line.Contains("EndsWith(\"/") && !line.Contains("Contains(\"/")
        && Sections.Any(section => line.Contains($"\"/{section}\"") || line.Contains($"\"/{section}/") || line.Contains($"$\"/{section}"));

    private static readonly string[] Sections = ["tasks", "sprints", "capacity", "team", "gantt", "projects", "analytics", "settings", "my-work", "data", "unassigned", "login", "register", "workspaces"];

    // Только клиентские проекты: они и строят ссылки. В серверных адреса маршрутов абсолютны по природе.
    private static readonly string[] ClientProjects = ["Alva.Client.UI", "Alva.Client", "Alva.Client.Demo"];

    private static IEnumerable<string> SourceFiles()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Alva.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return ClientProjects
            .SelectMany(project => new[] { "*.razor", "*.cs" }.SelectMany(pattern =>
                System.IO.Directory.EnumerateFiles(Path.Combine(directory!.FullName, "src", project), pattern, SearchOption.AllDirectories)))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
    }

    // Ровно то, что делает браузер: относительный адрес резолвится от базового, а «/раздел» — от корня
    // хостинга. Пустой адрес — корень приложения, каким его и делает <base href>.
    private static void AssertInsideHostBase(IEnumerable<string?> hrefs, string what)
    {
        var links = hrefs.Where(href => href is not null && (href.Length == 0 || (!href.StartsWith('#') && !href.Contains("://")))).ToArray();
        Assert.NotEmpty(links);
        foreach (var link in links)
        {
            Assert.False(link!.StartsWith('/'), $"«{what}»: ссылка «{link}» начинается с «/» и ведёт в корень хостинга");
            Assert.StartsWith(Host.AbsoluteUri, new Uri(Host, link).AbsoluteUri);
        }
    }
}
