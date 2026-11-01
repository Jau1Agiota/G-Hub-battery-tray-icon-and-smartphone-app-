#requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# No user-supplied selector, assembly or token. No original W1Offline assembly is run.
if (-not [string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable('PBC_GATE_E_AUTHORIZED'))) {
    throw 'Existing Gate E token detected; refusing without reading its value into evidence or changing it.'
}
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$output = Join-Path $PSScriptRoot 'bin/Debug/net10.0-windows'
$evidence = Join-Path $repo ('test-results/e3-3-gate-b/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
New-Item -ItemType Directory -Path $evidence | Out-Null
$assembly = 'PeripheralBatteryCompanion.GateB.Tests.dll'
if (-not (Test-Path -LiteralPath (Join-Path $output $assembly))) { throw 'Build the dedicated Gate B project first.' }

$methods = @(
    'GateBDenialTests.MissingPlanAndPayload_Deny',
    'GateBDenialTests.ExpiredPlan_Denies',
    'GateBDenialTests.PlanCannotAuthorize',
    'GateBDenialTests.RepeatedDenial_TripsLatch',
    'GateBDenialTests.NativeApprovalAndProvenanceHolds_Deny',
    'W1PreflightPlanTests.PermitOperations_AreInert_ForValidInvalidAndMissingPlans',
    'W1PreflightPlanTests.ReportDestination_RejectsPathsDependentOnCurrentDirectory',
    'W1PreflightPlanTests.SerializeCanonical_MatchesLiteralFifteenFieldBytes_WithoutHashing'
)
$filter = ($methods | ForEach-Object { 'FullyQualifiedName=PeripheralBatteryCompanion.W1Offline.Tests.' + $_ }) -join '|'
$filter | Set-Content (Join-Path $evidence 'allowlist.txt')

function Invoke-ObservedRunner([string]$Label, [string]$Directory, [bool]$Discover) {
    # WMI events are subscribed before launch; unlike a final snapshot, these capture
    # short-lived processes. No test assembly is loaded in this monitoring process.
    $startId = 'GateBStart-' + [Guid]::NewGuid().ToString('N')
    $stopId = 'GateBStop-' + [Guid]::NewGuid().ToString('N')
    $subscriptions = @()
    $records = [System.Collections.Generic.List[object]]::new()
    $live = @{}
    $runner = $null
    $observationMode = 'WmiEventsWithMetadata'
    $traceLimitation = $null
    $traceFailure = $null
    $known = [System.Collections.Generic.HashSet[int]]::new()
    $stopped = [System.Collections.Generic.HashSet[int]]::new()
    try {
        try {
            $subscriptions += Register-CimIndicationEvent -Query 'SELECT * FROM Win32_ProcessStartTrace' -SourceIdentifier $startId
            $subscriptions += Register-CimIndicationEvent -Query 'SELECT * FROM Win32_ProcessStopTrace' -SourceIdentifier $stopId
        } catch {
            $traceFailure = $_.Exception.Message
            foreach ($subscription in $subscriptions) { Unregister-Event -SubscriptionId $subscription.SubscriptionId }
            $subscriptions = @()
            $observationMode = 'CimPollingWithProcessHandles'
            $traceLimitation = 'OS process event subscription unavailable. Polling may miss short-lived descendants; retained handles supply observed process start/exit times.'
        }
        $args = @('vstest', $assembly, ('/TestCaseFilter:' + $filter), '/Logger:console;verbosity=detailed', '/Platform:x64')
        if ($Discover) { $args += '/ListTests' }
        else { $args += @('/Logger:trx;LogFileName=results.trx', ('/ResultsDirectory:' + (Join-Path $evidence $Label))) }
        $args += '/Diag:' + (Join-Path $evidence ($Label + '.vstest.log'))
        # ProcessStartInfo.ArgumentList avoids shell command parsing.
        $info = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
        $info.WorkingDirectory = $Directory
        $info.UseShellExecute = $false
        $info.CreateNoWindow = $true
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        foreach ($arg in $args) { $info.ArgumentList.Add($arg) }
        $runner = [System.Diagnostics.Process]::Start($info)
        [void]$known.Add($runner.Id)
        $stdout = $runner.StandardOutput.ReadToEndAsync()
        $stderr = $runner.StandardError.ReadToEndAsync()
        $deadline = [DateTime]::UtcNow.AddSeconds(60)
        $exitSeen = $null
        do {
            if ($observationMode -eq 'CimPollingWithProcessHandles') {
                $snapshot = @(Get-CimInstance -Query "SELECT ProcessId,ParentProcessId,Name,CreationDate,CommandLine FROM Win32_Process WHERE Name='dotnet.exe' OR Name='testhost.exe'")
                do {
                    $before = $known.Count
                    foreach ($candidate in $snapshot) {
                        $candidateId = [int]$candidate.ProcessId
                        if (($candidateId -eq $runner.Id -or $known.Contains([int]$candidate.ParentProcessId)) -and -not $live.ContainsKey($candidateId)) {
                            try {
                                $handle = [System.Diagnostics.Process]::GetProcessById($candidateId)
                                $null = $handle.Handle
                                $startTime = $handle.StartTime.ToUniversalTime().ToString('O')
                                $live[$candidateId] = $handle
                                [void]$known.Add($candidateId)
                                $records.Add([pscustomobject]@{
                                    Event = 'start'; Pid = $candidateId; ParentPid = [int]$candidate.ParentProcessId
                                    Name = [string]$candidate.Name; StartUtc = $startTime
                                    EventUtc = [DateTime]::UtcNow.ToString('O')
                                    Role = $(if ($candidate.Name -eq 'testhost.exe' -or $candidate.CommandLine -match '(?i)testhost\.dll') { 'testhost' } else { 'runner' })
                                })
                            } catch { throw 'Descendant exited before metadata could be retained; lifetime evidence incomplete.' }
                        }
                    }
                } while ($known.Count -ne $before)
                foreach ($entry in @($live.GetEnumerator())) {
                    if ($entry.Value.HasExited -and $stopped.Add([int]$entry.Key)) {
                        $records.Add([pscustomobject]@{
                            Event = 'stop'; Pid = [int]$entry.Key; Name = 'retained-process-handle'
                            EventUtc = [DateTime]::UtcNow.ToString('O')
                            ExitUtc = $entry.Value.ExitTime.ToUniversalTime().ToString('O')
                            ExitStatus = $entry.Value.ExitCode
                        })
                    }
                }
            }
            foreach ($event in @(Get-Event | Where-Object SourceIdentifier -in @($startId, $stopId))) {
                $native = $event.SourceEventArgs.NewEvent
                $processId = [int]$native.ProcessID
                if ([string]$native.ProcessName -in @('dotnet.exe', 'testhost.exe')) {
                    $isStart = $event.SourceIdentifier -eq $startId
                    $row = [ordered]@{
                        Event = $(if ($isStart) { 'start' } else { 'stop' })
                        Pid = $processId
                        Name = [string]$native.ProcessName
                        EventUtc = [DateTime]::FromFileTimeUtc([long]$native.TIME_CREATED).ToString('O')
                    }
                    if ($isStart) {
                        $row.ParentPid = [int]$native.ParentProcessID
                        $row.StartUtc = $null
                        $row.Role = 'unclassified'
                        try {
                            $handle = [System.Diagnostics.Process]::GetProcessById($processId)
                            $null = $handle.Handle
                            $row.StartUtc = $handle.StartTime.ToUniversalTime().ToString('O')
                            $metadata = Get-CimInstance Win32_Process -Filter "ProcessId=$processId"
                            if ($native.ProcessName -eq 'testhost.exe' -or ($metadata -and $metadata.CommandLine -match '(?i)testhost\.dll')) { $row.Role = 'testhost' }
                            $live[$processId] = $handle
                        } catch { $row.Role = 'exited-before-metadata-query' }
                    } else { $row.ExitStatus = [long]$native.ExitStatus }
                    $records.Add([pscustomobject]$row)
                }
                Remove-Event -EventIdentifier $event.EventIdentifier
            }
            if ($runner.HasExited -and $null -eq $exitSeen) { $exitSeen = [DateTime]::UtcNow }
            if ([DateTime]::UtcNow -gt $deadline) { throw 'Runner deadline exceeded; exit/cleanup unconfirmed. No retry or broad termination authorized.' }
            Start-Sleep -Milliseconds 10
        } while ($null -eq $exitSeen -or ([DateTime]::UtcNow - $exitSeen).TotalSeconds -lt 1)

        $stdout.Result | Set-Content (Join-Path $evidence ($Label + '.stdout.log'))
        $stderr.Result | Set-Content (Join-Path $evidence ($Label + '.stderr.log'))
        $descendants = [System.Collections.Generic.HashSet[int]]::new()
        [void]$descendants.Add($runner.Id)
        do {
            $previous = $descendants.Count
            foreach ($row in $records) {
                if ($row.Event -eq 'start' -and $descendants.Contains($row.ParentPid)) { [void]$descendants.Add($row.Pid) }
            }
        } while ($descendants.Count -ne $previous)
        $owned = @($records | Where-Object { $descendants.Contains($_.Pid) })
        $hosts = @($owned | Where-Object { $_.Event -eq 'start' -and $_.Role -eq 'testhost' })
        $remaining = @(Get-CimInstance Win32_Process | Where-Object {
            $_.Name -eq 'testhost.exe' -or ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match '(?i)testhost\.dll')
        } | Select-Object ProcessId, ParentProcessId, CreationDate)
        $summary = [ordered]@{
            Label = $Label; MonitorPid = $PID; RunnerPid = $runner.Id
            ObservationMode = $observationMode; TraceLimitation = $traceLimitation
            EventSubscriptionFailure = $traceFailure
            RunnerParentPid = $PID; RunnerStartUtc = $runner.StartTime.ToUniversalTime().ToString('O')
            RunnerExitUtc = $runner.ExitTime.ToUniversalTime().ToString('O'); ExitCode = $runner.ExitCode
            Arguments = $args; Events = $owned; RemainingTesthosts = $remaining
        }
        $summary | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $evidence ($Label + '.process.json'))
        if ($runner.ExitCode -ne 0) { throw "$Label runner failed; see evidence." }
        if ($hosts.Count -ne 1) { throw "${Label}: exactly one metadata-observed testhost required; evidence is incomplete." }
        foreach ($hostRow in $hosts) {
            if (-not $hostRow.StartUtc -or -not $live.ContainsKey($hostRow.Pid) -or -not $live[$hostRow.Pid].HasExited -or
                @($owned | Where-Object { $_.Event -eq 'stop' -and $_.Pid -eq $hostRow.Pid }).Count -ne 1) {
                throw "$Label testhost start/termination evidence incomplete."
            }
        }
        if ($remaining.Count -ne 0) { throw "$Label found remaining testhost processes; no cleanup success claimed." }
    } catch {
        ($_.Exception.Message + [Environment]::NewLine + $_.ScriptStackTrace) | Set-Content (Join-Path $evidence ($Label + '.failure.log'))
        throw
    } finally {
        foreach ($subscription in $subscriptions) { Unregister-Event -SubscriptionId $subscription.SubscriptionId -ErrorAction SilentlyContinue }
        foreach ($event in @(Get-Event | Where-Object SourceIdentifier -in @($startId, $stopId))) { Remove-Event -EventIdentifier $event.EventIdentifier }
        foreach ($handle in $live.Values) { $handle.Dispose() }
        if ($runner) { $runner.Dispose() }
    }
}

