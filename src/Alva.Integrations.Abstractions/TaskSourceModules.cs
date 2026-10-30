using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Alva.Integrations;

/// <summary>Подключённые модули по идентификатору. Два модуля с одним Id — ошибка конфигурации, она видна при старте.</summary>
public sealed class TaskSourceModules
{
    private readonly Dictionary<string, ITaskSourceModule> _modules = new(StringComparer.OrdinalIgnoreCase);

    public TaskSourceModules(IEnumerable<ITaskSourceModule> modules)
    {
        foreach (var module in modules)
            if (!_modules.TryAdd(module.Id, module))
                throw new InvalidOperationException($"Модуль интеграции «{module.Id}» зарегистрирован дважды.");
    }

    public IReadOnlyCollection<ITaskSourceModule> All => _modules.Values;

    public ITaskSourceModule? Find(string id) => _modules.GetValueOrDefault(id);
}

public static class TaskSourceModuleRegistration
{
    /// <summary>Подключает модуль: он становится доступен в списке модулей и для подключений.</summary>
    public static IServiceCollection AddTaskSourceModule<TModule>(this IServiceCollection services) where TModule : class, ITaskSourceModule
    {
        services.AddSingleton<ITaskSourceModule, TModule>();
        services.TryAddSingleton<TaskSourceModules>();
        return services;
    }

    /// <summary>Подключает уже созданный модуль.</summary>
    public static IServiceCollection AddTaskSourceModule(this IServiceCollection services, ITaskSourceModule module)
    {
        services.AddSingleton(module);
        services.TryAddSingleton<TaskSourceModules>();
        return services;
    }
}
