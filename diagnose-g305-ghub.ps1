# Windows PowerShell 5.1 / PowerShell 7. One explicitly invoked SQLite diagnostic, no HID or tray startup.
[CmdletBinding()]
param([switch]$CopySummary)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$exitCode = 0
$summary = ''
$pushed = $false
$windowsHost = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT

function Invoke-InspectorDotNet([string[]]$Arguments) {
    $captured = New-Object 'System.Collections.Generic.List[string]'
    $global:LASTEXITCODE = $null
    $ErrorActionPreference = 'Continue'
    & $dotnetPath @Arguments 2>&1 | ForEach-Object {
        if ($captured.Count -lt 100) { $captured.Add([string]$_) }
    }
    $code = if ($null -eq $LASTEXITCODE) { 1 } else { [int]$LASTEXITCODE }
    return [pscustomobject]@{ ExitCode = $code; Lines = $captured.ToArray() }
}

try {
    if (-not $windowsHost) {
        $exitCode = 2
        $summary = 'G305 G HUB inspection: Windows required. No database or hardware inspection started.'
    } else {
        $command = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $command) {
            $exitCode = 127
            $summary = 'G305 G HUB inspection: required .NET SDK unavailable. No inspection started.'
        } else {
            $dotnetPath = $command.Source
            $project = Join-Path $PSScriptRoot 'src/PeripheralBatteryCompanion.G305GHubInspector/PeripheralBatteryCompanion.G305GHubInspector.csproj'
            if (-not (Test-Path -LiteralPath $project)) { throw 'Missing project' }
            Push-Location -LiteralPath $PSScriptRoot
            $pushed = $true
            foreach ($arguments in @(
                @('restore', $project, '--locked-mode', '--verbosity', 'quiet'),
                @('build', $project, '-c', 'Release', '--no-restore', '--nologo', '--verbosity', 'quiet')
            )) {
                $result = Invoke-InspectorDotNet $arguments
                if ($result.ExitCode -ne 0) {
                    $exitCode = $result.ExitCode
                    # Do not copy compiler/restore output: it can contain private paths or feed credentials.
                    $summary = 'G305 G HUB inspector preparation failed. No inspection started; raw build output omitted for privacy.'
                    break
                }
            }
            if ($exitCode -eq 0) {
                $assembly = Join-Path $PSScriptRoot 'src/PeripheralBatteryCompanion.G305GHubInspector/bin/Release/net10.0/PeripheralBatteryCompanion.G305GHubInspector.dll'
                $result = Invoke-InspectorDotNet @($assembly)
                $exitCode = $result.ExitCode
                $report = $result.Lines -join [Environment]::NewLine
                if (-not $report.StartsWith('G305 G HUB read-only inspection v1') -or $report.Length -gt 10000) {
                    $summary = 'G305 G HUB inspector returned no valid bounded report. Raw runtime output omitted for privacy.'
                    if ($exitCode -eq 0) { $exitCode = 1 }
                } else { $summary = $report }
            }
        }
    }
} catch {
    if ($exitCode -eq 0) { $exitCode = 1 }
    $summary = 'G305 G HUB inspector entry point failed safely. No raw exception details copied.'
} finally {
    if ($pushed) { Pop-Location }
}
$summary += [Environment]::NewLine + "Diagnostic exit code: $exitCode"
if ($CopySummary) {
    if ($windowsHost) {
        try { Set-Clipboard -Value $summary -ErrorAction Stop }
        catch {
            if ($exitCode -eq 0) { $exitCode = 1 }
            $summary += [Environment]::NewLine + "Clipboard copy failed; entry-point exit code: $exitCode"
        }
    } else { $summary += [Environment]::NewLine + 'Windows clipboard: NOT RUN.' }
}
Write-Host $summary
exit $exitCode
