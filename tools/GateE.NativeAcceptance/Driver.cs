using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PeripheralBatteryCompanion.NativeAcceptance;

internal sealed record ProcessObservation(int Pid, int ParentPid, long StartUtcTicks);
internal sealed record CaseEvidence(string Case, Verdict Outcome, int? Pid = null, int? ParentPid = null, long? StartUtcTicks = null,
    long? ExitUtcTicks = null, int? ExitCode = null, bool TerminationRequested = false, bool ExitConfirmed = false, int? ErrorCode = null, string ErrorKind = "None");
internal interface IOwnedChild : IDisposable
{
    ProcessObservation Observation { get; }
    Task<string?> ReadLine(CancellationToken stop);
    void TerminateOwned();
    Task<bool> WaitStopped(TimeSpan timeout);
    int ExitCode { get; }
    long ExitUtcTicks { get; }
}
internal interface ICaseLauncher { IOwnedChild Start(CasePlan plan); }

internal sealed class EvidenceLog
{
    private readonly List<CaseEvidence> _cases = [];
    internal IReadOnlyList<CaseEvidence> Cases => _cases;
    internal void Add(CaseEvidence value)
    {
        _ = AcceptancePlan.Find(value.Case);
        if (!Enum.IsDefined(value.Outcome) || value.ErrorKind is not ("None" or "Unavailable" or "HResult" or "Win32"))
            throw new InvalidDataException("Unrecognized evidence code.");
        if (_cases.Count >= 24 || _cases.Any(c => c.Case == value.Case)) throw new InvalidDataException("Evidence bound.");
        _cases.Add(value);
    }
    internal byte[] Bytes()
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { Schema = 1, OS = Environment.OSVersion.Version.ToString(), Runtime = Environment.Version.ToString(),
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(), Provider = "Microsoft Software Key Storage Provider", Cases = _cases,
            WrongUser = "NOT RUN: no second-account manifest", ProcessTrace = "Retained owned handles only; no complete OS process-event trace", Cleanup = "Retain on failed/not-run/uncertain case" });
        if (bytes.Length > 1024 * 1024) throw new InvalidDataException("Evidence byte bound."); return bytes;
    }
}

