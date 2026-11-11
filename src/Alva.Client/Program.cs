using Alva.Client;
using Alva.Client.Services;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

// The production shell: the only difference from the demo version is the IApiService implementation,
// which calls the server API (same-origin, cookie).
var builder = WebAssemblyHostBuilder.CreateDefault(args);
AlvaHost.Configure(builder);
builder.Services.AddScoped(sp =>
{
    var client = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
    client.DefaultRequestHeaders.Add("X-Alva-Request", "1");
    // The hub connection presents the same tab id, so the server can leave this tab's own writes out
    // of the realtime broadcast.
    client.DefaultRequestHeaders.Add("X-Alva-Client", sp.GetRequiredService<ClientIdentity>().Id);
    return client;
});
builder.Services.AddScoped<IApiService, ApiServiceHttp>();
builder.Services.AddScoped<IRealtimeService, RealtimeService>();
builder.Services.AddLocalization();
builder.Services.AddScoped<CultureService>();
builder.Services.AddScoped<DraftGuard>();
var host = builder.Build();
await host.Services.GetRequiredService<CultureService>().InitializeAsync();
await host.RunAsync();
