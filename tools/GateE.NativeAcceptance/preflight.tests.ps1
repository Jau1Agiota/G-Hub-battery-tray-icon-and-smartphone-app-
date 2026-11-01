#requires -Version 7.4
# Deterministic policy/AST checks only. Does NOT invoke the live VM inspection branch.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'preflight.ps1')
$script:checks=0
function Assert-Check([bool]$Condition, [string]$Name) { if (-not $Condition) { throw ('FAIL: '+$Name) }; $script:checks++ }
function Assert-Rejected([scriptblock]$Action, [string]$Name) {
    $rejected=$false; try { & $Action | Out-Null } catch { $rejected=$true }
    Assert-Check $rejected $Name
}
Assert-Check (Test-GateEPath 'C:\Fixture\Stage') 'canonical path'
foreach ($bad in @('relative','\\server\share','C:\Fixture\..\Stage','C:\Fixture:ads','C:\Fixture\CON','C:\Fixture\trailing.')) {
    Assert-Check (-not (Test-GateEPath $bad)) 'unsafe path rejected'
}
$sid='S-1-5-21-100-200-300-1001'
function Descriptor([string]$Sddl) {
    $d=[Security.AccessControl.RawSecurityDescriptor]::new($Sddl); $b=[byte[]]::new($d.BinaryLength); $d.GetBinaryForm($b,0); return ,$b
}
Assert-Check (Test-GateEOwnerDescriptor (Descriptor "O:${sid}D:P(A;OICI;FA;;;$sid)") $sid $true) 'private descriptor'
Assert-Check (-not (Test-GateEOwnerDescriptor (Descriptor "O:${sid}D:P(A;;FA;;;$sid)(A;;FR;;;WD)") $sid $true)) 'broad descriptor'
Assert-Check (-not (Test-GateEOwnerDescriptor (Descriptor "O:${sid}D:NO_ACCESS_CONTROL") $sid $true)) 'null DACL'
Assert-Check (-not (Test-GateEOwnerDescriptor (Descriptor "O:${sid}D:(A;;FA;;;$sid)") $sid $true)) 'unprotected directory'
$hashes=[ordered]@{}; foreach ($name in Get-GateEArtifactNames) { $hashes[$name]='a'*64 }
$m=[ordered]@{ Schema=1; RunId='11111111-1111-4111-8111-111111111111'; IdentityId='22222222-2222-4222-8222-222222222222';
    AccountSid=$sid; LocalAppData='C:\Fixture\AppData'; Executable='C:\Fixture\Stage\GateE.NativeAcceptance.exe'; ReviewedCommit=('a'*40); Artifacts=$hashes;
    NotBeforeUtc='2026-11-01T12:00:00Z'; ExpiresUtc='2026-11-01T12:30:00Z'; CertificateNotBeforeUtc='2026-11-01T11:00:00Z'; CertificateNotAfterUtc='2026-11-02T11:00:00Z' }
$json=$m | ConvertTo-Json -Depth 4
$parsed=Read-GateEManifest $json
Assert-Check ($parsed.Artifacts.Count -eq 11) 'eleven exact artifacts'
Assert-Rejected { Read-GateEManifest $json.Insert(1,'"Schema":1,') } 'duplicate key'
Assert-Rejected { Read-GateEManifest $json.Insert(1,'"Authorize":true,') } 'no approval switch'
Assert-Rejected { Read-GateEManifest (' '*16385) } 'manifest bound'
Assert-Rejected { Read-GateEManifest $json.Replace('2026-11-01T12:30:00Z','2026-11-02T12:30:00Z') } 'window bound'
Assert-Rejected { Read-GateEManifest $json.Replace('SQLitePCLRaw.core.dll','unreviewed.dll') } 'closed artifact set'
Assert-Rejected { Read-GateEManifest ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'execution-manifest.template.json'))) } 'unbound template denied'
$missing=Invoke-GateEEnvironmentPreflight @{}
Assert-Check (-not $missing.ReadyToExecute) 'preflight never authorizes'
Assert-Check (@($missing.Checks | Where-Object Status -eq 'MISSING').Count -eq 7) 'exact missing bindings'
Assert-Check ($missing.Bindings.Count -eq 0) 'no live inspection with missing target'
$tokens=$null; $errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'preflight.ps1'), [ref]$tokens, [ref]$errors)
Assert-Check ($errors.Count -eq 0) 'PowerShell syntax'
$commands=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.CommandAst]},$true) | ForEach-Object GetCommandName)
foreach ($command in @('Set-Acl','New-LocalUser','Add-LocalGroupMember','Start-Process','Stop-Process','Invoke-Expression','Add-Type','Remove-Item','New-Item','Set-Content','Out-File')) {
    Assert-Check ($command -notin $commands) 'no mutating/native launch command'
}
Write-Output "$script:checks passed; 0 failed. Policy/AST only; live VM inspection NOT RUN."
