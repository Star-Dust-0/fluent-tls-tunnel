# Query the same Windows IP stack used by the connected TLS socket. Virtual,
# PPP and on-link routes are valid uplinks; only our own TUN and loopback are excluded.
function Write-RouteDiagnostic([string]$text) {
  if($script:DiagnosticPath){Add-Content -LiteralPath $script:DiagnosticPath -Value $text -Encoding UTF8}
}
function Get-NativeEndpointRoute([string]$destination,[string]$source) {
  if(-not ('FluentRouteNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Net;
using System.Runtime.InteropServices;
public static class FluentRouteNative {
  [DllImport("iphlpapi.dll", ExactSpelling=true)]
  static extern uint GetBestRoute2(IntPtr luid, uint index, IntPtr source, IntPtr destination, uint options, IntPtr row, IntPtr bestSource);
  static IntPtr Address(string text) {
    IPAddress ip=IPAddress.Parse(text); byte[] b=new byte[28];
    bool v4=ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork;
    BitConverter.GetBytes((ushort)(v4?2:23)).CopyTo(b,0);
    ip.GetAddressBytes().CopyTo(b,v4?4:8);
    if(!v4) BitConverter.GetBytes((uint)ip.ScopeId).CopyTo(b,24);
    IntPtr p=Marshal.AllocHGlobal(28);Marshal.Copy(b,0,p,28);return p;
  }
  public static string[] Lookup(string destination,string source) {
    IntPtr dest=Address(destination), src=IntPtr.Zero, row=Marshal.AllocHGlobal(104), best=Marshal.AllocHGlobal(28);
    try {
      if(!String.IsNullOrWhiteSpace(source))src=Address(source);
      uint error=GetBestRoute2(IntPtr.Zero,0,src,dest,0,row,best);
      if(error!=0)throw new System.ComponentModel.Win32Exception((int)error,"GetBestRoute2 failed ("+error+")");
      uint index=unchecked((uint)Marshal.ReadInt32(row,8));
      int family=Marshal.ReadInt16(row,44); if(family!=2 && family!=23)throw new InvalidOperationException("Unexpected next-hop family: "+family);
      byte[] bytes=new byte[family==2?4:16];Marshal.Copy(IntPtr.Add(row,family==2?48:52),bytes,0,bytes.Length);
      IPAddress hop=family==2?new IPAddress(bytes):new IPAddress(bytes,unchecked((uint)Marshal.ReadInt32(row,68)));
      return new[]{index.ToString(),hop.ToString()};
    } finally {Marshal.FreeHGlobal(dest);if(src!=IntPtr.Zero)Marshal.FreeHGlobal(src);Marshal.FreeHGlobal(row);Marshal.FreeHGlobal(best);}
  }
}
'@
  }
  $result=[FluentRouteNative]::Lookup($destination,$source)
  [pscustomobject]@{InterfaceIndex=[int]$result[0];NextHop=$result[1];Method='GetBestRoute2'}
}
function Test-RoutePrefix([string]$destination,[string]$prefix) {
  try {
    $parts=$prefix.Split('/');$a=[Net.IPAddress]::Parse($destination).GetAddressBytes();$b=[Net.IPAddress]::Parse($parts[0]).GetAddressBytes();$bits=[int]$parts[1]
    if($a.Length -ne $b.Length -or $bits -lt 0 -or $bits -gt $a.Length*8){return $false}
    for($i=0;$i -lt $a.Length -and $bits -gt 0;$i++){$n=[Math]::Min(8,$bits);$mask=(255 -shl (8-$n)) -band 255;if(($a[$i] -band $mask) -ne ($b[$i] -band $mask)){return $false};$bits-=$n}
    return $true
  } catch {return $false}
}
function Resolve-EndpointRoute([string]$destination,[string]$source) {
  $destIp=[Net.IPAddress]::Parse($destination)
  if($destIp.IsIPv4MappedToIPv6){Write-RouteDiagnostic "Mapped endpoint normalized: $destination";$destination=$destIp.MapToIPv4().ToString()}
  if($source){$srcIp=[Net.IPAddress]::Parse($source);if($srcIp.IsIPv4MappedToIPv6){$source=$srcIp.MapToIPv4().ToString()}}
  $family=if(([Net.IPAddress]::Parse($destination)).AddressFamily -eq 'InterNetwork'){'IPv4'}else{'IPv6'}
  Write-RouteDiagnostic "Endpoint=$destination TLS-source=$source AddressFamily=$family"
  foreach($query in @('interfaces','routes')) {
    try {
      if($query -eq 'interfaces'){$items=@(Get-NetIPInterface -AddressFamily $family -ErrorAction Stop|Select-Object InterfaceIndex,InterfaceAlias,ConnectionState,InterfaceMetric)}
      else {$items=@(Get-NetRoute -AddressFamily $family -PolicyStore ActiveStore -ErrorAction Stop|Select-Object DestinationPrefix,InterfaceIndex,InterfaceAlias,NextHop,RouteMetric)}
      Write-RouteDiagnostic ($query+': '+(ConvertTo-Json -InputObject $items -Compress))
    } catch {Write-RouteDiagnostic ($query+' inventory failed: '+$_.Exception.Message)}
  }
  try {
    $native=Get-NativeEndpointRoute $destination $source
    if($native.InterfaceIndex -le 1){throw 'Native route has no usable non-loopback interface index'}
    # Get-NetIPInterface can itself fail on affected Windows installations. Native
    # route selection is still authoritative; no hardware-adapter requirement.
    try {$iface=Get-NetIPInterface -InterfaceIndex $native.InterfaceIndex -AddressFamily $family -ErrorAction Stop
      if($iface.InterfaceAlias -eq 'Fluent_TLS_Tunnel' -or $iface.InterfaceIndex -eq 1){throw 'Native route points to our TUN or loopback'}
      Write-RouteDiagnostic ('Native interface: '+($iface|Select-Object InterfaceIndex,InterfaceAlias,ConnectionState,InterfaceMetric|ConvertTo-Json -Compress))
    } catch {if($_.Exception.Message -eq 'Native route points to our TUN or loopback'){throw};Write-RouteDiagnostic ('Interface query failed: '+$_.Exception.Message)}
    Write-RouteDiagnostic "Selected GetBestRoute2 interface=$($native.InterfaceIndex) nextHop=$($native.NextHop)"
    return $native
  } catch {Write-RouteDiagnostic ('Native lookup failed: '+$_.Exception.Message)}
  try {
    $interfaces=@(Get-NetIPInterface -AddressFamily $family -ErrorAction Stop)
    Write-RouteDiagnostic ('Interfaces: '+($interfaces|Select-Object InterfaceIndex,InterfaceAlias,ConnectionState,InterfaceMetric|ConvertTo-Json -Compress))
    $sourceIndexes=@()
    if($source){$sourceIndexes=@(Get-NetIPAddress -IPAddress $source -ErrorAction Stop|Select-Object -ExpandProperty InterfaceIndex)}
    $routes=@(Get-NetRoute -AddressFamily $family -PolicyStore ActiveStore -ErrorAction Stop)
    Write-RouteDiagnostic ('Routes: '+($routes|Select-Object DestinationPrefix,InterfaceIndex,InterfaceAlias,NextHop,RouteMetric|ConvertTo-Json -Compress))
    $candidates=@($routes|Where-Object {
      $r=$_;$f=$interfaces|Where-Object InterfaceIndex -eq $r.InterfaceIndex|Select-Object -First 1
      $r.InterfaceIndex -ne 1 -and $r.InterfaceAlias -ne 'Fluent_TLS_Tunnel' -and $f -and $f.ConnectionState -eq 'Connected' -and
      (!$source -or $r.InterfaceIndex -in $sourceIndexes) -and (Test-RoutePrefix $destination $r.DestinationPrefix)
    }|Sort-Object @{Expression={[int]($_.DestinationPrefix.Split('/')[1])};Descending=$true},@{Expression={
      $r=$_;$f=$interfaces|Where-Object InterfaceIndex -eq $r.InterfaceIndex|Select-Object -First 1;[long]$r.RouteMetric+[long]$f.InterfaceMetric
    }})
    if($candidates.Count){$route=$candidates[0];Write-RouteDiagnostic "Selected route-table interface=$($route.InterfaceIndex) nextHop=$($route.NextHop) prefix=$($route.DestinationPrefix)";return $route}
    Write-RouteDiagnostic 'No eligible matching route-table entry.'
  } catch {Write-RouteDiagnostic ('Route-table lookup failed: '+$_.Exception.Message)}
  throw 'Cannot preserve the TLS endpoint route. See routing diagnostics below.'
}
