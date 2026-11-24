[CmdletBinding()]
param([switch]$RemoveSettings)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$localRoot = [IO.Path]::GetFullPath($env:LOCALAPPDATA)
$installFolder = [IO.Path]::GetFullPath((Join-Path $localRoot 'Programs/SensiboDesktopWidget'))
$settingsFolder = [IO.Path]::GetFullPath((Join-Path $localRoot 'SensiboDesktopWidget'))
if (-not $installFolder.StartsWith($localRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid installation path.' }
$executable = Join-Path $installFolder 'SensiboWidget.exe'
if (Get-Process SensiboWidget -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $executable }) { throw 'Quit the widget from its tray menu first.' }
if (Test-Path -LiteralPath $installFolder) {
    $marker = Join-Path $installFolder 'installation.json'
    if (-not (Test-Path -LiteralPath $marker) -or (Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json).App -ne 'SensiboDesktopWidget') { throw 'Installation marker missing or invalid.' }
    if ((Get-Item -LiteralPath $installFolder).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing to remove a redirected installation folder.' }
    Remove-Item -LiteralPath $installFolder -Recurse -Force
}
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'SensiboDesktopWidget' -ErrorAction SilentlyContinue
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Sensibo Widget.lnk'
if (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath -Force }
if ($RemoveSettings -and (Test-Path -LiteralPath $settingsFolder)) {
    if ($settingsFolder -ne (Join-Path $localRoot 'SensiboDesktopWidget') -or
        ((Get-Item -LiteralPath $settingsFolder).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Invalid settings path.' }
    Remove-Item -LiteralPath $settingsFolder -Recurse -Force
}
Write-Host 'Uninstalled. Settings are preserved unless -RemoveSettings was supplied.'
