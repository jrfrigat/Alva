using Alva.Integrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Data.Tests;

/// <summary>Контракт модулей: реестр по Id и общая проверка настроек по схеме модуля.</summary>
public sealed class IntegrationAbstractionsTests
{
    private sealed class Module(string id = "demo") : ITaskSourceModule
    {
        public string Id => id;
        public string Name => "Демо";
        public string Description => "";
        public IntegrationCapabilities Capabilities => IntegrationCapabilities.Tasks;
        public IReadOnlyList<IntegrationSetting> Settings { get; } =
        [
            new("url", "Адрес", IntegrationSettingKind.Url, Required: true),
            new("token", "Токен", IntegrationSettingKind.Secret, Required: true),
            new("hours", "Часов в дне", IntegrationSettingKind.Number, Default: "8"),
            new("links", "Связи", IntegrationSettingKind.Flag),
            new("kind", "Вид", IntegrationSettingKind.Choice, Choices: [new("a", "А"), new("b", "Б")])
        ];
        public Task<ITaskSource> ConnectAsync(IntegrationConnection connection, CancellationToken ct) => throw new NotSupportedException();
    }

    private static IntegrationConnection Connection(Dictionary<string, string> settings, Dictionary<string, string>? secrets = null) =>
        new(Guid.NewGuid(), "Тест", settings, secrets ?? []);

    [Fact]
    public void Modules_are_found_by_id_in_any_case_and_registered_through_di()
    {
        var provider = new ServiceCollection().AddTaskSourceModule(new Module("yandex-tracker")).BuildServiceProvider();

        var modules = provider.GetRequiredService<TaskSourceModules>();

        Assert.NotNull(modules.Find("Yandex-Tracker"));
        Assert.Null(modules.Find("jira"));
    }

    [Fact]
    public void A_module_id_registered_twice_is_a_startup_error() =>
        Assert.Throws<InvalidOperationException>(() => new TaskSourceModules([new Module(), new Module()]));

    [Fact]
    public void Valid_settings_pass_and_defaults_fill_the_gaps()
    {
        var module = new Module();
        var connection = Connection(new() { ["url"] = "https://tracker.example", ["links"] = "true", ["kind"] = "b" }, new() { ["token"] = "t" });

        Assert.Empty(IntegrationSettings.Validate(module, connection));
        Assert.Equal("8", connection.Get(module, "hours"));
        Assert.Equal("t", connection.Value("token"));
    }

    [Fact]
    public void Each_wrong_setting_is_named()
    {
        var connection = Connection(new() { ["url"] = "ftp://x", ["hours"] = "много", ["links"] = "может", ["kind"] = "c" });

        var errors = IntegrationSettings.Validate(new Module(), connection).ToDictionary(e => e.Key, e => e.Message);

        Assert.Equal(["url", "token", "hours", "links", "kind"], errors.Keys);
        Assert.Contains("Токен", errors["token"]);
        Assert.Contains("http(s)", errors["url"]);
    }
}
