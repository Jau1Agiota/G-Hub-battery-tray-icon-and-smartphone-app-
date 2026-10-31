# Compatible with Windows PowerShell 5.1 and PowerShell 7. No elevation or extra modules required.
[CmdletBinding()]
param(
    [switch]$Release,
    [switch]$BuildOnly,
    [switch]$CopySummary
)

$ErrorActionPreference = 'Stop'
# In PowerShell 7, handle native failures ourselves so their original exit codes survive.
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = $PSScriptRoot
$configuration = if ($Release) { 'Release' } else { 'Debug' }
$logDirectory = Join-Path $repoRoot 'test-results'
$resultsDirectory = Join-Path $logDirectory 'automated'
$logPath = Join-Path $logDirectory 'latest.log'
$summaryPath = Join-Path $logDirectory 'summary.txt'
$statuses = [ordered]@{ Restore = 'NOT RUN'; Build = 'NOT RUN'; 'Automated tests' = 'NOT RUN' }
$failureDetails = New-Object 'System.Collections.Generic.List[string]'
$exitCode = 0
$sdkVersion = 'Unavailable'
$passed = 0L
$failed = 0L
$skipped = 0L
$executed = 0L
$parsedRuns = 0
$expectedRuns = 0
$clipboardStatus = if ($CopySummary) { 'Requested' } else { 'Not requested' }
$logWriter = $null
$runLock = $null
$pushedLocation = $false
$verboseOutput = $PSBoundParameters.ContainsKey('Verbose') -and [bool]$PSBoundParameters['Verbose']

