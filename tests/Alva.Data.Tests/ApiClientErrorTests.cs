using System.Net;
using System.Text;
using Alva.Client.Services;
using Xunit;

namespace Alva.Data.Tests;

/// <summary>HTTP-клиент отличает отказ, объяснённый сервером, от ответа без объяснения.</summary>
public sealed class ApiClientErrorTests
{
    [Fact]
    public async Task A_problem_with_a_title_becomes_a_server_refusal_with_its_words()
    {
        var api = Client(HttpStatusCode.BadRequest, """{"title":"Сначала переместите или архивируйте подзадачи.","status":400}""");

        var error = await Assert.ThrowsAsync<ServerRefusalException>(() => api.Send(HttpMethod.Delete, "api/tasks/1", new { }));

        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Equal("Сначала переместите или архивируйте подзадачи.", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>Bad gateway</html>")]
    [InlineData("""{"title":"  "}""")]
    public async Task A_response_without_an_explanation_stays_a_plain_failure(string body)
    {
        var api = Client(HttpStatusCode.BadGateway, body);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => api.Send(HttpMethod.Post, "api/tasks", new { }));

        Assert.IsNotType<ServerRefusalException>(error);
        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
    }

    private static ApiServiceHttp Client(HttpStatusCode status, string body) =>
        new(new HttpClient(new Answer(status, body)) { BaseAddress = new Uri("http://alva.test/") }, null!);

    private sealed class Answer(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/problem+json") });
    }
}
