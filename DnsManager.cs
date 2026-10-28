using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text;
using FluentTlsTunnel.Core;
namespace FluentTlsTunnel;
public sealed class DnsManager
{
    public record AdapterInfo(int Index,string Name) { public override string ToString() => Name; }
    public static AdapterInfo[] Adapters() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .Select(n => new AdapterInfo(n.GetIPProperties().GetIPv4Properties()?.Index ?? 0,n.Name)).Where(n => n.Index > 0).ToArray();
    public static string Journal => Path.Combine(Profile.DataDir,"dns-restore.json");
    public static bool Pending => File.Exists(Journal);
    static string Quote(string value) => "'" + value.Replace("'","''") + "'";
    public static async Task ApplyAsync(int index,string[] addresses)
    {
        if (Pending) throw new InvalidOperationException("Restore the previous DNS settings first.");
        Directory.CreateDirectory(Profile.DataDir);
        // Only validated IP literals and an integer reach PowerShell. Never interpolate arbitrary hostnames or credentials.
        var list = string.Join(",",addresses.Select(Quote));
        await RunAsync($$"""
        $ErrorActionPreference='Stop'
        $index={{index}}; $journal={{Quote(Journal)}}
        $adapter=Get-NetAdapter -InterfaceIndex $index
        $guid=$adapter.InterfaceGuid.ToString('B')
        $v4=(Get-ItemProperty -LiteralPath ('HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\'+$guid)).NameServer
        $v6=(Get-ItemProperty -LiteralPath ('HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces\'+$guid) -ErrorAction SilentlyContinue).NameServer
        @{Index=$index; Guid=$guid; V4=$v4; V6=$v6} | ConvertTo-Json | Set-Content -LiteralPath $journal -Encoding UTF8
        Set-DnsClientServerAddress -InterfaceIndex $index -ServerAddresses @({{list}})
        """);
    }
    public static async Task RestoreAsync()
    {
        if (!Pending) return;
        await RunAsync($$"""
        $ErrorActionPreference='Stop'
        $journal={{Quote(Journal)}}; $old=Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json
        $adapter=Get-NetAdapter -InterfaceIndex $old.Index
        if($adapter.InterfaceGuid.ToString('B') -ne $old.Guid) {throw 'The network adapter changed. Restore DNS manually.'}
        foreach($family in @('IPv4','IPv6')) {
          $oldValue=if($family -eq 'IPv4'){$old.V4}else{$old.V6}
          $current=Get-DnsClientServerAddress -InterfaceIndex $old.Index -AddressFamily $family
          if([string]::IsNullOrWhiteSpace($oldValue)) {$current | Set-DnsClientServerAddress -ResetServerAddresses}
          else {$current | Set-DnsClientServerAddress -ServerAddresses ($oldValue -split '[,; ]+' | Where-Object {$_})}
        }
        Remove-Item -LiteralPath $journal
        """);
    }
    static async Task RunAsync(string script)
    {
        var encoded=Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var start = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + encoded)
        { UseShellExecute=true, Verb="runas", WindowStyle=ProcessWindowStyle.Hidden };
        using var process=Process.Start(start) ?? throw new InvalidOperationException("Windows did not start the DNS helper.");
        await process.WaitForExitAsync();
        if(process.ExitCode!=0) throw new InvalidOperationException("DNS update failed or administrator permission was declined. Use Restore DNS if a backup is pending.");
    }
                       }
