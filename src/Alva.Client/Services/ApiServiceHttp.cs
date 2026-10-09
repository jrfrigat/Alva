using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alva.Client.Models;
using Microsoft.JSInterop;

namespace Alva.Client.Services;

/// <summary>Продовая реализация: те же HTTP-вызовы к API сервера (same-origin, cookie).</summary>
public sealed class ApiServiceHttp(HttpClient http, IJSRuntime js) : IApiService
{
    public UserInfo? User { get; private set; }
    public SpaceInfo[] Workspaces { get; private set; } = [];
    public TeamInfo[] Teams { get; private set; } = [];
    public Guid? WorkspaceId { get; private set; }
    public Guid? TeamId { get; private set; }
    public string TeamName => Teams.FirstOrDefault(t => t.Id == TeamId)?.Name ?? "Выбрать команду";
    public bool Initialized { get; private set; }
    public event Action? Changed;
    public string TeamPath(string resource) => $"api/teams/{TeamId}/{resource}";
    public async Task Initialize()
    {
        if (Initialized) return;
        using var response = await http.GetAsync("api/auth/me");
        if (response.StatusCode == HttpStatusCode.Unauthorized) { Initialized = true; Changed?.Invoke(); return; }
        await Check(response);
        User = await response.Content.ReadFromJsonAsync<UserInfo>();
        await LoadWorkspaces();
        Initialized = true;
        Changed?.Invoke();
    }
    public async Task Authenticate(string endpoint, object input)
    {
        await Send(HttpMethod.Post, "api/auth/" + endpoint, input);
        Initialized = false;
        await Initialize();
    }
    public async Task Logout()
    {
        await Send(HttpMethod.Post, "api/auth/logout", new { });
        User = null; Workspaces = []; Teams = []; WorkspaceId = TeamId = null; Initialized = true;
        await js.InvokeVoidAsync("localStorage.removeItem", "alva-team");
        await js.InvokeVoidAsync("localStorage.removeItem", "alva-workspace");
        Changed?.Invoke();
    }
    public async Task LoadWorkspaces()
    {
        Workspaces = await Get<SpaceInfo[]>("api/workspaces");
        var stored = await js.InvokeAsync<string?>("localStorage.getItem", "alva-workspace");
        var id = Guid.TryParse(stored, out var parsed) && Workspaces.Any(x => x.Id == parsed) ? parsed : Workspaces.FirstOrDefault()?.Id;
        if (id.HasValue) await SelectWorkspace(id.Value);
    }
    public async Task SelectWorkspace(Guid id)
    {
        WorkspaceId = id;
        await js.InvokeVoidAsync("localStorage.setItem", "alva-workspace", id.ToString());
        Teams = await Get<TeamInfo[]>($"api/workspaces/{id}/teams");
        var stored = await js.InvokeAsync<string?>("localStorage.getItem", "alva-team");
        TeamId = Guid.TryParse(stored, out var parsed) && Teams.Any(x => x.Id == parsed) ? parsed : Teams.FirstOrDefault()?.Id;
        Changed?.Invoke();
    }
    public async Task SelectTeam(Guid id)
    {
        TeamId = id;
        await js.InvokeVoidAsync("localStorage.setItem", "alva-team", id.ToString());
        Changed?.Invoke();
    }
    public async Task<T> Get<T>(string path)
    {
        using var response = await http.GetAsync(path);
        await Check(response);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    public async Task Send(HttpMethod method, string path, object value)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(value) };
        using var response = await http.SendAsync(request);
        await Check(response);
    }
    public async Task<T> Save<T>(HttpMethod method, string path, object value)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(value) };
        using var response = await http.SendAsync(request);
        await Check(response);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    public async Task Import(string format, Stream stream)
    {
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        using var response = await http.PostAsync(TeamPath("data/import?format=" + format), content);
        await Check(response);
    }
    public async Task RefreshUser()
    {
        User = await Get<UserInfo>("api/auth/me");
        Changed?.Invoke();
    }
    private static async Task Check(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new InvalidOperationException("Сеанс завершён. Войдите в Alva.");
        var message = $"Запрос не выполнен ({(int)response.StatusCode}).";
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            foreach (var key in new[] { "detail", "title", "error" })
                if (document.RootElement.TryGetProperty(key, out var text) && text.ValueKind == JsonValueKind.String) { message = text.GetString()!; break; }
        }
        catch (JsonException) { }
        throw new InvalidOperationException(message);
    }
}
