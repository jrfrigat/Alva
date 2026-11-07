using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Alva.Api;

internal sealed class TrustedProxyForwarding
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    private readonly RequestDelegate _next;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Func<string, Task<IPAddress[]>> _resolve;
    private readonly TimeProvider _clock;
    private readonly ForwardedHeadersMiddleware? _fixedProxy;
    private readonly string? _hostname;
    private readonly object _gate = new();
    private Task<ForwardedHeadersMiddleware?>? _lookup;
    private long _lookupStarted;

    public TrustedProxyForwarding(RequestDelegate next, ILoggerFactory loggerFactory, string? proxy,
        Func<string, Task<IPAddress[]>> resolve, TimeProvider clock)
    {
        _next = next;
        _loggerFactory = loggerFactory;
        _resolve = resolve;
        _clock = clock;
        if (IPAddress.TryParse(proxy, out var address))
            _fixedProxy = CreateForwarder([address]);
        else if (Uri.CheckHostName(proxy) == UriHostNameType.Dns)
            _hostname = proxy;
        else
            throw new InvalidOperationException("ReverseProxy:KnownProxy must specify a trusted proxy hostname or IP address.");
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (_fixedProxy is not null)
            return _fixedProxy.Invoke(context);
        if (!context.Request.Headers.ContainsKey("X-Forwarded-Proto") &&
            !context.Request.Headers.ContainsKey("X-Forwarded-For"))
            return _next(context);
        return ForwardFromHostnameAsync(context);
    }

    private async Task ForwardFromHostnameAsync(HttpContext context)
    {
        Task<ForwardedHeadersMiddleware?> lookup;
        lock (_gate)
        {
            if (_lookup is null || (_lookup.IsCompleted && _clock.GetElapsedTime(_lookupStarted) >= RefreshInterval))
            {
                _lookupStarted = _clock.GetTimestamp();
                _lookup = ResolveForwarderAsync(_hostname!);
            }
            lookup = _lookup;
        }
        var forwarder = await lookup;
        await (forwarder is null ? _next(context) : forwarder.Invoke(context));
    }

    private async Task<ForwardedHeadersMiddleware?> ResolveForwarderAsync(string hostname)
    {
        try
        {
            var addresses = await _resolve(hostname);
            return addresses.Length == 0 ? null : CreateForwarder(addresses);
        }
        catch (SocketException error)
        {
            _loggerFactory.CreateLogger<TrustedProxyForwarding>()
                .LogWarning(error, "Could not resolve trusted proxy {ProxyHostname}; ignoring forwarded headers.", hostname);
            return null;
        }
    }

    private ForwardedHeadersMiddleware CreateForwarder(IPAddress[] addresses)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1
        };
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var address in addresses)
            options.KnownProxies.Add(address);
        return new ForwardedHeadersMiddleware(_next, _loggerFactory, Options.Create(options));
    }
}
