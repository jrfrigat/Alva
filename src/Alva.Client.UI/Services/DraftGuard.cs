using Flare.Components;

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
