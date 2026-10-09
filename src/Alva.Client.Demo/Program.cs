using Alva.Client;
using Alva.Client.Services;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

// Demo-оболочка: единственное отличие от прод-версии — реализация IApiService,
// которая живёт в браузере на localStorage и не обращается к сети.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
AlvaHost.Configure(builder);
builder.Services.AddScoped<IApiService, ApiServiceLocalStorage>();
await builder.Build().RunAsync();
