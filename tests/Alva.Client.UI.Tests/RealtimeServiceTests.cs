using Alva.Client.Services;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>The demo has no realtime, and every tab names itself for the server's echo filter.</summary>
public sealed class RealtimeServiceTests
{
    [Fact]
    public async Task The_demo_realtime_does_nothing()
    {
        await using var service = new NullRealtimeService();

        await service.EnsureStarted();
        Assert.False(service.Connected);

        await service.Stop();
    }

    [Fact]
    public void Each_tab_has_its_own_client_id()
    {
        var one = new ClientIdentity();
        var two = new ClientIdentity();

        Assert.False(string.IsNullOrWhiteSpace(one.Id));
        Assert.NotEqual(one.Id, two.Id);
    }
}
