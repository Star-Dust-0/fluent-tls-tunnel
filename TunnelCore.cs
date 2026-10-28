namespace FluentTlsTunnel.Core {
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
// A private, ephemeral loopback listener carries SSH bytes into an authenticated TLS stream.
public sealed class TlsBridge : IAsyncDisposable
{
    readonly TcpListener listener = new(IPAddress.Loopback,0);
    readonly CancellationTokenSource stop = new();
    Task? pump;
    TcpClient? local, remote;
    public static IPAddress NormalizeAddress(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    public string? RemoteAddress => remote?.Client.RemoteEndPoint is IPEndPoint endpoint ? NormalizeAddress(endpoint.Address).ToString() : null;
    public string? LocalAddress => remote?.Client.LocalEndPoint is IPEndPoint endpoint ? NormalizeAddress(endpoint.Address).ToString() : null;
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
#pragma warning disable SYSLIB0039
            var attempts = new[] { SslProtocols.Tls12 | SslProtocols.Tls13, SslProtocols.Tls12, SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12 };
            SslStream? tls = null;
            for (int i = 0; i < attempts.Length && tls == null; i++)
            {
                remote?.Dispose();
                remote = new TcpClient { NoDelay = true };
                await remote.ConnectAsync(p.Host,p.Port,deadline.Token);
                var candidate = new SslStream(remote.GetStream(),false,(_,_,_,errors) => !p.VerifyTls || errors == SslPolicyErrors.None);
                try
                {
                    await candidate.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = p.Sni, EnabledSslProtocols = attempts[i] },deadline.Token);
                    tls = candidate;
                    log("TLS: connected with " + candidate.SslProtocol + ", " + candidate.NegotiatedCipherSuite);
                }
                catch (Exception e) when (i < attempts.Length - 1 && !stop.IsCancellationRequested && !deadline.IsCancellationRequested && (e is System.Security.Authentication.AuthenticationException || e is IOException))
                {
                    candidate.Dispose();
                    log("TLS: handshake with " + attempts[i] + " was rejected (" + e.Message + "). Trying a more compatible mode.");
                }
                catch { candidate.Dispose(); throw; }
            }
            using var tlsOwner = tls!;
#pragma warning restore SYSLIB0039
            log("tls-ready");
            using var pair = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            var upstream = local.GetStream().CopyToAsync(tlsOwner,pair.Token);
            var downstream = tlsOwner.CopyToAsync(local.GetStream(),pair.Token);
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
}

namespace FluentTlsTunnel.Core {
using Renci.SshNet;
public sealed class Tunnel : IAsyncDisposable
{
    TlsBridge? bridge;
    SshClient? ssh;
    ForwardedPortDynamic? socks;
    public string? RemoteAddress => bridge?.RemoteAddress;
    public string? LocalAddress => bridge?.LocalAddress;
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
            long socksFailures=0;long nextSocksLog=0;
            // SOCKS channel failures can arrive by the hundred; log a rate-limited summary with a running total.
            socks.Exception += (_,e) => { var total=Interlocked.Increment(ref socksFailures); var now=Environment.TickCount64; if(now>=Interlocked.Read(ref nextSocksLog)){Interlocked.Exchange(ref nextSocksLog,now+5000);log("SOCKS: " + e.Exception.Message + " (failures so far: " + total + ")");} };
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
}

namespace FluentTlsTunnel.Core {
using System.Net;
using System.Text.Json;
public sealed class Profile
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 443;
    public string Sni { get; set; } = "";
    public string Username { get; set; } = "";
    public int SocksPort { get; set; } = 1080;
    public bool VerifyTls { get; set; } = false;
    public string Language { get; set; } = "system";
    public string Theme { get; set; } = "system";
    public string Backdrop { get; set; } = "mica-alt";
    public string DnsServers { get; set; } = "1.1.1.1, 1.0.0.1";
    public int AdapterIndex { get; set; }
    public bool UseDns { get; set; }
    public bool RouteWindows { get; set; } = true;
    public bool MaskValues { get; set; }
    public Dictionary<string,string> HostKeys { get; set; } = new();
    public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentTlsTunnel");
    public string Endpoint => $"{Host.ToLowerInvariant()}:{Port}";
    public void Validate()
    {
        if (Uri.CheckHostName(Host) == UriHostNameType.Unknown) throw new ArgumentException("Enter a valid server hostname or IP address.");
        if (Uri.CheckHostName(Sni) != UriHostNameType.Dns) throw new ArgumentException("SNI must be a DNS hostname, without a scheme or path.");
        if (Port is < 1 or > 65535 || SocksPort is < 1024 or > 65535) throw new ArgumentException("Server port: 1-65535. SOCKS port: 1024-65535.");
        if (string.IsNullOrWhiteSpace(Username)) throw new ArgumentException("Enter an SSH username.");
        if (UseDns && (AdapterIndex < 1 || ParseDns(DnsServers).Length == 0)) throw new ArgumentException("Choose an adapter and enter DNS IP addresses.");
    }
    public static string[] ParseDns(string text)
    {
        var values = text.Split(new[] {',',';',' ','\r','\n'}, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length > 4 || values.Any(v => !IPAddress.TryParse(v, out _))) throw new ArgumentException("Enter up to four DNS IP addresses, separated by commas.");
        return values.Select(v => IPAddress.Parse(v).ToString()).Distinct().ToArray();
    }
    public void Save()
    {
        Directory.CreateDirectory(DataDir);
        var temp = Path.Combine(DataDir,"profile.tmp");
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp,Path.Combine(DataDir,"profile.json"),true);
    }
    public static Profile Load()
    {
        try { return JsonSerializer.Deserialize<Profile>(File.ReadAllText(Path.Combine(DataDir,"profile.json"))) ?? new(); }
        catch { return new(); }
    }
}
}
