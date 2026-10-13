using Alva.Client;
using Alva.Client.Services;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

// Продовая оболочка: единственное отличие от demo-версии — реализация IApiService,
// которая ходит в API сервера (same-origin, cookie).
var builder = WebAssemblyHostBuilder.CreateDefault(args);
AlvaHost.Configure(builder);
builder.Services.AddScoped(_ =>
{
    var client = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
    client.DefaultRequestHeaders.Add("X-Alva-Request", "1");
    return client;
});
builder.Services.AddScoped<IApiService, ApiServiceHttp>();
builder.Services.AddLocalization();
builder.Services.AddScoped<CultureService>();
var host = builder.Build();
await host.Services.GetRequiredService<CultureService>().InitializeAsync();
await host.RunAsync();