function Protect-Text([string]$Text) {
    # Shareable logs/summaries never need the full checkout or user-profile path.
    $safe = $Text
    foreach ($privatePath in @($repoRoot, $env:USERPROFILE, $env:HOME)) {
        if (-not [string]::IsNullOrWhiteSpace($privatePath) -and $privatePath.Length -gt 1) {
            foreach ($variant in @($privatePath, $privatePath.Replace('\', '/'))) {
                $safe = [regex]::Replace($safe, [regex]::Escape($variant), '<local-path>', 'IgnoreCase')
            }
        }
    }
    $safe = [regex]::Replace($safe, '(?i)(https?://)[^/\s@]+@', '$1<redacted>@')
    $safe = [regex]::Replace($safe, '(?i)([?&](?:token|access_token|api_key|password|sig)=)[^&\s]+', '$1<redacted>')
    return $safe
}

function Invoke-DotNet([string]$Step, [string[]]$Arguments) {
    Write-Host "[$Step] Running..."
    $logWriter.WriteLine((Protect-Text ("`n[$Step] dotnet " + ($Arguments -join ' '))))
    $tail = New-Object 'System.Collections.Generic.Queue[string]'
    $errors = New-Object 'System.Collections.Generic.List[string]'
    # Windows PowerShell 5.1 represents native stderr as ErrorRecord objects.
    # Capture it as text; decide success solely from the native command's exit code.
    $ErrorActionPreference = 'Continue'
    $global:LASTEXITCODE = $null
    & $dotnetPath @Arguments 2>&1 | ForEach-Object {
        $line = Protect-Text ([string]$_)
        $logWriter.WriteLine($line)
        if ($verboseOutput) { Write-Host $line }
        $tail.Enqueue($line)
        if ($tail.Count -gt 18) { [void]$tail.Dequeue() }
        if ($line -match '(?i)\berror\b|\bfailed\b|\bexception\b' -and $errors.Count -lt 12) { $errors.Add($line) }
    }
    $nativeExit = $LASTEXITCODE
    if ($null -eq $nativeExit) { $nativeExit = 1 }
    return [pscustomobject]@{ ExitCode = [int]$nativeExit; Tail = @($tail.ToArray()); Errors = @($errors.ToArray()) }
}

function Add-CommandFailure([string]$Step, $Result) {
    $failureDetails.Add("$Step failed (exit $($Result.ExitCode)).")
    $lines = if ($Result.Errors.Count -gt 0) { $Result.Errors } else { $Result.Tail }
    foreach ($line in $lines) { $failureDetails.Add($line) }
}

function Get-Counter($Counters, [string]$Name) {
    $value = $Counters.GetAttribute($Name)
    if ([string]::IsNullOrEmpty($value)) { return 0L }
    return [long]$value
}

function New-Summary {
    $lines = New-Object 'System.Collections.Generic.List[string]'
    $lines.Add('========================================')
    $lines.Add(' Peripheral Battery Companion')
    $lines.Add(' Local Verification')
    $lines.Add('========================================')
    $lines.Add(".NET SDK: $sdkVersion")
    $lines.Add("Configuration: $configuration")
    $lines.Add('')
    foreach ($step in $statuses.Keys) { $lines.Add(('{0,-18} {1}' -f ($step + ':'), $statuses[$step])) }
    $lines.Add('')
    if ($parsedRuns -gt 0) {
        $lines.Add("Passed: $passed")
        $lines.Add("Failed: $failed")
        $lines.Add("Skipped: $skipped")
        $lines.Add("Executed: $executed")
        $lines.Add("Result files read: $parsedRuns / $expectedRuns")
    } else {
        $lines.Add('Test counts: NOT RUN or unavailable (no current result file parsed)')
    }
    $lines.Add("Clipboard: $clipboardStatus")
    $lines.Add('')
    $overall = if ($exitCode -eq 0) { 'PASS' } else { 'FAIL' }
    $lines.Add("Overall result: $overall (exit $exitCode)")
    if ($BuildOnly) { $lines.Add('Build-only run; automated tests were not executed.') }
    $lines.Add('Native tray / physical-device checks: NOT RUN by this script')
    if ($failureDetails.Count -gt 0) {
        $lines.Add('')
        $lines.Add('Failure details:')
        # Bound clipboard size; the readable log and TRX retain the remaining diagnostics.
        foreach ($detail in ($failureDetails | Select-Object -First 45)) {
            $safeDetail = Protect-Text $detail
            if ($safeDetail.Length -gt 1200) { $safeDetail = $safeDetail.Substring(0, 1200) + ' ... (see logs/TRX)' }
            $lines.Add($safeDetail)
        }
        if ($failureDetails.Count -gt 45) { $lines.Add('Further details are in test-results/latest.log and automated/.') }
    }
    $lines.Add('Logs: test-results/latest.log; test-results/summary.txt; test-results/automated/')
    $lines.Add('========================================')
    return $lines -join [Environment]::NewLine
}

try {
    [void][System.IO.Directory]::CreateDirectory($logDirectory)
    # Prevent two runs from overwriting one another's latest log/results.
    $runLock = [System.IO.File]::Open((Join-Path $logDirectory 'runner.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    if (Test-Path -LiteralPath $resultsDirectory) { Remove-Item -LiteralPath $resultsDirectory -Recurse -Force }
    [void][System.IO.Directory]::CreateDirectory($resultsDirectory)
    $logWriter = New-Object System.IO.StreamWriter($logPath, $false, (New-Object System.Text.UTF8Encoding($false)))
    $logWriter.AutoFlush = $true
    Push-Location -LiteralPath $repoRoot
    $pushedLocation = $true

    $dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $dotnet) {
        $exitCode = 127
        throw 'The .NET SDK was not found on PATH. Install the SDK required by global.json, then reopen PowerShell.'
    }
    $dotnetPath = $dotnet.Source
    $sdk = Invoke-DotNet 'SDK' @('--version')
    if ($sdk.ExitCode -ne 0) {
        $exitCode = $sdk.ExitCode
        Add-CommandFailure 'SDK selection' $sdk
        throw 'The SDK selected by global.json is not available. See the SDK error above.'
    }
    $sdkVersion = $sdk.Tail | Where-Object { $_ -match '^\d+\.\d+\.\d+' } | Select-Object -Last 1
    if ([string]::IsNullOrEmpty($sdkVersion)) { throw 'dotnet --version did not report a usable SDK version.' }

    $preferredSolution = Join-Path $repoRoot 'PeripheralBatteryCompanion.sln'
    $solutions = @(Get-ChildItem -LiteralPath $repoRoot -File | Where-Object { $_.Extension -in @('.sln', '.slnx') })
    $projects = @(Get-ChildItem -LiteralPath $repoRoot -Filter '*.csproj' -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj|test-results)[\\/]' -and $_.Name -ne 'PeripheralBatteryCompanion.W1Offline.Tests.csproj' })
    if ($projects.Count -eq 0) { throw 'No C# project files found; restore the checkout before running verification.' }
    $testProjects = @($projects | Where-Object {
        [xml]$projectXml = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8
        $testFlag = @($projectXml.SelectNodes("//*[local-name()='IsTestProject']") | Where-Object { $_.InnerText -eq 'true' })
        $testSdk = $projectXml.SelectNodes("//*[local-name()='PackageReference'][@Include='Microsoft.NET.Test.Sdk']")
        $testFlag.Count -gt 0 -or $testSdk.Count -gt 0
    })
    if (-not $BuildOnly -and $testProjects.Count -eq 0) { throw 'No automated test projects found; refusing a zero-test success.' }
    $expectedRuns = $testProjects.Count
    if (Test-Path -LiteralPath $preferredSolution) { $targets = @($preferredSolution) }
    elseif ($solutions.Count -eq 1) { $targets = @($solutions[0].FullName) }
    elseif ($solutions.Count -gt 1) { throw 'Multiple solution files found. Restore the expected PeripheralBatteryCompanion.sln.' }
    else { $targets = @($projects | ForEach-Object { $_.FullName }) }

    $verbosity = if ($verboseOutput) { 'normal' } else { 'minimal' }
    foreach ($target in $targets) {
        $result = Invoke-DotNet 'Restore' @('restore', $target, '--locked-mode', '--verbosity', $verbosity)
        if ($result.ExitCode -ne 0) {
            $statuses['Restore'] = 'FAIL'; $exitCode = $result.ExitCode
            Add-CommandFailure 'Restore' $result
            break
        }
    }
    if ($exitCode -eq 0) {
        $statuses['Restore'] = 'PASS'
        foreach ($target in $targets) {
            $result = Invoke-DotNet 'Build' @('build', $target, '-c', $configuration, '--no-restore', '--verbosity', $verbosity)
            if ($result.ExitCode -ne 0) {
                $statuses['Build'] = 'FAIL'; $exitCode = $result.ExitCode
                Add-CommandFailure 'Build' $result
                break
            }
        }
        if ($exitCode -eq 0) { $statuses['Build'] = 'PASS' }
    }

    if ($BuildOnly) { $statuses['Automated tests'] = 'NOT RUN (BuildOnly)' }
    elseif ($exitCode -eq 0) {
        $statuses['Automated tests'] = 'PASS'
        $index = 0
        foreach ($testProject in $testProjects) {
            $index++
            $projectResults = Join-Path $resultsDirectory ([string]$index)
            $arguments = @('test', $testProject.FullName, '-c', $configuration, '--no-build', '--no-restore',
                '--results-directory', $projectResults, '--logger', 'trx;LogFileName=results.trx', '--verbosity', $verbosity)
            if ($verboseOutput) { $arguments += @('--logger', 'console;verbosity=detailed') }
            $result = Invoke-DotNet "Tests: $($testProject.BaseName)" $arguments
            if ($result.ExitCode -ne 0) {
                $statuses['Automated tests'] = 'FAIL'
                if ($exitCode -eq 0) { $exitCode = $result.ExitCode }
                Add-CommandFailure $testProject.BaseName $result
            }
            try {
                $trxPath = Join-Path $projectResults 'results.trx'
                if (-not (Test-Path -LiteralPath $trxPath)) { throw 'No current TRX file was produced.' }
                [xml]$trx = Get-Content -LiteralPath $trxPath -Raw -Encoding UTF8
                $counters = $trx.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
                if ($null -eq $counters) { throw 'The current TRX file contains no counters.' }
                $runPassed = Get-Counter $counters 'passed'
                $runSkipped = Get-Counter $counters 'notExecuted'
                $runTotal = Get-Counter $counters 'total'
                $runExecuted = Get-Counter $counters 'executed'
                $runFailed = $runTotal - $runPassed - $runSkipped
                if ($runFailed -lt 0 -or $runTotal -lt 0 -or $runPassed -lt 0 -or $runSkipped -lt 0 -or
                    $runExecuted -lt 0 -or $runExecuted -gt $runTotal) { throw 'Inconsistent TRX counters.' }
                $parsedRuns++
                $passed += $runPassed; $failed += $runFailed; $skipped += $runSkipped; $executed += $runExecuted
                $outcome = $trx.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='ResultSummary']").GetAttribute('outcome')
                if ($runExecuted -eq 0 -or $runFailed -gt 0 -or $outcome -notin @('Completed', 'Passed')) {
                    $statuses['Automated tests'] = 'FAIL'
                    if ($exitCode -eq 0) { $exitCode = 1 }
                    $failureDetails.Add("$($testProject.BaseName): $runExecuted executed, $runFailed failed, $runSkipped skipped; outcome $outcome.")
                }
                foreach ($failure in $trx.SelectNodes("//*[local-name()='UnitTestResult'][@outcome!='Passed' and @outcome!='NotExecuted']")) {
                    $failureDetails.Add("Test: $($failure.GetAttribute('testName')) [$($failure.GetAttribute('outcome'))]")
                    foreach ($message in $failure.SelectNodes(".//*[local-name()='Message' or local-name()='StackTrace']")) {
                        $failureDetails.Add((Protect-Text $message.InnerText))
                    }
                }
            } catch {
                $statuses['Automated tests'] = 'FAIL'
                if ($exitCode -eq 0) { $exitCode = 1 }
                $failureDetails.Add("$($testProject.BaseName): result validation failed: $($_.Exception.Message)")
            }
        }
    }
} catch {
    if ($exitCode -eq 0) { $exitCode = 1 }
    $failureDetails.Add((Protect-Text $_.Exception.Message))
} finally {
    if ($pushedLocation) { Pop-Location }
    if ($null -ne $logWriter) { $logWriter.Dispose() }
}

# A concurrent run must not replace the active run's log/summary or clipboard.
if ($null -eq $runLock) {
    Write-Host (New-Summary)
    exit $exitCode
}
try {
    if ($CopySummary) {
        $clipboardStatus = 'PASS'
        try { Set-Clipboard -Value (New-Summary) -ErrorAction Stop }
        catch {
            $clipboardStatus = 'FAIL'
            if ($exitCode -eq 0) { $exitCode = 1 }
            $failureDetails.Add("Clipboard copy failed: $($_.Exception.Message)")
        }
    }
    $summary = New-Summary
    [System.IO.File]::WriteAllText($summaryPath, $summary, (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::AppendAllText($logPath, [Environment]::NewLine + $summary, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host $summary
} catch {
    if ($exitCode -eq 0) { $exitCode = 1 }
    $failureDetails.Add("Could not save verification summary: " + (Protect-Text $_.Exception.Message))
    Write-Host (New-Summary)
} finally { $runLock.Dispose() }
exit $exitCode
