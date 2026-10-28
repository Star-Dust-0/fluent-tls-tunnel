$ErrorActionPreference='Stop'
. "$PSScriptRoot/RouteDiscovery.ps1"
function Check($ok,$name){if(!$ok){throw $name};Write-Output "PASS $name"}
Check (Test-RoutePrefix '141.98.233.74' '128.0.0.0/1') 'IPv4 split default matches'
Check (!(Test-RoutePrefix '141.98.233.74' '10.0.0.0/8')) 'Unrelated prefix rejected'
Check (Test-RoutePrefix '2001:db8::1' '2001:db8::/32') 'IPv6 prefix matches'
Check (!(Test-RoutePrefix '2001:db8::1' '0.0.0.0/0')) 'Address-family mismatch rejected'
$native=Get-NativeEndpointRoute '1.1.1.1' ''
Check ($native.InterfaceIndex -gt 1) 'Real IPv4 native route index'
$source=Get-NetIPAddress -InterfaceIndex $native.InterfaceIndex -AddressFamily IPv4|Where-Object AddressState -eq Preferred|Select-Object -First 1 -ExpandProperty IPAddress
$constrained=Get-NativeEndpointRoute '1.1.1.1' $source
Check ($constrained.InterfaceIndex -eq $native.InterfaceIndex) 'Connected source address constrains native route'
$v6=Get-NativeEndpointRoute '::1' ''
Check ($v6.InterfaceIndex -eq 1 -and $v6.NextHop -eq '::') 'Native IPv6 row layout and on-link next hop'
function Get-NativeEndpointRoute {param($destination,$source) throw 'Simulated native failure'}
function Get-NetIPInterface {param($AddressFamily,$ErrorAction) @([pscustomobject]@{InterfaceIndex=12;InterfaceAlias='PPP VPN';ConnectionState='Connected';InterfaceMetric=50},[pscustomobject]@{InterfaceIndex=22;InterfaceAlias='Wi-Fi';ConnectionState='Connected';InterfaceMetric=1},[pscustomobject]@{InterfaceIndex=30;InterfaceAlias='Fluent_TLS_Tunnel';ConnectionState='Connected';InterfaceMetric=1})}
function Get-NetRoute {param($AddressFamily,$PolicyStore,$ErrorAction) @([pscustomobject]@{DestinationPrefix='0.0.0.0/0';InterfaceIndex=22;InterfaceAlias='Wi-Fi';NextHop='192.168.1.1';RouteMetric=1},[pscustomobject]@{DestinationPrefix='128.0.0.0/1';InterfaceIndex=12;InterfaceAlias='PPP VPN';NextHop='0.0.0.0';RouteMetric=5},[pscustomobject]@{DestinationPrefix='141.98.233.74/32';InterfaceIndex=30;InterfaceAlias='Fluent_TLS_Tunnel';NextHop='0.0.0.0';RouteMetric=1})}
function Get-NetIPAddress {param($IPAddress,$ErrorAction) [pscustomobject]@{InterfaceIndex=12}}
$fallback=Resolve-EndpointRoute '141.98.233.74' ''
Check ($fallback.InterfaceIndex -eq 12 -and $fallback.NextHop -eq '0.0.0.0') 'Longest-prefix PPP on-link uplink wins; own TUN excluded'
$fallback=Resolve-EndpointRoute '141.98.233.74' '10.0.0.2'
Check ($fallback.InterfaceIndex -eq 12) 'Fallback remains on connected TLS source interface'

$mapped=Resolve-EndpointRoute '::ffff:141.98.233.74' '::ffff:10.0.0.2'
Check ($mapped.InterfaceIndex -eq 12) 'Mapped IPv4 socket addresses use IPv4 route table'
