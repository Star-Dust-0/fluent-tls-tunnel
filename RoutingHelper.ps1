param([int]$Owner,[long]$OwnerTicks,[string]$Endpoint,[int]$SocksPort,[string]$Session,[string]$SourceAddress)
$ErrorActionPreference='Stop'
if($SocksPort -lt 1024 -or $SocksPort -gt 65535){throw 'Invalid port'}
$ip=[System.Net.IPAddress]::Parse($Endpoint)
if($ip.IsIPv4MappedToIPv6){$ip=$ip.MapToIPv4()}
if($SourceAddress){$sourceIp=[Net.IPAddress]::Parse($SourceAddress);if($sourceIp.IsIPv4MappedToIPv6){$SourceAddress=$sourceIp.MapToIPv4().ToString()}}
$root=Join-Path $env:ProgramData 'FluentTlsTunnel'
New-Item $root -ItemType Directory -Force | Out-Null
# Only elevated helpers may alter the recovery journal.
& icacls.exe $root /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
$journal=Join-Path $root 'routing.json'
$lock=New-Object System.Threading.Mutex($false,'Global\FluentTlsTunnel.Routing')
if(!$lock.WaitOne(0)){throw 'Another routing session is active'}
$engine=$null
$state=@{Routes=@(); Rule=$null; Tag=$null; EngineId=0; EngineTicks=0; Adapter='Fluent TLS Tunnel'}
function Save-State { $tmp=$journal+'.tmp';$state|ConvertTo-Json -Depth 8|Set-Content $tmp; Move-Item $tmp $journal -Force }
function Cleanup($old) {
  if($old.Tag){@(Get-DnsClientNrptRule -ErrorAction SilentlyContinue) | Where-Object Comment -eq $old.Tag | Remove-DnsClientNrptRule -Force}
  if($old.Rule){@(Get-DnsClientNrptRule -ErrorAction SilentlyContinue) | Where-Object Name -eq $old.Rule | Remove-DnsClientNrptRule -Force}
  foreach($r in $old.Routes){Get-NetRoute -DestinationPrefix $r.Prefix -InterfaceIndex $r.Index -PolicyStore ActiveStore -ErrorAction SilentlyContinue | Where-Object NextHop -eq $r.Hop | Remove-NetRoute -Confirm:$false}
  if($old.EngineId){$p=Get-Process -Id $old.EngineId -ErrorAction SilentlyContinue; if($p -and $p.StartTime.ToUniversalTime().Ticks -eq $old.EngineTicks){Stop-Process -Id $p.Id -Force}}
  Clear-DnsClientCache
}
function Add-OwnedRoute($prefix,$index,$hop){
  if(Get-NetRoute -DestinationPrefix $prefix -InterfaceIndex $index -PolicyStore ActiveStore -ErrorAction SilentlyContinue | Where-Object NextHop -eq $hop){return}
  # Journal intent before effect; recovery can remove a partially applied route.
  $state.Routes+=@{Prefix=$prefix;Index=$index;Hop=$hop};Save-State
  New-NetRoute -DestinationPrefix $prefix -InterfaceIndex $index -NextHop $hop -RouteMetric 1 -PolicyStore ActiveStore | Out-Null
}
try {
  if(Test-Path $journal){Cleanup (Get-Content $journal -Raw|ConvertFrom-Json);Remove-Item $journal}
  if($Owner -eq 0){return}
  if(Get-NetAdapter -Name 'Fluent_TLS_Tunnel' -ErrorAction SilentlyContinue){throw 'A tunnel adapter is already present. Do not overwrite another session.'}
  # Preserve existing corporate/split DNS policy rather than silently overriding it.
  # Remove only this app's own stale rules (tagged by Comment); never touch rules owned by Windows or other software.
  @(Get-DnsClientNrptRule -ErrorAction SilentlyContinue) | Where-Object {$_.Comment -like 'FluentTlsTunnel-*'} | Remove-DnsClientNrptRule -Force -ErrorAction SilentlyContinue
  # Specific-namespace rules (for example Windows DNS resiliency) remain more specific than our catch-all and are preserved.
  # Only a foreign catch-all rule would conflict with ours.
  $foreignCatchAll=@(Get-DnsClientNrptRule -ErrorAction SilentlyContinue) | Where-Object {@($_.Namespace) -contains '.'} | Select-Object -First 1
  if($foreignCatchAll){throw ('A different Windows catch-all DNS policy is active ('+$foreignCatchAll.Name+'). Automatic routing cannot safely replace it.')}
  $script:DiagnosticPath=Join-Path $Session 'route-diagnostics.txt'
  . (Join-Path $PSScriptRoot 'RouteDiscovery.ps1')
  $route=Resolve-EndpointRoute $ip.ToString() $SourceAddress
  Save-State
  $prefix=$ip.ToString()+$(if($ip.AddressFamily -eq 'InterNetwork'){ '/32' }else{'/128'})
  Add-OwnedRoute $prefix $route.InterfaceIndex $route.NextHop
  $start=@{FilePath=(Join-Path $PSScriptRoot 'Routing/tun2socks.exe');ArgumentList=@('--device','tun://Fluent_TLS_Tunnel','--proxy',"socks5://127.0.0.1:$SocksPort",'--loglevel','info');PassThru=$true;WindowStyle='Hidden';RedirectStandardError=(Join-Path $Session 'engine-error.txt');RedirectStandardOutput=(Join-Path $Session 'engine-output.txt')}
  $engine=Start-Process @start
  $state.EngineId=$engine.Id;$state.EngineTicks=$engine.StartTime.ToUniversalTime().Ticks;$state.Adapter='Fluent_TLS_Tunnel';Save-State
  $adapter=$null
  for($i=0;$i -lt 100;$i++){if($engine.HasExited){throw ('TUN engine stopped: '+(Get-Content (Join-Path $Session 'engine-error.txt') -Raw -ErrorAction SilentlyContinue)+(Get-Content (Join-Path $Session 'engine-output.txt') -Raw -ErrorAction SilentlyContinue))};$adapter=Get-NetAdapter -Name 'Fluent_TLS_Tunnel' -ErrorAction SilentlyContinue;if($adapter){break};Start-Sleep -Milliseconds 100}
  if(!$adapter){throw 'Windows did not create the tunnel adapter'}
  New-NetIPAddress -InterfaceIndex $adapter.ifIndex -IPAddress 198.18.0.1 -PrefixLength 30 -PolicyStore ActiveStore | Out-Null
  Set-NetIPInterface -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4 -AutomaticMetric Disabled -InterfaceMetric 1
  Add-OwnedRoute '0.0.0.0/1' $adapter.ifIndex '0.0.0.0'
  Add-OwnedRoute '128.0.0.0/1' $adapter.ifIndex '0.0.0.0'
  # IPv6 is optional: some systems have it disabled or unbound, and Windows then reports a missing object.
  $v6ok=$true
  try {
    New-NetIPAddress -InterfaceIndex $adapter.ifIndex -IPAddress fd7a:115c:a1e0::1 -PrefixLength 64 -PolicyStore ActiveStore | Out-Null
    Set-NetIPInterface -InterfaceIndex $adapter.ifIndex -AddressFamily IPv6 -AutomaticMetric Disabled -InterfaceMetric 1
    Add-OwnedRoute '::/1' $adapter.ifIndex '::'
    Add-OwnedRoute '8000::/1' $adapter.ifIndex '::'
  } catch {
    $v6ok=$false
    Set-Content (Join-Path $Session 'warning.txt') ('IPv6 routing skipped: '+$_.Exception.Message)
  }
  # New addresses remain Tentative until Windows duplicate-address detection completes.
  $usable=$false
  for($i=0;$i -lt 100;$i++){
    $engine.Refresh();if($engine.HasExited){throw 'Routing engine exited during address setup'}
    $addresses=@(Get-NetIPAddress -InterfaceIndex $adapter.ifIndex -PolicyStore ActiveStore -ErrorAction SilentlyContinue)
    $v4=$addresses|Where-Object {$_.IPAddress -eq '198.18.0.1' -and $_.AddressState -eq 'Preferred'}
    $v6=$addresses|Where-Object {$_.IPAddress -eq 'fd7a:115c:a1e0::1' -and $_.AddressState -eq 'Preferred'}
    if($v4 -and ($v6 -or -not $v6ok)){$usable=$true;break}
    Start-Sleep -Milliseconds 200
  }
  if(!$usable){throw 'Tunnel IP addresses did not become usable'}
  Get-NetIPAddress -InterfaceIndex $adapter.ifIndex -ErrorAction SilentlyContinue | Format-List | Out-File (Join-Path $Session 'routing-state.txt')
  Get-NetRoute -InterfaceIndex $adapter.ifIndex -ErrorAction SilentlyContinue | Format-Table -AutoSize | Out-File (Join-Path $Session 'routing-state.txt') -Append
  $tag='FluentTlsTunnel-'+[guid]::NewGuid().ToString()
  $state.Tag=$tag;Save-State
  $rule=Add-DnsClientNrptRule -Namespace '.' -NameServers '127.0.0.1' -Comment $tag -PassThru
  $state.Rule=$rule.Name;Save-State
  Clear-DnsClientCache
  Write-RouteDiagnostic ('TUN adapter index='+$adapter.ifIndex+' IPv4=198.18.0.1 IPv6='+$v6ok+' engine='+$engine.Id)
  Write-RouteDiagnostic ('TUN routes: '+((Get-NetRoute -InterfaceIndex $adapter.ifIndex | Select-Object DestinationPrefix,NextHop,RouteMetric | ConvertTo-Json -Compress)))
  try { Write-RouteDiagnostic ('Effective 1.1.1.1 route: '+((Find-NetRoute -RemoteIPAddress '1.1.1.1' -ErrorAction SilentlyContinue | Select-Object InterfaceIndex,DestinationPrefix,NextHop | ConvertTo-Json -Compress))) } catch { Write-RouteDiagnostic ('Probe route lookup failed: '+$_.Exception.Message) }
  Set-Content (Join-Path $Session 'ready') 'ready' 
  while(!(Test-Path (Join-Path $Session 'stop'))){
    $parent=Get-Process -Id $Owner -ErrorAction SilentlyContinue
    if(!$parent -or $parent.StartTime.ToUniversalTime().Ticks -ne $OwnerTicks){break}
    $engine.Refresh();if($engine.HasExited){throw 'Tunnel routing engine stopped'}
    Start-Sleep -Milliseconds 250
  }
} catch {Set-Content (Join-Path $Session 'error.txt') ('Routing: '+$_.Exception.Message+[Environment]::NewLine+$(if(Test-Path (Join-Path $Session 'route-diagnostics.txt')){Get-Content (Join-Path $Session 'route-diagnostics.txt') -Raw}))}
finally {
  try {
    # Recovery also catches a crash between adding the DNS rule and saving its name.
    if(Test-Path $journal){Cleanup (Get-Content $journal -Raw|ConvertFrom-Json);Remove-Item $journal}
    if($engine -and !$engine.HasExited){Stop-Process -Id $engine.Id -Force}
    Set-Content (Join-Path $Session 'restored') 'restored'
  } catch {Set-Content (Join-Path $Session 'error.txt') ('Restore failed: '+$_.Exception.Message)}
  $lock.ReleaseMutex();$lock.Dispose()
}
