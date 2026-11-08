using Alva.Client.Layout;
using Alva.Client.Models;
using Alva.Client.Pages;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>The demo is published from a host subfolder (https://jrfrigat.github.io/Alva/), so URLs inside
/// the application are relative: the page base address resolves them, and the current path is read from
/// the same base address. A path with a leading "/" loses the subfolder and leads to the hosting root
/// instead of the application page.</summary>
public sealed class BasePathTests : UiTestBase
{
    // This is what the demo host looks like: both the page URL and the base address live in the /Alva/ subfolder.
    private static readonly Uri Host = new("https://jrfrigat.github.io/Alva/");

    private BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

    // A navigation manager with an arbitrary address: stands in for what <base href> provides in the application.
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

    // The class of defect that render tests cannot see by design: in tests the base address is the root,
    // so "/tasks" and "tasks" produce the same Uri, while on a host in a subfolder they are different pages. The guard
    // reads the application sources and looks for a section URL with a leading "/" in a link or a navigation.
    [Fact]
    public void No_source_links_to_a_section_from_the_origin_root()
    {
        // The guard must bite: the rule catches a link and a navigation, but not a route declaration or a
        // current path comparison - otherwise it would silently miss exactly what it was written for.
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

    // A link violates the rule if the section URL starts with "/". Route declarations (@page),
    // server routes (MapGet/MapPost) and current path comparisons (StartsWith/EndsWith/Contains) are not
    // links, so they are not counted.
    private static bool LinksToRoot(string line) =>
        !line.Contains("@page ") && !line.Contains("MapGet(") && !line.Contains("MapPost(") && !line.Contains("MapPut(") && !line.Contains("MapDelete(")
        && !line.Contains("StartsWith(\"/") && !line.Contains("EndsWith(\"/") && !line.Contains("Contains(\"/")
        && Sections.Any(section => line.Contains($"\"/{section}\"") || line.Contains($"\"/{section}/") || line.Contains($"$\"/{section}"));

    private static readonly string[] Sections = ["tasks", "sprints", "capacity", "team", "gantt", "projects", "analytics", "settings", "my-work", "data", "unassigned", "login", "register", "workspaces"];

    // Client projects only: they build the links. Server route URLs are absolute by nature.
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

    // Exactly what the browser does: a relative URL resolves against the base, and "/section" against the
    // hosting root. An empty URL is the application root, as <base href> makes it.
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
