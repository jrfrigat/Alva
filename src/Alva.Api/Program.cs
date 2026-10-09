using Alva.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using Alva.Api;
using Alva.Domain;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using System.Net;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<AlvaDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Alva") ?? "Host=localhost;Database=alva;Username=alva"));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<PasswordHasher<UserAccount>>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "Alva.Session";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromHours(12);
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
var protection = builder.Services.AddDataProtection().SetApplicationName("Alva");
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath) protection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
var proxyEnabled = builder.Configuration.GetValue("ReverseProxy:Enabled", false);
if (proxyEnabled)
{
    if (!IPAddress.TryParse(builder.Configuration["ReverseProxy:KnownProxy"], out var knownProxy)) throw new InvalidOperationException("ReverseProxy:KnownProxy must specify the trusted proxy IP.");
    builder.Services.Configure<ForwardedHeadersOptions>(o => { o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto; o.KnownProxies.Add(knownProxy); });
}
builder.Services.AddRateLimiter(o => { o.RejectionStatusCode = 429; o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })); });
var app = builder.Build();
if (proxyEnabled) app.UseForwardedHeaders();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    try
    {
        if (context.Request.Path.StartsWithSegments("/api") && context.Request.Method is "POST" or "PUT" or "PATCH" or "DELETE")
        {
            if (context.Request.Headers["X-Alva-Request"] != "1") throw new ApiException(403, "Отсутствует заголовок защиты запроса.");
            if (context.Request.Headers.Origin is { Count: > 0 } origins && (!Uri.TryCreate(origins.ToString(), UriKind.Absolute, out var origin) || origin.GetLeftPart(UriPartial.Authority) != $"{context.Request.Scheme}://{context.Request.Host}")) throw new ApiException(403, "Недопустимый источник запроса.");
        }
        await next();
    }
    catch (ApiException ex) { await Results.Problem(statusCode: ex.Status, title: ex.Message).ExecuteAsync(context); }
    catch (DbUpdateConcurrencyException) { await Results.Problem(statusCode: 409, title: "Данные изменились. Обновите страницу и повторите действие.").ExecuteAsync(context); }
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapAuth();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (AlvaDbContext db, CancellationToken ct) => await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
if (builder.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AlvaDbContext>().Database.MigrateAsync();
}
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");
app.Run();

public partial class Program;
