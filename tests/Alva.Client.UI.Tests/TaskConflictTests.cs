using Alva.Client.Models;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>A version conflict is explained in words and does not leave the user saving over a stale version:
/// the server responds 409, the card says so and takes the fresh data instead of the stale draft.</summary>
public sealed class TaskConflictTests : UiTestBase
{
    private readonly TaskModel _task = new() { Id = Guid.NewGuid(), Title = "Релиз", Status = "Planned", Version = 1 };

    public TaskConflictTests()
    {
        Api.Answer($"tasks/{_task.Id}/history", _ => Array.Empty<HistoryDto>());
    }

    [Fact]
    public void A_stale_version_is_explained_and_the_card_takes_the_fresh_task()
    {
        var cut = Render<TaskDetails>(p => p.Add(x => x.Item, _task));
        Field(cut, "Остаток, часы").Change("5");
        Api.OnSend = (method, path, value) =>
            method == HttpMethod.Put && path.Contains($"tasks/{_task.Id}")
                ? throw new HttpRequestException("Conflict", null, System.Net.HttpStatusCode.Conflict)
                : null;

        Button(cut, "Сохранить").Click();

        cut.WaitForAssertion(() => Assert.Contains("изменил кто-то другой", cut.Markup));

        // The draft yields to the server data: otherwise the next save would again go out with the old version,
        // and the user would hit the same conflict.
        cut.WaitForAssertion(() => Assert.Equal("", Field(cut, "Остаток, часы").GetAttribute("value") ?? ""));
    }
}
