using Alva.Client;
using Alva.Client.Services;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

// The demo shell: the only difference from the production version is the IApiService implementation,
// which lives in the browser on localStorage and does not access the network.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
AlvaHost.Configure(builder);
builder.Services.AddScoped<IApiService, ApiServiceLocalStorage>();
builder.Services.AddLocalization();
builder.Services.AddScoped<CultureService>();
builder.Services.AddScoped<DraftGuard>();
var host = builder.Build();
await host.Services.GetRequiredService<CultureService>().InitializeAsync();
await host.RunAsync();
