using Flare.Components;
using Microsoft.AspNetCore.Components;

namespace Alva.Client.Services;

/// <summary>
/// Drafts of open editors. A language change and leaving the page ask before losing edits
/// rather than unloading the application silently.
/// </summary>
public sealed class DraftGuard
{
    private readonly List<Func<bool>> _drafts = [];

    public bool HasDirty => _drafts.Any(d => d());

    /// <summary>Registers a dirty-draft check; the returned value unregisters it.</summary>
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

/// <summary>A draft snapshot for comparison: the models are plain classes without value equality.</summary>
public static class DraftSnapshot
{
    public static string Of(params object?[] values) => System.Text.Json.JsonSerializer.Serialize(values);
}

/// <summary>One prompt for all editors: leave with unsaved changes or stay.</summary>
public static class DraftPrompt
{
    public static async Task<bool> CanDiscardAsync(FlareConfirmDialogProvider? confirm, bool dirty)
    {
        if (!dirty || confirm is null) return true;
        var leave = await confirm.ConfirmAsync(AlvaStrings.Common_UnsavedChanges,
            AlvaStrings.Common_ThereAreUnsavedChangesLeaveAndLoseThem,
            AlvaStrings.Common_LeaveWithoutSaving, AlvaStrings.Common_Stay);
        return leave == true;
    }
}

/// <summary>Confirmation of an irreversible or bulk action, with the object name and the consequence.</summary>
public static class DestructivePrompt
{
    public static async Task<bool> ConfirmAsync(FlareConfirmDialogProvider? confirm, string title, string message, string action)
    {
        if (confirm is null) return true;
        return await confirm.ConfirmAsync(title, message, action, AlvaStrings.Common_Cancel) == true;
    }
}

/// <summary>Focus an element without crashing outside a browser: bUnit and disabled JS provide no ElementReference.</summary>
public static class SafeFocus
{
    public static async Task Async(ElementReference element)
    {
        try { await element.FocusAsync(); }
        catch (Exception e) when (e is InvalidOperationException or Microsoft.JSInterop.JSException or Microsoft.JSInterop.JSDisconnectedException) { }
    }
}
