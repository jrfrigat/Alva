using System.Globalization;

namespace Alva.Integrations;

/// <summary>The kind of a setting field: the form field and validation depend on it.</summary>
public enum IntegrationSettingKind { Text, Secret, Url, Number, Flag, Choice }

/// <summary>A single module setting, as it is shown and validated.</summary>
/// <param name="Key">The key in the connection settings dictionary.</param>
/// <param name="Choices">Options for <see cref="IntegrationSettingKind.Choice"/>: value and label.</param>
public sealed record IntegrationSetting(
    string Key,
    string Label,
    IntegrationSettingKind Kind = IntegrationSettingKind.Text,
    bool Required = false,
    string? Default = null,
    string? Description = null,
    IReadOnlyList<IntegrationChoice>? Choices = null);

public sealed record IntegrationChoice(string Value, string Label);

/// <summary>Shared validation of settings against the module schema: the core and the module describe an error the same way.</summary>
public static class IntegrationSettings
{
    /// <summary>Errors by key; an empty list means the settings are valid.</summary>
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

    /// <summary>The setting value, with the schema default substituted when absent.</summary>
    public static string? Get(this IntegrationConnection connection, ITaskSourceModule module, string key) =>
        connection.Value(key) ?? module.Settings.FirstOrDefault(s => s.Key == key)?.Default;
}
