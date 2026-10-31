# D3.3A-specific scope gate. Original test.ps1 and all assertions remain unchanged.
# No native TLS proof. Do not use the ordinary full runner for this code-only stage.
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$filter = 'FullyQualifiedName!~PublicServerCertificateInspectorTests&FullyQualifiedName!~GeneratedSecretIs256BitsRedactedRevocableAndDisposed&FullyQualifiedName!~SecretExportRequiresExplicitCorrectSizedBuffer'
Write-Host 'D3.3A code-only: excludes 11 legacy certificate-generation cases and 2 real-secret cases.'
Write-Host 'Windows-only tests and native fixtures NOT RUN. TLS proof NOT RUN.'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    dotnet restore PeripheralBatteryCompanion.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet build PeripheralBatteryCompanion.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet test tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj -c Release --no-build --no-restore --filter $filter --logger 'trx;LogFileName=d33a-code-only.trx' --results-directory test-results/d33a-code-only
    exit $LASTEXITCODE
} finally { Pop-Location }
