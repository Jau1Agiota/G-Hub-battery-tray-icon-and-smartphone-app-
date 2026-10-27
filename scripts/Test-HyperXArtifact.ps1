# Static package verification only: never starts the tray/helper or touches HID.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [Parameter(Mandatory = $true)][string]$ParentDependencyFile
)
$ErrorActionPreference = 'Stop'
foreach ($relative in @(
    'PeripheralBatteryCompanion.exe', 'THIRD_PARTY_NOTICES.md',
    'hyperx-helper/PeripheralBatteryCompanion.HyperXHelper.exe',
    'hyperx-helper/PeripheralBatteryCompanion.HyperXHelper.dll',
    'hyperx-helper/PeripheralBatteryCompanion.HyperXHelper.deps.json',
    'hyperx-helper/PeripheralBatteryCompanion.HyperXHelper.runtimeconfig.json',
    'hyperx-helper/PeripheralBatteryCompanion.HyperXNative.dll',
    'hyperx-helper/PeripheralBatteryCompanion.HidMetadata.dll',
    'hyperx-helper/PeripheralBatteryCompanion.HyperXHelperProtocol.dll',
    'hyperx-helper/coreclr.dll', 'hyperx-helper/hostpolicy.dll'
)) {
    if (-not (Test-Path -LiteralPath (Join-Path $PublishDirectory $relative) -PathType Leaf)) {
        throw "Missing production package component: $relative"
    }
}
$parent = Get-Content -LiteralPath $ParentDependencyFile -Raw | ConvertFrom-Json
if (@($parent.libraries.PSObject.Properties.Name | Where-Object {
    $_ -match '^PeripheralBatteryCompanion\.(HyperXNative|HyperXBatteryPoc|HyperXHelper)/'
}).Count -ne 0) { throw 'Tray dependency graph includes native transport or diagnostic executable.' }
$helper = Get-Content -LiteralPath (Join-Path $PublishDirectory 'hyperx-helper/PeripheralBatteryCompanion.HyperXHelper.deps.json') -Raw | ConvertFrom-Json
if (@($helper.libraries.PSObject.Properties.Name | Where-Object {
    $_ -match '^PeripheralBatteryCompanion\.(Core|G305Inspector|HyperXBatteryPoc)/'
}).Count -ne 0) { throw 'Production helper depends on tray/database/diagnostic projects.' }
if (@(Get-ChildItem -LiteralPath $PublishDirectory -File | Where-Object {
    $_.Name -match '^PeripheralBatteryCompanion\.(HyperXNative|HyperXBatteryPoc|HyperXHelper)\.'
}).Count -ne 0) { throw 'Native/diagnostic helper files escaped the isolated component directory.' }
if (@(Get-ChildItem -LiteralPath $PublishDirectory -Recurse -File | Where-Object {
    $_.Extension -in @('.pcap', '.pcapng', '.db', '.db-wal', '.db-shm')
}).Count -ne 0) { throw 'Private database/capture file found in package.' }
Write-Host 'PASS: production helper packaged; native transport isolated; diagnostics/private captures excluded. No hardware executed.'
