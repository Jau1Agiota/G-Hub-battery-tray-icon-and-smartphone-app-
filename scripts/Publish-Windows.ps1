$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    dotnet publish src/PeripheralBatteryCompanion.Windows -c Release `
        -p:RestoreLockedMode=true -p:DebugType=None -o artifacts/win-x64
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
    $exe = 'artifacts/win-x64/PeripheralBatteryCompanion.exe'
    if (-not (Test-Path $exe)) { throw 'Published executable is missing' }
    Get-FileHash $exe -Algorithm SHA256
    Compress-Archive -Path artifacts/win-x64/* -DestinationPath artifacts/PeripheralBatteryCompanion-win-x64.zip -Force
} finally { Pop-Location }
