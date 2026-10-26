# Windows PowerShell 5.1 / PowerShell 7. No elevation or machine-policy changes.
[CmdletBinding()]
param([switch]$CopySummary)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = $PSScriptRoot
$exitCode = 0
$lines = New-Object 'System.Collections.Generic.List[string]'
$pushedLocation = $false
$windowsHost = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT

function Protect-Text([string]$Text) {
    $safe = $Text
    foreach ($privatePath in @($repoRoot, $env:USERPROFILE, $env:HOME)) {
        if (-not [string]::IsNullOrWhiteSpace($privatePath) -and $privatePath.Length -gt 1) {
            foreach ($variant in @($privatePath, $privatePath.Replace('\', '/'))) {
                $safe = [regex]::Replace($safe, [regex]::Escape($variant), '<local-path>', 'IgnoreCase')
            }
        }
    }
    $safe = [regex]::Replace($safe, '(?i)(https?://)[^/\s@]+@', '$1<redacted>@')
    return [regex]::Replace($safe, '(?i)([?&](?:token|access_token|api_key|password|sig)=)[^&\s]+', '$1<redacted>')
}

function Invoke-DotNet([string[]]$Arguments) {
    $captured = New-Object 'System.Collections.Generic.List[string]'
    $ErrorActionPreference = 'Continue'
    $global:LASTEXITCODE = $null
    & $dotnetPath @Arguments 2>&1 | ForEach-Object { $captured.Add((Protect-Text ([string]$_))) }
    $code = if ($null -eq $LASTEXITCODE) { 1 } else { [int]$LASTEXITCODE }
    return [pscustomobject]@{ ExitCode = $code; Lines = @($captured.ToArray()) }
}

try {
    if (-not $windowsHost) {
        $exitCode = 2
        throw 'Windows is required. Native inspection was not started.'
    }
    $dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $dotnet) {
        $exitCode = 127
        throw 'The .NET SDK required by global.json was not found on PATH. Inspection was not started.'
    }
    $dotnetPath = $dotnet.Source
    $project = Join-Path $repoRoot 'src/PeripheralBatteryCompanion.G305Inspector/PeripheralBatteryCompanion.G305Inspector.csproj'
    if (-not (Test-Path -LiteralPath $project)) { throw 'The diagnostic project is missing. Restore the checkout before running it.' }
    Push-Location -LiteralPath $repoRoot
    $pushedLocation = $true
    Write-Host 'Preparing the passive HID metadata inspector...'
    foreach ($arguments in @(
        @('restore', $project, '--locked-mode', '--verbosity', 'quiet'),
        @('build', $project, '-c', 'Release', '--no-restore', '--nologo', '--verbosity', 'quiet')
    )) {
        $result = Invoke-DotNet $arguments
        if ($result.ExitCode -ne 0) {
            $exitCode = $result.ExitCode
            $lines.Add('Inspector preparation failed; native inspection was not started.')
            foreach ($line in ($result.Lines | Select-Object -Last 15)) { $lines.Add($line) }
            break
        }
    }
    if ($exitCode -eq 0) {
        $assembly = Join-Path $repoRoot 'src/PeripheralBatteryCompanion.G305Inspector/bin/Release/net10.0/PeripheralBatteryCompanion.G305Inspector.dll'
        if (-not (Test-Path -LiteralPath $assembly)) { throw 'The diagnostic executable was not produced. Inspection was not started.' }
        $result = Invoke-DotNet @($assembly)
        $exitCode = $result.ExitCode
        foreach ($line in $result.Lines) { $lines.Add($line) }
        if ($result.Lines.Count -eq 0) {
            $lines.Add('Inspector returned no report. No successful metadata inspection is claimed.')
            if ($exitCode -eq 0) { $exitCode = 1 }
        }
    }
} catch {
    if ($exitCode -eq 0) { $exitCode = 1 }
    $lines.Add((Protect-Text $_.Exception.Message))
} finally {
    if ($pushedLocation) { Pop-Location }
}

$lines.Add('')
$lines.Add("Diagnostic process exit code: $exitCode")
$summary = $lines -join [Environment]::NewLine
if ($CopySummary) {
    if ($windowsHost) {
        try {
            Set-Clipboard -Value $summary -ErrorAction Stop
            Write-Host 'Sanitized diagnostic summary copied to clipboard.'
        } catch {
            if ($exitCode -eq 0) { $exitCode = 1 }
            $summary += [Environment]::NewLine + 'Clipboard copy failed; use the sanitized terminal report.'
            $summary += [Environment]::NewLine + "Entry-point exit code: $exitCode"
        }
    } else { $summary += [Environment]::NewLine + 'Windows clipboard copy: NOT RUN.' }
}
Write-Host $summary
exit $exitCode
