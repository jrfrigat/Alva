using System.Net;
using System.Net.Sockets;
using Alva.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Alva.Data.Tests;

public sealed class TrustedProxyForwardingTests
{
    private static readonly IPAddress OriginalProxy = IPAddress.Parse("172.18.0.8");
    private static readonly IPAddress MovedProxy = IPAddress.Parse("172.18.0.6");
    private static readonly IPAddress Browser = IPAddress.Parse("192.0.2.14");

    [Fact]
    public async Task A_proxy_hostname_is_refreshed_and_its_old_address_loses_trust()
    {
        var clock = new TestClock();
        var address = OriginalProxy;
        var lookups = 0;
        var forwarding = Create("nginx-proxy", host =>
        {
            Assert.Equal("nginx-proxy", host);
            lookups++;
            return Task.FromResult(new[] { address });
        }, clock);

        await AssertForwarding(forwarding, OriginalProxy.MapToIPv6(), trusted: true);
        await AssertForwarding(forwarding, MovedProxy, trusted: false);
        Assert.Equal(1, lookups);

        address = MovedProxy;
        clock.Advance();
        await AssertForwarding(forwarding, OriginalProxy, trusted: false);
        await AssertForwarding(forwarding, MovedProxy, trusted: true);
        Assert.Equal(2, lookups);
    }

    [Fact]
    public async Task A_failed_lookup_drops_stale_trust_and_can_recover()
    {
        var clock = new TestClock();
        IPAddress? address = OriginalProxy;
        var forwarding = Create("nginx-proxy", _ => address is { } proxy
            ? Task.FromResult(new[] { proxy })
            : Task.FromException<IPAddress[]>(new SocketException((int)SocketError.HostNotFound)), clock);

        await AssertForwarding(forwarding, OriginalProxy, trusted: true);
        address = null;
        clock.Advance();
        await AssertForwarding(forwarding, OriginalProxy, trusted: false);
        await AssertForwarding(forwarding, MovedProxy, trusted: false);

        address = MovedProxy;
        clock.Advance();
        await AssertForwarding(forwarding, MovedProxy, trusted: true);
    }

    [Fact]
    public async Task An_explicit_proxy_address_works_without_DNS()
    {
        var forwarding = Create(OriginalProxy.ToString(), _ => throw new InvalidOperationException());

        await AssertForwarding(forwarding, OriginalProxy, trusted: true);
        await AssertForwarding(forwarding, MovedProxy, trusted: false);
    }

    private static TrustedProxyForwarding Create(string proxy, Func<string, Task<IPAddress[]>> resolve, TimeProvider? clock = null) =>
        new(_ => Task.CompletedTask, NullLoggerFactory.Instance, proxy, resolve, clock ?? TimeProvider.System);

    private static async Task AssertForwarding(TrustedProxyForwarding forwarding, IPAddress peer, bool trusted)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = peer;
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-Proto"] = "http, https";
        context.Request.Headers["X-Forwarded-For"] = $"192.0.2.99, {Browser}";

        await forwarding.InvokeAsync(context);

        Assert.Equal(trusted ? "https" : "http", context.Request.Scheme);
        Assert.Equal(trusted ? Browser : peer, context.Connection.RemoteIpAddress);
    }

    private sealed class TestClock : TimeProvider
    {
        private long _timestamp;
        public override long GetTimestamp() => _timestamp;
        public void Advance() => _timestamp += 6 * TimestampFrequency;
    }
}
