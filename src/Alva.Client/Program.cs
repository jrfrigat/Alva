using Alva.Client;
using Flare.Extensions;
using Flare.Theme.MaterialDesign3Expressive;
using Flare.Abstractions.Tokens;
using Flare.Theme.MaterialDesign3.Tokens;
using Alva.Client.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = new System.Globalization.CultureInfo("ru-RU");
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = new System.Globalization.CultureInfo("ru-RU");
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddFlare(options =>
{
    options.DefaultTheme = new MaterialDesign3ExpressiveTheme();
    options.DefaultPalette = PaletteFactory.Brand("alva-sapphire", "Alva Sapphire", MaterialDesign3Tokens.LightColors, MaterialDesign3Tokens.DarkColors, "#1D4ED8", "Alva");
});
builder.Services.AddScoped(_ =>
{
    var client = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
    client.DefaultRequestHeaders.Add("X-Alva-Request", "1");
    return client;
});
builder.Services.AddScoped<IApiService, ApiServiceHttp>();
builder.Services.AddScoped<AlvaSession>();
await builder.Build().RunAsync();
