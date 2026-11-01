#requires -Version 7.4
[CmdletBinding()]
param(
    [string]$VmId, [string]$SnapshotId, [string]$SourceRoot, [string]$ArtifactDirectory,
    [string]$ManifestPath, [string]$ExpectedSourceCommit, [string]$ExpectedManifestSha256,
    [string]$ExpectedRuntimeVersion = '10.0.13'
)

# Read-only: no harness/test assembly load or launch, network probe, provider/key
# lookup, registry write, account/ACL modification, file creation or remediation.
function Get-GateEArtifactNames {
    @('GateE.NativeAcceptance.exe', 'GateE.NativeAcceptance.dll', 'GateE.NativeAcceptance.deps.json',
      'GateE.NativeAcceptance.runtimeconfig.json', 'PeripheralBatteryCompanion.Core.dll',
      'PeripheralBatteryCompanion.HyperXHelperProtocol.dll', 'Microsoft.Data.Sqlite.dll',
      'SQLitePCLRaw.batteries_v2.dll', 'SQLitePCLRaw.core.dll', 'SQLitePCLRaw.provider.e_sqlite3.dll',
      'runtimes/win-x64/native/e_sqlite3.dll')
}
function Test-GateEPath([string]$Path) {
    if ([string]::IsNullOrEmpty($Path) -or $Path.Length -gt 220 -or $Path -cnotmatch '^[A-Z]:\\' -or
        $Path.Contains('/') -or $Path.Substring(2).Contains(':') -or $Path.EndsWith('\')) { return $false }
    foreach ($part in $Path.Substring(3).Split('\')) {
        if ($part.Length -eq 0 -or $part -in @('.', '..') -or $part -match '[\x00-\x1f<>"|?*]' -or
            $part.EndsWith('.') -or $part.EndsWith(' ') -or $part.Split('.')[0] -match '^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])$') { return $false }
    }
    return [IO.Path]::GetFullPath($Path) -ceq $Path
}
function Test-GateEOwnerDescriptor([byte[]]$Bytes, [string]$Sid, [bool]$Directory) {
    try {
        $d = [Security.AccessControl.RawSecurityDescriptor]::new($Bytes, 0)
        if ($d.Owner.Value -cne $Sid -or -not $d.ControlFlags.HasFlag([Security.AccessControl.ControlFlags]::DiscretionaryAclPresent) -or
            $null -eq $d.DiscretionaryAcl -or $d.DiscretionaryAcl.Count -ne 1 -or
            ($Directory -and -not $d.ControlFlags.HasFlag([Security.AccessControl.ControlFlags]::DiscretionaryAclProtected))) { return $false }
        $a = $d.DiscretionaryAcl[0]
        return $a -is [Security.AccessControl.CommonAce] -and -not $a.IsCallback -and
            $a.AceQualifier -eq [Security.AccessControl.AceQualifier]::AccessAllowed -and $a.SecurityIdentifier.Value -ceq $Sid -and
            $a.AccessMask -eq [int][Security.AccessControl.FileSystemRights]::FullControl -and
            -not $a.AceFlags.HasFlag([Security.AccessControl.AceFlags]::InheritOnly)
    } catch { return $false }
}
function Read-GateEManifest([string]$Json) {
    if ([Text.Encoding]::UTF8.GetByteCount($Json) -gt 16384) { throw 'ManifestBound' }
    $doc = [Text.Json.JsonDocument]::Parse($Json, [Text.Json.JsonDocumentOptions]@{ MaxDepth = 4 })
    try {
        function Test-GateEJsonObject($node) {
            if ($node.ValueKind -ne [Text.Json.JsonValueKind]::Object) { throw 'ManifestObject' }
            $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($p in $node.EnumerateObject()) {
                if (-not $names.Add($p.Name)) { throw 'DuplicateField' }
                if ($p.Value.ValueKind -eq [Text.Json.JsonValueKind]::Object) { Test-GateEJsonObject $p.Value }
            }
        }
        Test-GateEJsonObject $doc.RootElement
        $m = ConvertFrom-Json -InputObject $Json -AsHashtable -ErrorAction Stop
        # Newer PowerShell converts ISO strings to DateTime automatically. Validate the
        # original JSON types/bytes, not their culture-dependent converted representation.
        $m.Schema=$doc.RootElement.GetProperty('Schema').GetInt32()
        foreach ($key in @('NotBeforeUtc','ExpiresUtc','CertificateNotBeforeUtc','CertificateNotAfterUtc')) {
            $m[$key]=$doc.RootElement.GetProperty($key).GetString()
        }
        $fields = @('Schema','RunId','IdentityId','AccountSid','LocalAppData','Executable','ReviewedCommit','Artifacts',
            'NotBeforeUtc','ExpiresUtc','CertificateNotBeforeUtc','CertificateNotAfterUtc')
        if ($m.Count -ne $fields.Count -or @($m.Keys | Where-Object { $_ -cnotin $fields }).Count -ne 0 -or $m.Schema -ne 1) { throw 'ManifestFields' }
        foreach ($key in @('RunId','IdentityId')) {
            $id = [guid]::Empty
            if ($m[$key] -isnot [string] -or -not [guid]::TryParseExact($m[$key], 'D', [ref]$id) -or $id -eq [guid]::Empty -or $id.ToString('D') -cne $m[$key]) { throw 'ManifestUuid' }
        }
        if ($m.RunId -ceq $m.IdentityId -or $m.AccountSid -cnotmatch '^S-1-5-21-(?:[0-9]{1,10}-){2}[0-9]{1,10}-[0-9]{1,10}$' -or
            $m.ReviewedCommit -cnotmatch '^[0-9a-f]{40}$' -or -not (Test-GateEPath $m.LocalAppData) -or -not (Test-GateEPath $m.Executable) -or
            [IO.Path]::GetFileName($m.Executable) -cne 'GateE.NativeAcceptance.exe') { throw 'ManifestBinding' }
        $names = @(Get-GateEArtifactNames)
        if ($m.Artifacts -isnot [Collections.IDictionary] -or $m.Artifacts.Count -ne 11 -or @($m.Artifacts.Keys | Where-Object { $_ -cnotin $names }).Count -ne 0) { throw 'ManifestArtifacts' }
        foreach ($hash in $m.Artifacts.Values) { if ($hash -isnot [string] -or $hash -cnotmatch '^[0-9a-f]{64}$') { throw 'ManifestHash' } }
        $times = @{}
        foreach ($key in @('NotBeforeUtc','ExpiresUtc','CertificateNotBeforeUtc','CertificateNotAfterUtc')) {
            $value = [DateTimeOffset]::MinValue
            if (-not [DateTimeOffset]::TryParseExact($m[$key], "yyyy-MM-dd'T'HH:mm:ss'Z'", [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$value)) { throw 'ManifestUtc' }
            $times[$key] = $value
        }
        if ($times.ExpiresUtc -le $times.NotBeforeUtc -or ($times.ExpiresUtc - $times.NotBeforeUtc).TotalHours -gt 1 -or
            $times.CertificateNotBeforeUtc -gt $times.NotBeforeUtc -or $times.CertificateNotAfterUtc -le $times.ExpiresUtc -or
            ($times.CertificateNotAfterUtc - $times.CertificateNotBeforeUtc).TotalHours -gt 24) { throw 'ManifestLifetime' }
        $root = Join-Path (Join-Path $m.LocalAppData 'PBC.GateE.Acceptance') $m.RunId
        if ($root.Length -gt 180 -or -not (Test-GateEPath $root)) { throw 'ManifestRoot' }
        return $m
    } finally { $doc.Dispose() }
}
function Invoke-GateEEnvironmentPreflight {
    param([hashtable]$Inputs)
    $rows = [Collections.Generic.List[object]]::new()
    function Add-Check([string]$Id, [string]$Status, [string]$Detail) { $rows.Add([ordered]@{ Id=$Id; Status=$Status; Detail=$Detail }) }
    $bindings = [ordered]@{}
    foreach ($key in @('VmId','SnapshotId','SourceRoot','ArtifactDirectory','ManifestPath','ExpectedSourceCommit','ExpectedManifestSha256')) {
        if ([string]::IsNullOrWhiteSpace($Inputs[$key])) { Add-Check $key 'MISSING' ('Owner must bind ' + $key) }
    }
    if (-not $Inputs.ExpectedRuntimeVersion) { $Inputs.ExpectedRuntimeVersion='10.0.13' }
    if ($Inputs.ExpectedSourceCommit -and $Inputs.ExpectedSourceCommit -cnotmatch '^[0-9a-f]{40}$') { Add-Check 'ExpectedSourceCommit' 'FAIL' 'Requires full lowercase 40-hex reviewed commit' }
    if ($Inputs.ExpectedManifestSha256 -and $Inputs.ExpectedManifestSha256 -cnotmatch '^[0-9a-f]{64}$') { Add-Check 'ExpectedManifestSha256' 'FAIL' 'Requires independently recorded lowercase 64-hex SHA-256' }
    if ($Inputs.ExpectedRuntimeVersion -cnotmatch '^10\.[0-9]+\.[0-9]+$') { Add-Check 'ExpectedRuntimeVersion' 'FAIL' 'Requires pinned .NET 10 version only' }
    # No inspection of the current machine when no target VM/snapshot was selected.
    if ($rows.Count -eq 0) {
        try {
            if (-not [OperatingSystem]::IsWindows() -or -not [Environment]::Is64BitProcess) { throw 'WindowsX64PowerShellRequired' }
            $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
            try {
                $sid = $identity.User.Value
                $groupSids = @($identity.Groups | ForEach-Object { $_.Value })
                if ($groupSids -contains 'S-1-5-32-544') { Add-Check 'StandardToken' 'FAIL' 'Administrator SID present, including deny-only/UAC membership' }
                else { Add-Check 'StandardToken' 'OBSERVED' 'No Administrators SID in current logon token; image/account membership attestation still required' }
            } finally { $identity.Dispose() }
            $local = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
            $bindings.AccountSid=$sid; $bindings.LocalAppData=$local; $bindings.VmId=$Inputs.VmId; $bindings.SnapshotId=$Inputs.SnapshotId
            $system = Get-CimInstance Win32_ComputerSystem -ErrorAction Stop
            Add-Check 'Domain' $(if ($system.PartOfDomain) {'FAIL'} else {'OBSERVED'}) 'Requires disposable workgroup VM; domain membership must be absent'
            Add-Check 'Memory' $(if ($system.TotalPhysicalMemory -ge 4GB) {'OBSERVED'} else {'FAIL'}) 'Minimum 4 GiB guest RAM'
            Add-Check 'Cpu' $(if ($system.NumberOfLogicalProcessors -ge 2) {'OBSERVED'} else {'FAIL'}) 'Minimum two logical processors'
            $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
            $bindings.OsVersion=$os.Version; $bindings.OsBuild=$os.BuildNumber; $bindings.Architecture='x64'
            Add-Check 'OsBaseline' 'OWNER_REVIEW' 'Compare exact OS/image hash to approved snapshot; prior measured Windows build was 26200'
            $active = @([Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces() | Where-Object {
                $_.NetworkInterfaceType -ne [Net.NetworkInformation.NetworkInterfaceType]::Loopback -and $_.OperationalStatus -eq [Net.NetworkInformation.OperationalStatus]::Up })
            Add-Check 'GuestNetwork' $(if ($active.Count -eq 0) {'OBSERVED'} else {'FAIL'}) 'Requires zero active non-loopback interfaces; this is not proof of host-side isolation'
            $hosts = @(Get-Process -Name 'testhost','GateE.NativeAcceptance' -ErrorAction SilentlyContinue)
            Add-Check 'OtherRunners' $(if ($hosts.Count -eq 0) {'OBSERVED'} else {'FAIL'}) 'Requires no concurrent testhost or acceptance process; nothing is terminated'
            $runtime = Join-Path $env:ProgramFiles ('dotnet\shared\Microsoft.NETCore.App\' + $Inputs.ExpectedRuntimeVersion)
            Add-Check 'Runtime' $(if (Test-Path -LiteralPath $runtime -PathType Container) {'OBSERVED'} else {'MISSING'}) ('Bind installed x64 .NET runtime ' + $Inputs.ExpectedRuntimeVersion + '; directory presence only, no executable run')
            foreach ($p in @($Inputs.SourceRoot,$Inputs.ArtifactDirectory,$Inputs.ManifestPath)) {
                if (-not (Test-GateEPath $p)) { throw 'NoncanonicalInputPath' }
                $part = [IO.Path]::GetPathRoot($p)
                foreach ($segment in $p.Substring(3).Split('\')) {
                    $part = Join-Path $part $segment
                    if ((Get-Item -LiteralPath $part -Force -ErrorAction Stop).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'ReparseInputPath' }
                }
                $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($p))
                if ($drive.DriveType -ne [IO.DriveType]::Fixed -or $drive.DriveFormat -cne 'NTFS') { throw 'FixedNtfsRequired' }
                if ($drive.AvailableFreeSpace -lt 8GB) { throw 'EightGiBFreeSpaceRequired' }
            }
            $git = Get-Command git.exe -CommandType Application -ErrorAction Stop
            $head = & $git.Source --no-optional-locks -c core.fsmonitor=false -C $Inputs.SourceRoot rev-parse HEAD 2>$null
            if ($head -cmatch '^[0-9a-f]{40}$') { $bindings.ObservedSourceCommit=$head }
            if ($LASTEXITCODE -ne 0 -or $head -cne $Inputs.ExpectedSourceCommit) { throw 'SourceRevisionMismatch' }
            & $git.Source --no-optional-locks -c core.fsmonitor=false -C $Inputs.SourceRoot diff --no-ext-diff --no-textconv --quiet HEAD -- tools/GateE.NativeAcceptance src/PeripheralBatteryCompanion.Windows src/PeripheralBatteryCompanion.Core
            if ($LASTEXITCODE -ne 0) { throw 'ScopedSourceDirty' }
            & $git.Source --no-optional-locks -c core.fsmonitor=false -C $Inputs.SourceRoot diff --no-ext-diff --no-textconv --cached --quiet HEAD -- tools/GateE.NativeAcceptance src/PeripheralBatteryCompanion.Windows src/PeripheralBatteryCompanion.Core
            if ($LASTEXITCODE -ne 0) { throw 'ScopedIndexDirty' }
            $untracked=@(& $git.Source --no-optional-locks -c core.fsmonitor=false -C $Inputs.SourceRoot ls-files --others --exclude-standard -- tools/GateE.NativeAcceptance src/PeripheralBatteryCompanion.Core src/PeripheralBatteryCompanion.HyperXHelperProtocol)
            if ($LASTEXITCODE -ne 0 -or $untracked.Count -ne 0) { throw 'ScopedUntrackedSource' }
            Add-Check 'Source' 'OBSERVED' 'Exact local HEAD and clean tracked harness/security scopes; no fetch, build or assembly load'
            $file = Get-Item -LiteralPath $Inputs.ManifestPath -Force -ErrorAction Stop
            if ($file.Length -gt 16384) { throw 'ManifestBound' }
            $observedHash=(Get-FileHash -LiteralPath $Inputs.ManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
            $bindings.ObservedManifestSha256=$observedHash
            if ($observedHash -cne $Inputs.ExpectedManifestSha256) { throw 'ManifestFingerprintMismatch' }
            $m = Read-GateEManifest ([IO.File]::ReadAllText($Inputs.ManifestPath))
            if ($m.AccountSid -cne $sid -or $m.LocalAppData -cne $local -or $m.ReviewedCommit -cne $head -or
                $m.Executable -cne (Join-Path $Inputs.ArtifactDirectory 'GateE.NativeAcceptance.exe') -or
                $Inputs.ManifestPath -cne (Join-Path $Inputs.ArtifactDirectory 'execution-manifest.json')) { throw 'LiveManifestBindingMismatch' }
            $bindings.ManifestSha256=$Inputs.ExpectedManifestSha256; $bindings.SourceCommit=$head
            $bindings.RunRoot=Join-Path (Join-Path $local 'PBC.GateE.Acceptance') $m.RunId
            $bindings.KeyName='PBC.Identity.' + $m.IdentityId
            if ((Test-Path -LiteralPath $bindings.RunRoot) -or (Test-Path -LiteralPath ($bindings.RunRoot + '.evidence.json'))) { throw 'RunOrEvidenceAlreadyExists' }
            $privateParent = Join-Path $local 'PBC.GateE.Acceptance'
            if (-not (Test-Path -LiteralPath $privateParent -PathType Container)) { throw 'AcceptanceParentMissingRequiresSeparatePreparationApproval' }
            $part=[IO.Path]::GetPathRoot($privateParent)
            foreach ($segment in $privateParent.Substring(3).Split('\')) {
                $part=Join-Path $part $segment
                if ((Get-Item -LiteralPath $part -Force -ErrorAction Stop).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'AcceptanceParentReparse' }
            }
            $rootDrive=[IO.DriveInfo]::new([IO.Path]::GetPathRoot($privateParent))
            if ($rootDrive.DriveType -ne [IO.DriveType]::Fixed -or $rootDrive.DriveFormat -cne 'NTFS') { throw 'FixedNtfsRequired' }
            if ($rootDrive.AvailableFreeSpace -lt 8GB) { throw 'EightGiBFreeSpaceRequired' }
            $dirs = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            $null=$dirs.Add($privateParent); $null=$dirs.Add($Inputs.ArtifactDirectory)
            $allowed = @(Get-GateEArtifactNames)
            $observed = [Collections.Generic.List[string]]::new()
            function Visit-GateEStage([string]$Directory, [string]$Relative) {
                foreach ($entry in Get-ChildItem -LiteralPath $Directory -Force -ErrorAction Stop) {
                    if ($observed.Count -ge 32 -or $entry.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'StageBoundOrReparse' }
                    $name = if ($Relative) { $Relative + '/' + $entry.Name } else { $entry.Name }; $observed.Add($name)
                    if ($entry.PSIsContainer) {
                        if (@($allowed | Where-Object { $_.StartsWith($name + '/', [StringComparison]::Ordinal) }).Count -eq 0) { throw 'UnexpectedStageDirectory' }
                        $null=$dirs.Add($entry.FullName); Visit-GateEStage $entry.FullName $name
                    } else {
                        if ($name -cnotin $allowed -and $name -cnotin @('execution-manifest.json','GateE.NativeAcceptance.pdb','PeripheralBatteryCompanion.Core.pdb','PeripheralBatteryCompanion.HyperXHelperProtocol.pdb')) { throw 'UnexpectedStageFile' }
                        if ($entry.Length -gt 64MB) { throw 'ArtifactBound' }
                        $acl=Get-Acl -LiteralPath $entry.FullName -ErrorAction Stop
                        if (-not (Test-GateEOwnerDescriptor $acl.GetSecurityDescriptorBinaryForm() $sid $false)) { throw 'FileAclPreparationRequiresSeparateApproval' }
                    }
                }
            }
            Visit-GateEStage $Inputs.ArtifactDirectory ''
            foreach ($name in $allowed) {
                $path=Join-Path $Inputs.ArtifactDirectory $name
                if ((Get-FileHash -LiteralPath $path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant() -cne $m.Artifacts[$name]) { throw 'ArtifactHashMismatch' }
            }
            foreach ($directory in $dirs) {
                $acl=Get-Acl -LiteralPath $directory -ErrorAction Stop
                if (-not (Test-GateEOwnerDescriptor $acl.GetSecurityDescriptorBinaryForm() $sid $true)) { throw 'DirectoryAclPreparationRequiresSeparateApproval' }
            }
            Add-Check 'StageAndManifest' 'OBSERVED' 'Eleven file hashes, bounded inventory, path ACL observations and live bindings match; no loader or provider used'
            $expiry=[DateTimeOffset]::ParseExact($m.ExpiresUtc,"yyyy-MM-dd'T'HH:mm:ss'Z'",[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::AssumeUniversal)
            Add-Check 'Window' $(if ([DateTimeOffset]::UtcNow -ge $expiry) {'FAIL'} else {'OWNER_REVIEW'}) (
                'Review fresh run/certificate window: ' + $m.NotBeforeUtc + ' to ' + $m.ExpiresUtc + '; an expired window needs a newly reviewed packet')
        } catch {
            $safeCodes=@('WindowsX64PowerShellRequired','NoncanonicalInputPath','ReparseInputPath','FixedNtfsRequired','EightGiBFreeSpaceRequired',
                'SourceRevisionMismatch','ScopedSourceDirty','ScopedIndexDirty','ScopedUntrackedSource','ManifestBound','ManifestFingerprintMismatch','ManifestObject','DuplicateField',
                'ManifestFields','ManifestUuid','ManifestBinding','ManifestArtifacts','ManifestHash','ManifestUtc','ManifestLifetime','ManifestRoot',
                'LiveManifestBindingMismatch','RunOrEvidenceAlreadyExists','StageBoundOrReparse','UnexpectedStageDirectory','UnexpectedStageFile','ArtifactBound',
                'FileAclPreparationRequiresSeparateApproval','DirectoryAclPreparationRequiresSeparateApproval','ArtifactHashMismatch',
                'AcceptanceParentMissingRequiresSeparatePreparationApproval','AcceptanceParentReparse')
            $code=$_.Exception.Message
            if ($code -cnotin $safeCodes) { $code='MetadataUnavailable: verify supplied paths, permissions, PowerShell modules and JSON types locally' }
            Add-Check 'Inspection' 'FAIL' $code
        }
    }
    foreach ($id in @('StandardToken','Domain','Memory','Cpu','GuestNetwork','OtherRunners','Runtime','Source','StageAndManifest')) {
        if (@($rows | Where-Object Id -eq $id).Count -eq 0) { Add-Check $id 'NOT_OBSERVED' 'Target binding missing or an earlier read-only check stopped inspection' }
    }
    foreach ($pair in @(
        @('HypervisorIsolation','Owner must attest VM/snapshot/image hash, disconnected vNICs, no shared folders/clipboard/drives/USB, no real data'),
        @('AccountAndFolders','Owner must attest standard local account and existing private artifact/acceptance-parent setup; creation/ACL changes need separate approval'),
        @('FreshKeyNamespace','No CNG/key-existence probe performed; exact fixture-name collision check remains a protected case'),
        @('NativeProperties','Hardlink counts, pinned-handle identity, writable directory flushing, Job behavior, DPAPI/CNG and crash classification NOT TESTED'),
        @('AdmissionRevision','Separate source-only review/authorization for a resource-bound harness-only admission revision is missing'),
        @('NativeExecution','Native execution remains unauthorized even if all read-only observations pass')
    )) { Add-Check $pair[0] 'OWNER_REQUIRED' $pair[1] }
    [ordered]@{ Schema=1; Mode='READ_ONLY_ENVIRONMENT_PREPARATION'; ReadyToExecute=$false;
        ObservedPrerequisites=$(if (@($rows | Where-Object Status -in @('FAIL','MISSING')).Count) {'INCOMPLETE_OR_UNSUITABLE'} else {'OBSERVED_WITH_OWNER_ATTESTATIONS_PENDING'});
        Bindings=$bindings; Checks=$rows.ToArray(); EvidenceLimit='Point-in-time metadata only; no handle pinning, atomicity or native security proof' }
}
if ($MyInvocation.InvocationName -ne '.') {
    $inputValues=@{ VmId=$VmId; SnapshotId=$SnapshotId; SourceRoot=$SourceRoot; ArtifactDirectory=$ArtifactDirectory;
        ManifestPath=$ManifestPath; ExpectedSourceCommit=$ExpectedSourceCommit; ExpectedManifestSha256=$ExpectedManifestSha256;
        ExpectedRuntimeVersion=$ExpectedRuntimeVersion }
    Invoke-GateEEnvironmentPreflight $inputValues | ConvertTo-Json -Depth 8
}
