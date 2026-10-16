using Alva.Api;
using Alva.Contracts;
using System.Net.Http.Json;
using Xunit;

namespace Alva.Data.Tests;

/// <summary>Справочник участников работает и на локальной SQLite, где нет SELECT … FOR UPDATE.</summary>
public sealed class SqliteActorTests
{
    [Fact]
    public async Task A_person_can_be_created_changed_and_archived_on_sqlite()
    {
        var server = await ApiServer.Create(app => app.MapActors());
        await using var _ = server;

        var created = await server.Client.PostAsJsonAsync(server.Team("actors"), new ActorRequest("Анна", "Person"));
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var actor = (await created.Content.ReadFromJsonAsync<ActorDto>())!;

        var changed = await server.Client.PutAsJsonAsync(server.Team($"actors/{actor.Id}"), new ActorRequest("Анна Ковалёва", "Person"));
        Assert.True(changed.IsSuccessStatusCode, await changed.Content.ReadAsStringAsync());

        var archived = await server.Client.DeleteAsync(server.Team($"actors/{actor.Id}"));
        Assert.True(archived.IsSuccessStatusCode, await archived.Content.ReadAsStringAsync());
    }
}
