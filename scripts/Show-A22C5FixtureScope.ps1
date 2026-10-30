# Pure preview. Does not build, run .NET, inspect stores, create files or modify settings.
[CmdletBinding()]
param()
$operation = [Guid]::NewGuid().ToString('N')
Write-Host 'A2.2C5 scope preview ONLY. No resources created.'
Write-Host "Operation ID: $operation"
Write-Host "Exact key: PBC-A22C5-Fixture-$operation (CurrentUser Microsoft Software Key Storage Provider, ECDSA P-256, no export)."
Write-Host "One 1-hour untrusted leaf: fixture-$operation.invalid, CurrentUser/My only; never Root or LocalMachine. Offline fixture SAN, not production identity."
Write-Host 'One isolated operation directory under LocalApplicationData/PBC-A22C5-Fixtures; owner-only manifest records exact public identifiers and stages. Journal retained for review.'
Write-Host 'Execution will restore/build the dedicated tool, inspect key ACLs, sign a random challenge, attempt in-memory raw private export expecting denial, test duplicate create refusal, then remove ONLY its exact owned key/leaf.'
Write-Host 'Mismatch verification also creates/disposes one in-memory ephemeral comparison key; it is never persisted or exported.'
Write-Host 'No PFX, private data files, DPAPI, bearer tokens, TLS listener, pairing, network/profile/firewall/router changes, accounts or admin elevation.'
Write-Host 'Crash/cleanup failure can leave an orphan. Never rerun the same ID or delete by prefix. Preserve journal locally; stop for review. No automatic recovery/adoption.'
Write-Host 'Cross-user denial and separate-process restart are NOT RUN. Same-user tampering and privileged principals remain limitations.'
Write-Host 'STOP: review scope/source/PR first. If explicitly approved, separately run:'
Write-Host ('.\scripts\Invoke-A22C5Fixture.ps1 -OperationId ' + $operation + ' -Confirmation CREATE-ISOLATED-CURRENTUSER-FIXTURE')