internal static class ManagedDriver
{
    internal static async Task<EvidenceLog> Run(ICaseLauncher launcher, int expectedParentPid, TimeSpan? deadline = null)
    {
        var evidence = new EvidenceLog(); bool stopped = false, retained = false;
        foreach (var plan in AcceptancePlan.Cases)
        {
            if (stopped || (plan.Id == "cleanup" && retained)) { evidence.Add(new(plan.Id, Verdict.NotRun)); continue; }
            IOwnedChild? child = null; ProcessObservation? observation = null; bool terminated = false, exited = false;
            try
            {
                child = launcher.Start(plan); observation = child.Observation;
                if (observation.ParentPid != expectedParentPid || observation.Pid <= 0 || observation.StartUtcTicks <= 0)
                    throw new InvalidOperationException("Child identity uncertain.");
                using var limit = new CancellationTokenSource(deadline ?? TimeSpan.FromSeconds(30));
                string? line = await child.ReadLine(limit.Token).ConfigureAwait(false);
                if (plan.CrashPhase is not null)
                {
                    if (line != "READY:" + plan.CrashPhase) throw new InvalidDataException("Crash phase absent.");
                    child.TerminateOwned(); terminated = true;
                    exited = await child.WaitStopped(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    if (!exited) throw new InvalidOperationException("Crash exit uncertain.");
                    evidence.Add(new(plan.Id, Verdict.Passed, observation.Pid, observation.ParentPid, observation.StartUtcTicks,
                        child.ExitUtcTicks, child.ExitCode, true, true)); continue;
                }
                exited = await child.WaitStopped(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (!exited) throw new InvalidOperationException("Owned exit uncertain.");
                Verdict outcome = line == "RESULT:Passed" && exited && child.ExitCode == 0 ? Verdict.Passed :
                    line == "RESULT:NotRun" && exited && child.ExitCode == 2 ? Verdict.NotRun : Verdict.Failed;
                var failure = ParseFailure(line);
                evidence.Add(new(plan.Id, outcome, observation.Pid, observation.ParentPid, observation.StartUtcTicks,
                    exited ? child.ExitUtcTicks : null, exited ? child.ExitCode : null, false, exited, failure.Code, failure.Kind));
                if (outcome == Verdict.NotRun || plan.Id == "faults") retained = true;
                if (outcome == Verdict.Failed) stopped = true;
            }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
            {
                if (child is not null && observation?.ParentPid == expectedParentPid && !exited)
                {
                    try { child.TerminateOwned(); terminated = true; exited = await child.WaitStopped(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                    catch (Exception cleanup) when (cleanup is not OutOfMemoryException and not StackOverflowException) { exited = false; }
                }
                evidence.Add(new(plan.Id, Verdict.Uncertain, observation?.Pid, observation?.ParentPid, observation?.StartUtcTicks,
                    exited ? child!.ExitUtcTicks : null, exited ? child!.ExitCode : null, terminated, exited, error.HResult, "HResult")); stopped = true;
            }
            finally { child?.Dispose(); }
        }
        return evidence;
    }
    internal static (int? Code, string Kind) ParseFailure(string? line)
    {
        string[] fields = line?.Split(':') ?? [];
        if (fields.Length == 3 && fields[0] == "ERROR" && fields[1] is "Win32" or "HResult" &&
            int.TryParse(fields[2], System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int value) &&
            value.ToString(System.Globalization.CultureInfo.InvariantCulture) == fields[2]) return (value, fields[1]);
        return (null, "Unavailable");
    }
}

internal sealed class WindowsCaseLauncher(ExecutionManifest manifest, string manifestPath, int parentPid) : ICaseLauncher
{
    private int _started;
    public IOwnedChild Start(CasePlan plan)
    {
        HarnessExecutionHold.Demand();
        if (DateTimeOffset.UtcNow < ExecutionManifest.Utc(manifest.NotBeforeUtc) || DateTimeOffset.UtcNow >= ExecutionManifest.Utc(manifest.ExpiresUtc))
            throw new InvalidOperationException("Launch outside manifest window.");
        if (Interlocked.Increment(ref _started) > 24) throw new InvalidOperationException("Process budget.");
        manifest.VerifyArtifacts();
        var start = new ProcessStartInfo(manifest.Executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(manifest.Executable)! };
        foreach (string argument in new[] { "--child", plan.Id, manifestPath, parentPid.ToString(System.Globalization.CultureInfo.InvariantCulture) }) start.ArgumentList.Add(argument);
        var process = Process.Start(start) ?? throw new InvalidOperationException("Launch uncertain.");
        try
        {
            var owned = new OwnedProcess(process, manifest.Executable, parentPid);
            process.StandardInput.Write("GO\n"); process.StandardInput.Flush(); process.StandardInput.Close();
            return owned;
        }
        catch { process.Dispose(); throw; } // Unknown launch is retained, never a broad cleanup/kill.
    }
    internal static int ParentOfCurrent()
    {
        using var current = Process.GetCurrentProcess();
        if (NtQueryInformationProcess(current.Handle, 0, out var info, Marshal.SizeOf<BasicInfo>(), out _) != 0) throw new InvalidOperationException("Parent observation unavailable.");
        return checked((int)info.Parent.ToInt64());
    }
    private sealed class OwnedProcess : IOwnedChild
    {
        private readonly Process _process;
        private readonly OwnedCaseJob _job;
        private readonly Task _stderr;
        public ProcessObservation Observation { get; }
        internal OwnedProcess(Process process, string expectedExecutable, int expectedParent)
        {
            _process = process; _ = process.Handle; // Retain exact kernel process handle before observing/terminating.
            if (process.MainModule?.FileName != expectedExecutable || NtQueryInformationProcess(process.Handle, 0, out var info, Marshal.SizeOf<BasicInfo>(), out _) != 0 ||
                info.Parent.ToInt64() != expectedParent) throw new InvalidOperationException("Owned process binding failed.");
            Observation = new(process.Id, expectedParent, process.StartTime.ToUniversalTime().Ticks);
            _job = new OwnedCaseJob(process);
            _stderr = DrainBounded(process.StandardError);
        }
        private static async Task DrainBounded(StreamReader reader)
        {
            char[] block = new char[128]; int count = 0, read;
            while ((read = await reader.ReadAsync(block).ConfigureAwait(false)) != 0)
                if ((count += read) > 1024) throw new InvalidDataException("Unexpected child diagnostics.");
        }
        public async Task<string?> ReadLine(CancellationToken stop)
        {
            char[] ch = new char[1]; var result = new System.Text.StringBuilder();
            while (await _process.StandardOutput.ReadAsync(ch.AsMemory(), stop).ConfigureAwait(false) != 0)
            {
                if (ch[0] == '\n') return result.ToString().TrimEnd('\r');
                if (result.Length >= 128) throw new InvalidDataException("Child output bound."); result.Append(ch[0]);
            }
            return null;
        }
        public void TerminateOwned()
        {
            if (_process.Id != Observation.Pid || _process.StartTime.ToUniversalTime().Ticks != Observation.StartUtcTicks)
                throw new InvalidOperationException("Retained process mismatch.");
            _process.Kill(entireProcessTree: false); // NEVER kill arbitrary descendants or names.
        }
        public async Task<bool> WaitStopped(TimeSpan timeout)
        {
            using var limit = new CancellationTokenSource(timeout);
            try { await _process.WaitForExitAsync(limit.Token).ConfigureAwait(false); await _stderr.WaitAsync(limit.Token).ConfigureAwait(false); return _process.HasExited; }
            catch (OperationCanceledException) { return false; }
        }
        public int ExitCode => _process.ExitCode;
        public long ExitUtcTicks => _process.ExitTime.ToUniversalTime().Ticks;
        public void Dispose() { _job.Dispose(); _process.Dispose(); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct BasicInfo { public IntPtr Exit, Peb, Affinity, Priority, Pid, Parent; }
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr process, int kind, out BasicInfo info, int length, out int returned);
}

internal static class Program
{
    internal static int Main(string[] args)
    {
        try { HarnessExecutionHold.Demand(); return RunAdmitted(args).GetAwaiter().GetResult(); }
        catch (Exception error)
        {
            // Typed numeric diagnostics only. Never serialize exception text, stack, headers or buffers.
            if (error is Windows.NativeRecordProtectionFailure { NativeErrorCode: int code }) Console.WriteLine("ERROR:Win32:" + code.ToString(System.Globalization.CultureInfo.InvariantCulture));
            else if (error is System.ComponentModel.Win32Exception win32) Console.WriteLine("ERROR:Win32:" + win32.NativeErrorCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
            else Console.WriteLine("ERROR:HResult:" + error.HResult.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return 77;
        }
    }
    private static async Task<int> RunAdmitted(string[] args)
    {
        if (args.Length is not (2 or 4)) throw new InvalidDataException("Arguments.");
        bool child = args[0] == "--child" && args.Length == 4;
        if (!child && (args[0] != "--manifest" || args.Length != 2)) throw new InvalidDataException("Mode.");
        string path = Path.GetFullPath(args[child ? 2 : 1]);
        if (!Windows.WindowsAuthorizationDirectory.ValidPathSyntax(path) || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Manifest file.");
        using var manifestPin = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (manifestPin.Length > 16384) throw new InvalidDataException("Manifest file bound.");
        byte[] manifestBytes = new byte[checked((int)manifestPin.Length)]; manifestPin.ReadExactly(manifestBytes);
        var manifest = ExecutionManifest.Parse(manifestBytes);
        if (Environment.ProcessPath != manifest.Executable) throw new InvalidDataException("Executable.");
        if (path != Path.Join(Path.GetDirectoryName(manifest.Executable)!, "execution-manifest.json")) throw new InvalidDataException("Manifest must be adjacent to reviewed executable.");
        Windows.WindowsAuthorizationDirectory artifactDirectory;
        IDisposable artifactPins;
        using (NativeAdmission.Enter(manifest, AcceptancePlan.Find("prepare")))
        {
            Windows.WindowsAuthorizationDirectory.VerifyFixtureAccount();
            artifactDirectory = Windows.WindowsAuthorizationDirectory.PinPrivateFixture(Path.GetDirectoryName(manifest.Executable)!);
            try
            {
                artifactDirectory.ValidateFile(manifestPin.SafeFileHandle, path);
                artifactPins = manifest.PinArtifacts((stream, artifact) =>
                {
                    if (Path.GetDirectoryName(artifact) == artifactDirectory.Path) { artifactDirectory.ValidateFile(stream.SafeFileHandle, artifact); return null; }
                    var sub = Windows.WindowsAuthorizationDirectory.PinPrivateFixture(Path.GetDirectoryName(artifact)!);
                    try { sub.ValidateFile(stream.SafeFileHandle, artifact); return sub; } catch { sub.Dispose(); throw; }
                });
            }
            catch { artifactDirectory.Dispose(); throw; }
        }
        using var pinnedArtifactDirectory = artifactDirectory; using var pinnedArtifacts = artifactPins;
        if (child)
        {
            // A child cannot select an arbitrary operation; only the closed case table is accepted.
            if (!int.TryParse(args[3], out int parent) || parent <= 0) throw new InvalidDataException("Parent.");
            using var parentProcess = Process.GetProcessById(parent);
            if (WindowsCaseLauncher.ParentOfCurrent() != parent || parentProcess.MainModule?.FileName != manifest.Executable)
                throw new InvalidDataException("Unbound child process.");
            // Private redirected pipe is only an owned-process start barrier, NEVER native authorization.
            // A metadata/handle binding failure closes the pipe before any protected case can start.
            using var startLimit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            char[] start = new char[3]; int count = 0;
            while (count < 3)
            {
                int read = await Console.In.ReadAsync(start.AsMemory(count, 3 - count), startLimit.Token).ConfigureAwait(false);
                if (read == 0) break; count += read;
            }
            if (count != 3 || new string(start) != "GO\n") throw new InvalidDataException("Owned start barrier absent.");
            var outcome = new NativeCases(manifest, AcceptancePlan.Find(args[1])).Run(); Console.WriteLine("RESULT:" + outcome);
            return outcome == Verdict.Passed ? 0 : 2;
        }
        if (Directory.Exists(manifest.Root) || File.Exists(manifest.Root + ".evidence.json")) throw new InvalidDataException("Run already exists.");
        foreach (var testhost in Process.GetProcessesByName("testhost")) { using (testhost) { throw new InvalidOperationException("Concurrent testhost present."); } }
        Windows.WindowsAuthorizationDirectory parentPins;
        FileStream output;
        using (NativeAdmission.Enter(manifest, AcceptancePlan.Find("prepare")))
        {
            Windows.WindowsAuthorizationDirectory.VerifyFixtureAccount();
            parentPins = Windows.WindowsAuthorizationDirectory.PinPrivateFixture(Path.GetDirectoryName(manifest.Root)!);
            try { output = parentPins.OpenFile(manifest.Root + ".evidence.json", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, FileOptions.WriteThrough); }
            catch { parentPins.Dispose(); throw; }
        }
        using var pinnedParent = parentPins;
        using var evidenceStream = output; // Open before execution; bounded final evidence remains writable after a case deadline.
        var evidence = await ManagedDriver.Run(new WindowsCaseLauncher(manifest, path, Environment.ProcessId), Environment.ProcessId).ConfigureAwait(false);
        evidenceStream.Write(evidence.Bytes()); evidenceStream.Flush(true);
        return evidence.Cases.All(c => c.Outcome == Verdict.Passed) ? 0 : 2;
    }
}
