# Explicit mutation opt-in. Not called by test.ps1, tray, CI or ordinary tests.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$OperationId,
      [Parameter(Mandatory=$true)][string]$Confirmation)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if ($env:OS -ne 'Windows_NT') { throw 'Windows required. Nothing executed.' }
$parsed = [Guid]::Empty
if ($OperationId -ieq '3c4a7c190ef34f2e90625276aa9cbfdb') { throw 'Retired failed operation: no rerun, adoption, or cleanup authorized. Nothing executed.' }
if ($Confirmation -cne 'CREATE-ISOLATED-CURRENTUSER-FIXTURE' -or
    -not [Guid]::TryParseExact($OperationId, 'N', [ref]$parsed) -or $parsed -eq [Guid]::Empty) {
    throw 'Exact confirmation and fresh preview operation ID required. Nothing executed.'
}
Write-Host 'Explicit fixture mutation authorized for this operation only. No production identity/network/storage registry.'
$repo = Split-Path $PSScriptRoot -Parent
$project = 'tools/PeripheralBatteryCompanion.SecurityFixture/PeripheralBatteryCompanion.SecurityFixture.csproj'
Push-Location $repo
try {
    & dotnet restore $project --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed. Fixture NOT RUN.' }
    & dotnet build $project -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed. Fixture NOT RUN.' }
    & dotnet tools/PeripheralBatteryCompanion.SecurityFixture/bin/Release/net10.0-windows/PeripheralBatteryCompanion.SecurityFixture.dll --confirm-fixture-creation $OperationId
    if ($LASTEXITCODE -ne 0) { throw 'Fixture failed: STOP. Preserve ownership journal; cleanup may be incomplete. No retries or automatic cleanup.' }
    Write-Host 'STOP for owner review. Run full test.ps1 regression separately after reviewing cleanup status.'
} finally { Pop-Location }
