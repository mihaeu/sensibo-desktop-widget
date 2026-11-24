[CmdletBinding()]
param([switch]$NoStartup, [switch]$Launch)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path $PSScriptRoot -Parent
$publishFolder = Join-Path $projectRoot 'artifacts/publish'
$installFolder = Join-Path $env:LOCALAPPDATA 'Programs/SensiboDesktopWidget'
$executable = Join-Path $installFolder 'SensiboWidget.exe'
if (-not (Test-Path -LiteralPath (Join-Path $publishFolder 'SensiboWidget.exe'))) { throw 'Run scripts/build.ps1 first.' }
$running = Get-Process SensiboWidget -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $executable }
if ($running) { throw 'Quit the installed widget from its tray menu before updating.' }
if ((Test-Path -LiteralPath $installFolder) -and -not (Test-Path -LiteralPath (Join-Path $installFolder 'installation.json'))) {
    throw "An unrecognized folder already exists at $installFolder. Choose a safe location before installing."
}
if ((Test-Path -LiteralPath $installFolder) -and ((Get-Item -LiteralPath $installFolder).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw 'Refusing to install into a redirected folder.'
}
New-Item -ItemType Directory -Path $installFolder -Force | Out-Null
Get-ChildItem -LiteralPath $publishFolder -File | Copy-Item -Destination $installFolder -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination $installFolder -Force
@{ App = 'SensiboDesktopWidget'; Version = '1.0.0'; InstalledAt = (Get-Date).ToString('o') } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $installFolder 'installation.json') -Encoding utf8
$shell = New-Object -ComObject WScript.Shell
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Sensibo Widget.lnk'
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $executable
$shortcut.WorkingDirectory = $installFolder
$shortcut.Description = 'Small desktop air quality monitor'
$shortcut.Save()
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (-not $NoStartup) {
    if (-not (Test-Path -LiteralPath $runKey)) { New-Item -Path $runKey | Out-Null }
    New-ItemProperty -Path $runKey -Name 'SensiboDesktopWidget' -Value ('"' + $executable + '"') -PropertyType String -Force | Out-Null
}
else { Remove-ItemProperty -Path $runKey -Name 'SensiboDesktopWidget' -ErrorAction SilentlyContinue }
Write-Host "Installed: $executable"
Write-Host "Start menu shortcut: $shortcutPath"
Write-Host ('Start at sign-in: ' + (-not $NoStartup))
if ($Launch) { Start-Process -FilePath $executable -WorkingDirectory $installFolder -WindowStyle Normal }
