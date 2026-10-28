using System.Net;
using System.Text.Json;
namespace FluentTlsTunnel.Core;
public sealed class Profile
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 443;
    public string Sni { get; set; } = "";
    public string Username { get; set; } = "";
    public int SocksPort { get; set; } = 1080;
    public bool VerifyTls { get; set; } = true;
    public string Language { get; set; } = "system";
    public string Theme { get; set; } = "system";
    public string DnsServers { get; set; } = "";
    public int AdapterIndex { get; set; }
    public bool UseDns { get; set; }
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