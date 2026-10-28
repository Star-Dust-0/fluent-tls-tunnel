using Renci.SshNet;
namespace FluentTlsTunnel.Core;
public sealed class Tunnel : IAsyncDisposable
{
    TlsBridge? bridge;
    SshClient? ssh;
    ForwardedPortDynamic? socks;
    public bool Connected => ssh?.IsConnected == true && socks?.IsStarted == true;
    public async Task ConnectAsync(Profile p, string password, Func<string,string,bool,Task<bool>> confirmKey, Action<string> log, CancellationToken ct)
    {
        p.Validate();
        try
        {
            bridge = new(); bridge.Start(p,log);
            ssh = new SshClient("127.0.0.1",bridge.Port,p.Username,password);
            ssh.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);
            ssh.KeepAliveInterval = TimeSpan.FromSeconds(20);
            ssh.HostKeyReceived += (_,e) =>
            {
                string key = "SHA256:" + e.FingerPrintSHA256;
                p.HostKeys.TryGetValue(p.Endpoint,out var known);
                e.CanTrust = known == key || confirmKey(p.Endpoint,key,known != null).GetAwaiter().GetResult();
                if (e.CanTrust && known != key) { p.HostKeys[p.Endpoint] = key; p.Save(); }
            };
            ssh.ErrorOccurred += (_,e) => log("SSH: " + e.Exception.Message);
            await ssh.ConnectAsync(ct);
            socks = new ForwardedPortDynamic("127.0.0.1",(uint)p.SocksPort);
            socks.Exception += (_,e) => log("SOCKS: " + e.Exception.Message);
            ssh.AddForwardedPort(socks); socks.Start(); log("connected");
        }
        catch { await DisposeAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        try { socks?.Stop(); } catch { }
        socks?.Dispose(); socks = null;
        try { ssh?.Disconnect(); } catch { }
        ssh?.Dispose(); ssh = null;
        if (bridge != null) { await bridge.DisposeAsync(); bridge = null; }
    }
}