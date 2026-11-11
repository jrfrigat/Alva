namespace Alva.Client.Services;

/// <summary>
/// Identifies this tab in realtime: every write carries it in X-Alva-Client and the hub connection
/// presents it on subscribe, so the server can leave this tab's own changes out of the broadcast.
/// </summary>
public sealed class ClientIdentity
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
}
