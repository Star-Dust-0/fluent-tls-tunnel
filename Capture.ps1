Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class CaptureNative {
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle, out RECT rect);
 public struct RECT { public int Left,Top,Right,Bottom; }
}
'@
New-Item artifacts/screenshots -ItemType Directory -Force | Out-Null
foreach($lang in @('en','ar')) {
 $app=Start-Process artifacts/portable/FluentTlsTunnel.exe -ArgumentList "--preview=$lang" -PassThru
 try {
  for($i=0;$i -lt 30;$i++) {Start-Sleep -Milliseconds 500;$app.Refresh();if($app.MainWindowHandle -ne [IntPtr]::Zero){break};if($app.HasExited){throw 'Application exited before screenshot'}}
  if($app.MainWindowHandle -eq [IntPtr]::Zero){throw 'No application window'}
  [CaptureNative]::SetForegroundWindow($app.MainWindowHandle) | Out-Null
  Start-Sleep -Seconds 2
  $rect=New-Object CaptureNative+RECT
  [CaptureNative]::GetWindowRect($app.MainWindowHandle,[ref]$rect) | Out-Null
  $bitmap=New-Object System.Drawing.Bitmap(($rect.Right-$rect.Left),($rect.Bottom-$rect.Top))
  $g=[System.Drawing.Graphics]::FromImage($bitmap)
  $g.CopyFromScreen($rect.Left,$rect.Top,0,0,$bitmap.Size)
  $bitmap.Save((Join-Path (Get-Location) "artifacts/screenshots/$lang.png"))
  $g.Dispose();$bitmap.Dispose()
 } finally {if(!$app.HasExited){$app.Kill();$app.WaitForExit()}}
}