# Small framework-free contract checks for test.ps1. No real SDK/build, hardware or clipboard is used.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path -Parent $PSScriptRoot
$engine = (Get-Process -Id $PID).Path
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('pbc runner checks ' + [guid]::NewGuid().ToString('N'))
$windowsHost = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT
$names = @('PATH', 'PBC_STUB_ENGINE', 'PBC_STUB_SCRIPT', 'PBC_STUB_REPO', 'PBC_STUB_MODE',
    'PBC_STUB_CALLS', 'PBC_STUB_OPTIONS', 'PBC_STUB_CLIPBOARD')
$saved = @{}
foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$originalPath = $env:PATH
$checks = 0

$fakeDotNet = @'
$command = [string]$args[0]
[IO.File]::AppendAllText($env:PBC_STUB_CALLS, ($args -join '|') + [Environment]::NewLine)
$mode = $env:PBC_STUB_MODE
if ($command -eq '--version') {
    if ($mode -eq 'sdk-failure') { Write-Output 'Required SDK unavailable'; exit 145 }
    Write-Output '10.0.402'; exit 0
}
if ($command -eq 'restore' -and $mode -eq 'restore-failure') {
    Write-Output "error: restore failed at $env:PBC_STUB_REPO/feed https://credential@example.test/pkg?token=supersecret"
    exit 23
}
if ($command -eq 'build' -and $mode -eq 'build-failure') { Write-Output 'error CS0000: synthetic build error'; exit 17 }
if ($command -eq 'test') {
    if ($mode -eq 'no-trx') { Write-Output 'No test results produced'; exit 0 }
    $index = [Array]::IndexOf($args, '--results-directory')
    $directory = [string]$args[$index + 1]
    [void][IO.Directory]::CreateDirectory($directory)
    $trxPath = Join-Path $directory 'results.trx'
    if ($mode -eq 'bad-trx') { [IO.File]::WriteAllText($trxPath, '<bad xml'); exit 0 }
    $total = 3; $executed = 2; $passed = 2; $failed = 0; $skipped = 1; $outcome = 'Completed'
    if ($mode -eq 'zero-tests') { $total = 0; $executed = 0; $passed = 0; $skipped = 0 }
    if ($mode -eq 'test-failure') { $passed = 1; $failed = 1; $outcome = 'Failed' }
    $testOutcome = if ($failed -gt 0) { 'Failed' } else { 'Passed' }
    $xml = @"
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
<Results><UnitTestResult testName="Synthetic.Failure" outcome="$testOutcome"><Output><ErrorInfo>
<Message>Expected 45, actual 48</Message><StackTrace>at Synthetic.Failure()</StackTrace>
</ErrorInfo></Output></UnitTestResult></Results>
<ResultSummary outcome="$outcome"><Counters total="$total" executed="$executed" passed="$passed" failed="$failed" notExecuted="$skipped" /></ResultSummary>
</TestRun>
"@
    [IO.File]::WriteAllText($trxPath, $xml)
    if ($mode -eq 'test-failure') { Write-Output 'synthetic assertion failed'; exit 7 }
}
exit 0
'@

$bootstrap = @'
function global:Set-Clipboard {
    [CmdletBinding()]
    param([string]$Value)
    if ($env:PBC_STUB_MODE -eq 'clipboard-failure') { throw 'Synthetic clipboard unavailable' }
    [IO.File]::WriteAllText($env:PBC_STUB_CLIPBOARD, $Value)
}
$options = @{}
foreach ($option in ($env:PBC_STUB_OPTIONS -split ',')) { if ($option) { $options[$option] = $true } }
& (Join-Path $env:PBC_STUB_REPO 'test.ps1') @options
exit $LASTEXITCODE
'@

function Assert-Contains([string]$Text, [string]$Expected) {
    if (-not $Text.Contains($Expected)) { throw "Expected '$Expected' in result:`n$Text" }
}

