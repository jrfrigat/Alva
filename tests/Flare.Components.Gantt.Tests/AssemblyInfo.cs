using System.Globalization;
using System.Runtime.CompilerServices;
using Xunit;

// These tests assert Russian Gantt captions. Pin the culture so the result does not depend on
// the machine: Windows developers use ru-RU, while CI runners start with en-US.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Flare.Components.Gantt.Tests;

internal static class TestCulture
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var russian = CultureInfo.GetCultureInfo("ru-RU");
        CultureInfo.CurrentCulture = russian;
        CultureInfo.CurrentUICulture = russian;
        CultureInfo.DefaultThreadCurrentCulture = russian;
        CultureInfo.DefaultThreadCurrentUICulture = russian;
    }
}
