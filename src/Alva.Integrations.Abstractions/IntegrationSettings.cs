using System.Globalization;

namespace Alva.Integrations;

/// <summary>Вид поля настройки: от него зависят поле формы и проверка.</summary>
public enum IntegrationSettingKind { Text, Secret, Url, Number, Flag, Choice }

/// <summary>Одна настройка модуля, как её показать и проверить.</summary>
/// <param name="Key">Ключ в словаре настроек подключения.</param>
/// <param name="Choices">Варианты для <see cref="IntegrationSettingKind.Choice"/>: значение и подпись.</param>
public sealed record IntegrationSetting(
    string Key,
    string Label,
    IntegrationSettingKind Kind = IntegrationSettingKind.Text,
    bool Required = false,
    string? Default = null,
    string? Description = null,
    IReadOnlyList<IntegrationChoice>? Choices = null);

public sealed record IntegrationChoice(string Value, string Label);

/// <summary>Общая проверка настроек по схеме модуля: ядро и модуль называют ошибку одинаково.</summary>
public static class IntegrationSettings
{
    /// <summary>Ошибки по ключам; пустой список — настройки годятся.</summary>
    public static IReadOnlyList<(string Key, string Message)> Validate(ITaskSourceModule module, IntegrationConnection connection)
    {
        var errors = new List<(string, string)>();
        foreach (var setting in module.Settings)
        {
            var value = connection.Value(setting.Key);
            if (value is null)
            {
                if (setting.Required && setting.Default is null) errors.Add((setting.Key, $"Заполните поле «{setting.Label}»."));
                continue;
            }
            var problem = setting.Kind switch
            {
                IntegrationSettingKind.Url when !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
                    => $"В поле «{setting.Label}» нужен адрес http(s).",
                IntegrationSettingKind.Number when !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _)
                    => $"В поле «{setting.Label}» нужно число.",
                IntegrationSettingKind.Flag when !bool.TryParse(value, out _)
                    => $"В поле «{setting.Label}» нужно да или нет.",
                IntegrationSettingKind.Choice when setting.Choices is { } choices && choices.All(c => c.Value != value)
                    => $"В поле «{setting.Label}» выберите один из вариантов.",
                _ => null
            };
            if (problem is not null) errors.Add((setting.Key, problem));
        }
        return errors;
    }

    /// <summary>Значение настройки с подстановкой значения по умолчанию из схемы.</summary>
    public static string? Get(this IntegrationConnection connection, ITaskSourceModule module, string key) =>
        connection.Value(key) ?? module.Settings.FirstOrDefault(s => s.Key == key)?.Default;
}
