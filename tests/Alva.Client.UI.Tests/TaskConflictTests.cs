using Alva.Client.Models;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Конфликт версий объясняется словами и не оставляет человека сохранять поверх устаревшей версии:
/// сервер отвечает 409, карточка говорит об этом и принимает свежие данные вместо несвежего черновика.</summary>
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
        Field(cut, "Название").Change("Релиз — моя правка");
        Api.OnSend = (method, path, value) =>
            method == HttpMethod.Put && path.Contains($"tasks/{_task.Id}")
                ? throw new HttpRequestException("Conflict", null, System.Net.HttpStatusCode.Conflict)
                : null;

        Button(cut, "Сохранить").Click();

        cut.WaitForAssertion(() => Assert.Contains("изменил кто-то другой", cut.Markup));

        // Черновик уступает данным сервера: иначе следующее сохранение снова ушло бы со старой версией,
        // и человек бился бы в тот же конфликт.
        cut.WaitForAssertion(() => Assert.Equal("Релиз", Field(cut, "Название").GetAttribute("value")));
    }
}
