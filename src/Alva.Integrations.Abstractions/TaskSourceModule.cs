namespace Alva.Integrations;

/// <summary>
/// Модуль интеграции: откуда берутся задачи (Яндекс Трекер, Jira, …). Модуль описывает свои настройки, а ядро хранит их
/// как есть и не знает их смысла — так новый модуль не требует правок в ядре.
/// </summary>
public interface ITaskSourceModule
{
    /// <summary>Стабильный идентификатор: хранится в подключениях, менять его нельзя.</summary>
    string Id { get; }
    string Name { get; }
    string Description { get; }
    IReadOnlyList<IntegrationSetting> Settings { get; }
    IntegrationCapabilities Capabilities { get; }

    /// <summary>Источник задач для конкретного подключения. Неверные настройки — <see cref="IntegrationException"/>.</summary>
    Task<ITaskSource> ConnectAsync(IntegrationConnection connection, CancellationToken ct);
}

/// <summary>Задачи одного подключения.</summary>
public interface ITaskSource
{
    /// <summary>Проверка подключения: доступ есть и под какой учётной записью.</summary>
    Task<IntegrationCheck> CheckAsync(CancellationToken ct);

    /// <summary>
    /// Задачи, изменённые после <paramref name="changedSince"/>; null — все задачи подключения. Источник может вернуть и
    /// чуть больше (например, с точностью до дня): ядро обновляет задачи идемпотентно.
    /// </summary>
    IAsyncEnumerable<ExternalTask> ReadTasksAsync(DateTimeOffset? changedSince, CancellationToken ct);
}

/// <summary>Что модуль умеет приносить.</summary>
[Flags]
public enum IntegrationCapabilities
{
    None = 0,
    Tasks = 1,
    Links = 2,
    Hierarchy = 4,
    Estimates = 8,
    Assignees = 16,
    /// <summary>Умеет отдавать только изменённое с прошлого раза.</summary>
    Incremental = 32
}

/// <summary>Подключение, как его хранит ядро: настройки по ключам схемы и отдельно секреты.</summary>
public sealed record IntegrationConnection(Guid Id, string Name, IReadOnlyDictionary<string, string> Settings, IReadOnlyDictionary<string, string> Secrets)
{
    /// <summary>Значение настройки или секрета; пустое — null.</summary>
    public string? Value(string key) =>
        (Settings.TryGetValue(key, out var value) || Secrets.TryGetValue(key, out value)) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}

/// <summary>Итог проверки подключения.</summary>
/// <param name="Account">Под кем работает подключение — чтобы человек увидел, тот ли это токен.</param>
public sealed record IntegrationCheck(bool Ok, string Message, string? Account = null);

/// <summary>Ошибка модуля с текстом для человека: ядро показывает её как есть.</summary>
public sealed class IntegrationException(string message, Exception? inner = null) : Exception(message, inner);
