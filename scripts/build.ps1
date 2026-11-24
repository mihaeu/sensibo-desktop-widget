[CmdletBinding()]
param([string]$DotnetPath = 'dotnet')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $expectedSdk = (Get-Content global.json -Raw | ConvertFrom-Json).sdk.version
    $actualSdk = (& $DotnetPath --version)
    if ($LASTEXITCODE -ne 0 -or $actualSdk -ne $expectedSdk) { throw "Install .NET SDK $expectedSdk, then run this script again." }
    & $DotnetPath restore SensiboDesktopWidget.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    & $DotnetPath build SensiboDesktopWidget.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $DotnetPath run --project tests/SensiboWidget.Tests -c Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
    & $DotnetPath publish src/SensiboWidget/SensiboWidget.csproj -c Release --no-restore -o artifacts/publish
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $hash = Get-FileHash artifacts/publish/SensiboWidget.exe -Algorithm SHA256
    "$($hash.Hash)  SensiboWidget.exe" | Set-Content artifacts/SHA256SUMS.txt -Encoding ascii
    Write-Host 'Ready: artifacts/publish/SensiboWidget.exe'
}
finally { Pop-Location }
