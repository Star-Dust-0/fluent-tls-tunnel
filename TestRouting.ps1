$ErrorActionPreference='Stop'
function Snapshot {
 @{
 Routes=@(Get-NetRoute -PolicyStore ActiveStore | Select-Object DestinationPrefix,InterfaceIndex,NextHop,RouteMetric | Sort-Object DestinationPrefix,InterfaceIndex,NextHop)
 DNS=@(Get-DnsClientNrptRule | Select-Object Name,Namespace,NameServers | Sort-Object Name)
 AdapterDNS=@(Get-DnsClientServerAddress | Select-Object InterfaceIndex,AddressFamily,ServerAddresses | Sort-Object InterfaceIndex,AddressFamily)
 }|ConvertTo-Json -Depth 8 -Compress
}
$before=Snapshot
& dotnet run --project RoutingTests.csproj -c Release
if($LASTEXITCODE -ne 0){Get-ChildItem (Join-Path $env:LOCALAPPDATA 'FluentTlsTunnel') -Recurse -Filter '*-*.txt' | ForEach-Object {Get-Content $_.FullName};throw 'Routing integration tests failed'}
Start-Sleep -Seconds 2
$after=Snapshot
if($before -ne $after){$before|Set-Content artifacts/routing-before.json;$after|Set-Content artifacts/routing-after.json;throw 'Route/DNS state was not restored exactly'}
'PASS normal disconnect restores original routes, DNS policy and adapter DNS' | Tee-Object artifacts/routing-tests.txt
# Crash watchdog: helper must remove routing after its owning process dies.
$owner=Start-Process powershell.exe -ArgumentList '-NoProfile -Command Start-Sleep -Seconds 120' -PassThru
$session=Join-Path $env:TEMP ('FluentRouteTest-'+[guid]::NewGuid());New-Item $session -ItemType Directory|Out-Null
try {
 $helper=Start-Process powershell.exe -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',"$PWD/RoutingHelper.ps1",'-Owner',$owner.Id,'-OwnerTicks',$owner.StartTime.ToUniversalTime().Ticks,'-Endpoint','1.1.1.1','-SocksPort','19080','-Session',$session) -PassThru
 for($i=0;$i -lt 200;$i++){if(Test-Path "$session/ready"){break};if(Test-Path "$session/error.txt"){throw (Get-Content "$session/error.txt" -Raw)};Start-Sleep -Milliseconds 100}
 if(!(Test-Path "$session/ready")){throw 'Crash test routing did not start'}
 Stop-Process -Id $owner.Id -Force
 if(!$helper.WaitForExit(20000)){throw 'Watchdog did not exit after owner crash'}
 if(!(Test-Path "$session/restored")){throw 'Watchdog did not confirm restore'}
 Start-Sleep -Seconds 2
 if($before -ne (Snapshot)){throw 'Crash watchdog did not restore original route/DNS state'}
 'PASS crash watchdog restores route and DNS state' | Tee-Object artifacts/routing-tests.txt -Append
} finally {if(!$owner.HasExited){Stop-Process -Id $owner.Id -Force};if($helper -and !$helper.HasExited){Set-Content "$session/stop" 'stop';$helper.WaitForExit(20000)|Out-Null}}

# Exercise the production helper on real Windows networking while only replacing endpoint-route lookup.
foreach($mode in @('empty','throws','all-cim-fail','mapped-v4')) {
 $owner=Start-Process powershell.exe -ArgumentList '-NoProfile -Command Start-Sleep -Seconds 120' -PassThru
 $session=Join-Path $env:TEMP ('FluentFallback-'+[guid]::NewGuid());New-Item $session -ItemType Directory|Out-Null
 $wrapper=Join-Path $session 'fallback.ps1'
 # Literal mock source, then invoke the unchanged production helper in the same script scope.
 $mock=if($mode -eq 'throws'){'function Find-NetRoute {param($RemoteIPAddress,$ErrorAction) throw "The requested object could not be found"}'}else{'function Find-NetRoute {param($RemoteIPAddress,$ErrorAction) return $null}'}
 if($mode -eq 'all-cim-fail'){$mock+='; function Get-NetRoute {param($DestinationPrefix,$PolicyStore,$AddressFamily,$InterfaceIndex,$ErrorAction) if($AddressFamily){throw "Simulated CIM route query failure"}; NetTCPIP\Get-NetRoute @PSBoundParameters}; function Get-NetIPInterface {param($InterfaceIndex,$AddressFamily,$ErrorAction) throw "Simulated CIM interface query failure"}'}
 $testEndpoint=if($mode -eq 'mapped-v4'){'::ffff:192.0.2.74'}else{'192.0.2.74'}
 @($mock,"& '$PWD/RoutingHelper.ps1' -Owner $($owner.Id) -OwnerTicks $($owner.StartTime.ToUniversalTime().Ticks) -Endpoint '$testEndpoint' -SocksPort 19080 -Session '$session'")|Set-Content $wrapper
 $helper=$null
 try {
  $helper=Start-Process powershell.exe -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',$wrapper) -PassThru
  for($i=0;$i -lt 200;$i++){if(Test-Path "$session/ready"){break};if(Test-Path "$session/error.txt"){throw (Get-Content "$session/error.txt" -Raw)};Start-Sleep -Milliseconds 100}
  if(!(Test-Path "$session/ready")){throw "Fallback $mode did not activate routing"}
  $diagnostics=Get-Content "$session/route-diagnostics.txt" -Raw
  if($diagnostics -notmatch 'Selected GetBestRoute2 interface='){throw 'Native route selection diagnostic missing'}
  if($mode -eq 'all-cim-fail' -and $diagnostics -notmatch 'inventory failed'){throw 'CIM errors were not diagnosed'}
  Set-Content "$session/stop" stop
  if(!$helper.WaitForExit(20000)){throw "Fallback $mode did not stop"}
  if(!(Test-Path "$session/restored")){throw "Fallback $mode did not confirm restore"}
  if(Test-Path "$session/error.txt"){throw (Get-Content "$session/error.txt" -Raw)}
  Start-Sleep -Seconds 2
  if($before -ne (Snapshot)){throw "Fallback $mode changed original route/DNS state"}
  "PASS Find-NetRoute ${mode}: native endpoint route starts and restores exactly"|Tee-Object artifacts/routing-tests.txt -Append
 } finally {if(!$owner.HasExited){Stop-Process -Id $owner.Id -Force};if($helper -and !$helper.HasExited){Set-Content "$session/stop" stop;$helper.WaitForExit(20000)|Out-Null}}
}
# Recovery journal can refer to a rule already removed. No NRPT result must be harmless.
$journal=Join-Path $env:ProgramData 'FluentTlsTunnel/routing.json'
@{Routes=@();Tag='FluentTlsTunnel-absent-regression';Rule='{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}';EngineId=0;EngineTicks=0}|ConvertTo-Json|Set-Content $journal
$session=Join-Path $env:TEMP ('FluentEmptyDns-'+[guid]::NewGuid());New-Item $session -ItemType Directory|Out-Null
& ./RoutingHelper.ps1 -Owner 0 -Endpoint 1.1.1.1 -SocksPort 19080 -Session $session
if(Test-Path "$session/error.txt"){throw (Get-Content "$session/error.txt" -Raw)}
if(Test-Path $journal){throw 'Empty DNS recovery did not remove journal'}
if($before -ne (Snapshot)){throw 'Empty DNS cleanup changed original state'}
'PASS cleanup tolerates absent NRPT rules'|Tee-Object artifacts/routing-tests.txt -Append
'passed' | Set-Content artifacts/routing-passed.txt
