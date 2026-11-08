using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Alva.Integrations;

/// <summary>Registered modules by identifier. Two modules with the same Id are a configuration error, visible at startup.</summary>
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
    /// <summary>Registers a module: it becomes available in the module list and for connections.</summary>
    public static IServiceCollection AddTaskSourceModule<TModule>(this IServiceCollection services) where TModule : class, ITaskSourceModule
    {
        services.AddSingleton<ITaskSourceModule, TModule>();
        services.TryAddSingleton<TaskSourceModules>();
        return services;
    }

    /// <summary>Registers an already created module.</summary>
    public static IServiceCollection AddTaskSourceModule(this IServiceCollection services, ITaskSourceModule module)
    {
        services.AddSingleton(module);
        services.TryAddSingleton<TaskSourceModules>();
        return services;
    }
}
