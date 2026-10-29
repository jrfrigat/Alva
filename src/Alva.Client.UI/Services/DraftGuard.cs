using Flare.Components;
using Microsoft.AspNetCore.Components;

namespace Alva.Client.Services;

/// <summary>
/// Черновики открытых редакторов. Смена языка и уход со страницы спрашивают, прежде чем
/// потерять правки, а не выгружают приложение молча.
/// </summary>
public sealed class DraftGuard
{
    private readonly List<Func<bool>> _drafts = [];

    public bool HasDirty => _drafts.Any(d => d());

    /// <summary>Регистрирует признак черновика; возвращённое значение снимает регистрацию.</summary>
    public IDisposable Track(Func<bool> dirty)
    {
        _drafts.Add(dirty);
        return new Subscription(this, dirty);
    }

    private sealed class Subscription(DraftGuard owner, Func<bool> dirty) : IDisposable
    {
        public void Dispose() => owner._drafts.Remove(dirty);
    }
}

/// <summary>Снимок черновика для сравнения: модели — обычные классы без значимого равенства.</summary>
public static class DraftSnapshot
{
    public static string Of(params object?[] values) => System.Text.Json.JsonSerializer.Serialize(values);
}

/// <summary>Один вопрос на все редакторы: уходить с несохранёнными правками или остаться.</summary>
public static class DraftPrompt
{
    public static async Task<bool> CanDiscardAsync(FlareConfirmDialogProvider? confirm, bool dirty)
    {
        if (!dirty || confirm is null) return true;
        var leave = await confirm.ConfirmAsync(Strings.T("Несохранённые изменения"),
            Strings.T("Есть несохранённые правки. Уйти и потерять их?"),
            Strings.T("Уйти без сохранения"), Strings.T("Остаться"));
        return leave == true;
    }
}

/// <summary>Подтверждение необратимого или массового действия с названием объекта и последствием.</summary>
public static class DestructivePrompt
{
    public static async Task<bool> ConfirmAsync(FlareConfirmDialogProvider? confirm, string title, string message, string action)
    {
        if (confirm is null) return true;
        return await confirm.ConfirmAsync(title, message, action, Strings.T("Отмена")) == true;
    }
}

/// <summary>Фокус на элемент без падения вне браузера: bUnit и отключённый JS не дают ElementReference.</summary>
public static class SafeFocus
{
    public static async Task Async(ElementReference element)
    {
        try { await element.FocusAsync(); }
        catch (Exception e) when (e is InvalidOperationException or Microsoft.JSInterop.JSException or Microsoft.JSInterop.JSDisconnectedException) { }
    }
}
