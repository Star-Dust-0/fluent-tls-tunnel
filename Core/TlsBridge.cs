using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
namespace FluentTlsTunnel.Core;
// A private, ephemeral loopback listener carries SSH bytes into an authenticated TLS stream.
public sealed class TlsBridge : IAsyncDisposable
{
    readonly TcpListener listener = new(IPAddress.Loopback,0);
    readonly CancellationTokenSource stop = new();
    Task? pump;
    TcpClient? local, remote;
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public void Start(Profile profile, Action<string> log)
    {
        listener.Start(1);
        pump = PumpAsync(profile,log);
    }
    async Task PumpAsync(Profile p, Action<string> log)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            local = await listener.AcceptTcpClientAsync(deadline.Token);
            listener.Stop(); // Only the SSH connection can use this bridge.
            remote = new TcpClient { NoDelay = true };
            await remote.ConnectAsync(p.Host,p.Port,deadline.Token);
            using var tls = new SslStream(remote.GetStream(),false,(_,_,_,errors) => !p.VerifyTls || errors == SslPolicyErrors.None);
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = p.Sni,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            },deadline.Token);
            log("tls-ready");
            using var pair = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            var upstream = local.GetStream().CopyToAsync(tls,pair.Token);
            var downstream = tls.CopyToAsync(local.GetStream(),pair.Token);
            await Task.WhenAny(upstream,downstream);
            pair.Cancel();
            try { await Task.WhenAll(upstream,downstream); } catch (OperationCanceledException) { }
        }
        catch (Exception e) when (!stop.IsCancellationRequested) { log("TLS: " + e.Message); }
        finally { local?.Dispose(); remote?.Dispose(); }
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); listener.Stop(); local?.Dispose(); remote?.Dispose();
        if (pump != null) try { await pump; } catch { }
        stop.Dispose();
    }
}