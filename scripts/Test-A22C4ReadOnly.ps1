# Windows PowerShell 5.1 / PowerShell 7. Diagnostic tests only, NOT a provisioning harness.
[CmdletBinding()]
param([switch]$ConfirmReadOnlyScope)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if ($env:OS -ne 'Windows_NT') { throw 'Windows is required. No tests executed.' }
Write-Host 'Scope: read CurrentUser/My public certificate metadata, assigned NIC metadata, and silent-open random missing CurrentUser software-CNG fixture keys.'
Write-Host 'No keys/certificates/DPAPI data created, modified, signed, exported or deleted. No network listener or profile/firewall changes.'
Write-Host 'Build outputs and test logs are written inside this checkout. This is NOT native security proof.'
if (-not $ConfirmReadOnlyScope) { throw 'Review the scope, then rerun with -ConfirmReadOnlyScope. Nothing executed.' }
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    & dotnet restore PeripheralBatteryCompanion.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
    & dotnet build PeripheralBatteryCompanion.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    & dotnet test tests/PeripheralBatteryCompanion.Windows.Tests/PeripheralBatteryCompanion.Windows.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~ReadOnlySecurityInspectionTests|FullyQualifiedName~ReadOnlyFixtureKeyInspectionTests' --logger 'trx;LogFileName=a22c4-readonly.trx' --results-directory test-results/a22c4-readonly
    if ($LASTEXITCODE -ne 0) { throw 'Read-only tests failed. Stop and inspect locally; do not share user paths or key/store contents.' }
    Write-Host 'Expected: 10 read-only cases passed, 0 failed/skipped. Provisioning/DPAPI/TLS/ACL proof NOT RUN. Stop for owner review.'
} finally { Pop-Location }