# Isolated copies; production/source JSON and original build output are never edited.
$copies = @{}
foreach ($mode in @('baseline', 'json-probe')) {
    $destination = Join-Path $evidence $mode
    New-Item -ItemType Directory -Path $destination | Out-Null
    Copy-Item -Path (Join-Path $output '*') -Destination $destination -Recurse
    $copies[$mode] = $destination
}
$jsonPath = Join-Path $copies['json-probe'] 'xunit.runner.json'
$json = Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json
$json.preEnumerateTheories = $true
$json.diagnosticMessages = $true
$json | ConvertTo-Json | Set-Content -LiteralPath $jsonPath

# Byte comparisons do not load assemblies or invoke application hashing. Identical
# binaries/assembly attributes make the JSON-only discovery differential attributable.
$copyEvidence = @(
    foreach ($file in Get-ChildItem -LiteralPath $output -File -Recurse) {
        $relative = [System.IO.Path]::GetRelativePath($output, $file.FullName)
        $sourceBytes = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($file.FullName))
        foreach ($mode in @('baseline', 'json-probe')) {
            $same = $sourceBytes -ceq [Convert]::ToBase64String([System.IO.File]::ReadAllBytes((Join-Path $copies[$mode] $relative)))
            if (-not $same -and -not ($mode -eq 'json-probe' -and $relative -eq 'xunit.runner.json')) { throw 'Unexpected copied-file difference.' }
            [pscustomobject]@{ File = $relative; Copy = $mode; IdenticalToBuildOutput = $same }
        }
    }
)
$copyEvidence | ConvertTo-Json | Set-Content (Join-Path $evidence 'copy-integrity.json')

