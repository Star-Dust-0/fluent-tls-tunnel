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
if($LASTEXITCODE -ne 0){throw 'Routing integration tests failed'}
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