function Run-Case([string]$Name, [string]$Mode, [int]$ExpectedExit, [string[]]$Options = @()) {
    $env:PATH = $originalPath
    $caseRoot = Join-Path $tempRoot $Name
    $bin = Join-Path $caseRoot 'fake tools'
    $projectDirectory = Join-Path $caseRoot 'tests/Synthetic.Tests'
    [void][IO.Directory]::CreateDirectory($projectDirectory)
    [void][IO.Directory]::CreateDirectory($bin)
    Copy-Item -LiteralPath (Join-Path $repoRoot 'test.ps1') -Destination (Join-Path $caseRoot 'test.ps1')
    [IO.File]::WriteAllText((Join-Path $caseRoot 'PeripheralBatteryCompanion.sln'), 'synthetic solution; never opened by a real SDK')
    $project = Join-Path $projectDirectory 'Synthetic.Tests.csproj'
    [IO.File]::WriteAllText($project, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>')
    if ($Mode -eq 'project-fallback') { Remove-Item -LiteralPath (Join-Path $caseRoot 'PeripheralBatteryCompanion.sln') }
    if ($Mode -eq 'no-projects') { Remove-Item -LiteralPath $project }
    $env:PBC_STUB_REPO = $caseRoot
    $env:PBC_STUB_ENGINE = $engine
    $env:PBC_STUB_SCRIPT = Join-Path $bin 'fake-dotnet.ps1'
    $env:PBC_STUB_MODE = $Mode
    $env:PBC_STUB_OPTIONS = $Options -join ','
    $env:PBC_STUB_CALLS = Join-Path $caseRoot 'calls.txt'
    $env:PBC_STUB_CLIPBOARD = Join-Path $caseRoot 'clipboard.txt'
    [IO.File]::WriteAllText($env:PBC_STUB_SCRIPT, $fakeDotNet)
    $bootstrapPath = Join-Path $caseRoot 'invoke-runner.ps1'
    [IO.File]::WriteAllText($bootstrapPath, $bootstrap)
    if ($windowsHost) {
        $shim = '@echo off' + "`r`n" + '"%PBC_STUB_ENGINE%" -NoProfile -File "%PBC_STUB_SCRIPT%" %*' + "`r`n" + 'exit /b %ERRORLEVEL%'
        [IO.File]::WriteAllText((Join-Path $bin 'dotnet.cmd'), $shim)
    } else {
        $shimPath = Join-Path $bin 'dotnet'
        [IO.File]::WriteAllText($shimPath, '#!/bin/sh' + "`n" + 'exec "$PBC_STUB_ENGINE" -NoProfile -File "$PBC_STUB_SCRIPT" "$@"' + "`n")
        & chmod +x $shimPath
        if ($LASTEXITCODE -ne 0) { throw 'Unable to prepare synthetic dotnet command' }
    }
    if ($Mode -eq 'missing-dotnet') {
        Remove-Item -LiteralPath (Join-Path $bin $(if ($windowsHost) { 'dotnet.cmd' } else { 'dotnet' }))
        $env:PATH = $bin
    } else { $env:PATH = $bin + [IO.Path]::PathSeparator + $originalPath }
    if ($Mode -eq 'no-trx') {
        $stale = Join-Path $caseRoot 'test-results/automated/1'
        [void][IO.Directory]::CreateDirectory($stale)
        [IO.File]::WriteAllText((Join-Path $stale 'results.trx'), '<old-result />')
    }
    # The child starts from a different directory, and the fixture path contains spaces.
    Push-Location -LiteralPath $tempRoot
    try {
        $output = & $engine -NoProfile -File $bootstrapPath 2>&1
        $actualExit = $LASTEXITCODE
    } finally { Pop-Location }
    if ($actualExit -ne $ExpectedExit) { throw "$Name expected exit $ExpectedExit, got $actualExit`n$($output -join "`n")" }
    $summary = [IO.File]::ReadAllText((Join-Path $caseRoot 'test-results/summary.txt'))
    $calls = if (Test-Path -LiteralPath $env:PBC_STUB_CALLS) { [IO.File]::ReadAllText($env:PBC_STUB_CALLS) } else { '' }
    Assert-Contains $summary $(if ($ExpectedExit -eq 0) { 'Overall result: PASS' } else { 'Overall result: FAIL' })
    if ($Mode -eq 'restore-failure') {
        Assert-Contains $summary 'restore failed'
        if ($calls.Contains('build|') -or $calls.Contains('test|')) { throw 'Runner continued after failed restore' }
        if ($summary.Contains($caseRoot) -or $summary.Contains('supersecret') -or $summary.Contains('credential@')) { throw 'Summary did not redact synthetic private details' }
    }
    if ($Mode -eq 'build-failure') { Assert-Contains $summary 'CS0000'; if ($calls.Contains('test|')) { throw 'Tests ran after a failed build' } }
    if ($Mode -eq 'test-failure') { Assert-Contains $summary 'Expected 45, actual 48'; Assert-Contains $summary 'Failed: 1' }
    if ($Mode -eq 'no-trx') { Assert-Contains $summary 'No current TRX file'; if ($summary.Contains('Passed:')) { throw 'Stale counts were reused' } }
    if ($Mode -eq 'zero-tests') { Assert-Contains $summary '0 executed' }
    if ($Options -contains 'Release') { Assert-Contains $calls 'build|'; Assert-Contains $calls '|Release|' }
    if ($Options -contains 'Verbose') { Assert-Contains $calls 'console;verbosity=detailed' }
    if ($Options -contains 'BuildOnly') {
        Assert-Contains $summary 'NOT RUN (BuildOnly)'
        if ($calls.Contains('test|')) { throw 'BuildOnly ran tests' }
    } elseif ($ExpectedExit -eq 0) {
        Assert-Contains $summary 'Passed: 2'; Assert-Contains $summary 'Skipped: 1'
    }
    if ($Options -contains 'CopySummary' -and $Mode -ne 'clipboard-failure') {
        $copied = [IO.File]::ReadAllText($env:PBC_STUB_CLIPBOARD)
        Assert-Contains $copied 'Overall result:'
        if ($Mode -eq 'test-failure') { Assert-Contains $copied 'Expected 45, actual 48' }
    }
    if ($Mode -eq 'clipboard-failure') { Assert-Contains $summary 'Clipboard: FAIL' }
    if ($Mode -eq 'project-fallback') { Assert-Contains $calls 'Synthetic.Tests.csproj' }
    $script:checks++
    Write-Host "PASS: $Name"
}

try {
    [void][IO.Directory]::CreateDirectory($tempRoot)
    Run-Case 'default success' 'success' 0
    Run-Case 'release and clipboard' 'success' 0 @('Release', 'CopySummary')
    Run-Case 'verbose' 'success' 0 @('Verbose')
    Run-Case 'build only' 'success' 0 @('BuildOnly')
    Run-Case 'missing SDK command' 'missing-dotnet' 127
    Run-Case 'unavailable SDK version' 'sdk-failure' 145
    Run-Case 'missing projects' 'no-projects' 1
    Run-Case 'project fallback' 'project-fallback' 0
    Run-Case 'restore failure and redaction' 'restore-failure' 23
    Run-Case 'build failure' 'build-failure' 17
    Run-Case 'test failure copied' 'test-failure' 7 @('CopySummary')
    Run-Case 'zero tests' 'zero-tests' 1
    Run-Case 'missing current results' 'no-trx' 1
    Run-Case 'malformed results' 'bad-trx' 1
    Run-Case 'clipboard unavailable' 'clipboard-failure' 1 @('CopySummary')
    Write-Host "Runner contract checks passed: $checks"
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}
# Negative fixture cases deliberately leave nonzero native exit codes; do not leak one to CI.
exit 0