Invoke-ObservedRunner 'baseline-discovery' $copies['baseline'] $true
Invoke-ObservedRunner 'probe-discovery' $copies['json-probe'] $true
Invoke-ObservedRunner 'allowlisted-execution' $copies['baseline'] $false

$baselineText = Get-Content (Join-Path $evidence 'baseline-discovery.stdout.log')
$probeText = Get-Content (Join-Path $evidence 'probe-discovery.stdout.log')
$baselineCases = @($baselineText | Where-Object { $_ -match '^\s+PeripheralBatteryCompanion\.W1Offline\.Tests\.' })
$probeCases = @($probeText | Where-Object { $_ -match '^\s+PeripheralBatteryCompanion\.W1Offline\.Tests\.' })
if ($baselineCases.Count -ne 8 -or $probeCases.Count -ne 15) { throw 'JSON pre-enumeration differential not demonstrated; inspect discovery evidence.' }
[xml]$trx = Get-Content (Join-Path $evidence 'allowlisted-execution/results.trx') -Raw
$results = @($trx.TestRun.Results.UnitTestResult)
if ($results.Count -ne 15 -or @($results | Where-Object outcome -ne 'Passed').Count -ne 0) { throw 'Expected exactly 15 passed, no skipped or failed.' }
foreach ($result in $results) {
    $definition = @($trx.TestRun.TestDefinitions.UnitTest | Where-Object id -eq $result.testId)
    $method = $definition[0].TestMethod
    if ($methods -notcontains (($method.className -split '\.')[-1] + '.' + $method.name)) { throw 'Unexpected executed method.' }
}
foreach ($methodName in $methods) {
    $expected = if ($methodName -eq 'GateBDenialTests.PlanCannotAuthorize') { 3 }
        elseif ($methodName -eq 'W1PreflightPlanTests.ReportDestination_RejectsPathsDependentOnCurrentDirectory') { 6 }
        else { 1 }
    $fullName = 'PeripheralBatteryCompanion.W1Offline.Tests.' + $methodName
    $actual = @($results | Where-Object { $_.testName -eq $fullName -or $_.testName.StartsWith($fullName + '(') }).Count
    if ($actual -ne $expected) { throw 'Allowlisted method case-count mismatch.' }
}
[ordered]@{ Passed = 15; Failed = 0; Skipped = 0; BaselineDiscovered = 8; ProbeDiscovered = 15; EvidenceDirectory = $evidence } |
    ConvertTo-Json | Set-Content (Join-Path $evidence 'summary.json')
Write-Output "15 passed, 0 failed, 0 skipped; JSON discovery differential 8 -> 15. Evidence: $evidence"
