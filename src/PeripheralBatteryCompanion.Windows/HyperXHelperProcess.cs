using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using PeripheralBatteryCompanion.Core;
using PeripheralBatteryCompanion.HyperXHelperProtocol;

namespace PeripheralBatteryCompanion.Windows;

// Only process/pipe/Job APIs live here. No active HID I/O imports or pending report buffers enter the tray process.
internal sealed class HyperXHelperProcessFactory(string? executable = null) : IHyperXHelperProcessFactory
{
    private readonly string _executable = executable ?? Path.Combine(AppContext.BaseDirectory, "hyperx-helper", "PeripheralBatteryCompanion.HyperXHelper.exe");
    private readonly object _starts = new();
    private Task<IHyperXHelperProcess>? _starting;
    public bool IsAvailable => File.Exists(_executable) && _starting is not { IsCompleted: false } && !WindowsHelperChild.HasUnconfirmedExit;
    public Task<IHyperXHelperProcess> StartAsync(HelperMode mode, string nonce, CancellationToken token)
    {
        lock (_starts)
        {
            if (_starting is { IsCompleted: false }) throw new InvalidOperationException("Creation already owned");
            // Keep ownership even if the caller's ready budget expires while CreateProcess is still returning.
            return _starting = Task.Run<IHyperXHelperProcess>(() =>
            {
                token.ThrowIfCancellationRequested();
                if (!HelperProtocol.ValidNonce(nonce) || !File.Exists(_executable)) throw new InvalidOperationException("Helper unavailable");
                return WindowsHelperChild.Start(_executable, mode, nonce, token);
            }, token);
        }
    }
}

internal sealed class WindowsHelperChild : IHyperXHelperProcess
{
    private static readonly object RegistryLock = new();
    private static readonly HashSet<WindowsHelperChild> Registry = [];
    static WindowsHelperChild() => AppDomain.CurrentDomain.ProcessExit += (_, _) =>
    {
        lock (RegistryLock)
            foreach (var child in Registry.ToArray()) child.OwnerExiting();
    };
    public static bool HasUnconfirmedExit
    {
        get { lock (RegistryLock) return Registry.Any(child => child._retained && !child._exit.Task.IsCompleted); }
    }
    private Semaphore? _queryLease;
    private readonly Process _process;
    private readonly SafeProcessHandle _killHandle;
    private bool _jobMember;
    private readonly JobHandle _job;
    private readonly StreamReader _output;
    private readonly StreamWriter _input;
    private readonly SafeFileHandle _errorRead;
    private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private CancellationTokenRegistration _startupCancellation;
    private volatile bool _retained;
    private bool _disposed, _armed;
    public bool Contained { get; private set; }
    public int? StartupWin32Error { get; private set; }
    public int? ExitCode { get; private set; }

    private WindowsHelperChild(Process process, JobHandle job, SafeFileHandle output, SafeFileHandle input, SafeFileHandle errorRead)
    {
        _process = process; _killHandle = process.SafeHandle; _job = job; _errorRead = errorRead;
        _output = new(new FileStream(output, FileAccess.Read), new UTF8Encoding(false, true));
        _input = new(new FileStream(input, FileAccess.Write), new UTF8Encoding(false)) { AutoFlush = true };
        _process.Exited += (_, _) => OnExit();
        _process.EnableRaisingEvents = true;
        if (_process.HasExited) OnExit();
        lock (RegistryLock) Registry.Add(this);
    }

    public static WindowsHelperChild Start(string executable, HelperMode mode, string nonce, CancellationToken token)
    {
        Semaphore? queryLease = null;
        if (mode == HelperMode.NativeOneShot)
        {
            queryLease = new Semaphore(1, 1, HelperProtocol.GlobalQueryGate);
            if (!queryLease.WaitOne(0)) { queryLease.Dispose(); throw new Win32Exception(170); }
        }
        var job = ProcessNative.CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error(); job.Dispose();
            if (queryLease is not null) { queryLease.Release(); queryLease.Dispose(); }
            throw new Win32Exception(error);
        }
        SafeFileHandle? parentInput = null, parentOutput = null, errorRead = null;
        WindowsHelperChild? owned = null;
        try
        {
            var limits = new ProcessNative.ExtendedLimits();
            limits.Basic.Flags = 0x2000 | 0x8; // KILL_ON_JOB_CLOSE | ACTIVE_PROCESS_LIMIT; no breakaway.
            limits.Basic.ActiveProcesses = 1;
            using (var storage = new NativeStorage(Marshal.SizeOf<ProcessNative.ExtendedLimits>()))
            {
                Marshal.StructureToPtr(limits, storage.Pointer, false);
                if (!ProcessNative.SetInformationJobObject(job, 9, storage.Pointer, (uint)storage.Size)) throw new Win32Exception();
            }
            var security = new ProcessNative.Security { Size = Marshal.SizeOf<ProcessNative.Security>(), Inherit = 1 };
            if (!ProcessNative.CreatePipe(out var childInput, out parentInput, ref security, 4096)) throw new Win32Exception();
            using (childInput)
            {
                if (!ProcessNative.CreatePipe(out parentOutput, out var childOutput, ref security, 4096)) throw new Win32Exception();
                using (childOutput)
                {
                    if (!ProcessNative.CreatePipe(out errorRead, out var childError, ref security, 4096)) throw new Win32Exception();
                    using (childError)
                    {
                        foreach (var handle in new[] { parentInput, parentOutput, errorRead })
                            if (!ProcessNative.SetHandleInformation(handle, 1, 0)) throw new Win32Exception();
                        UIntPtr required = 0;
                        _ = ProcessNative.InitializeProcThreadAttributeList(IntPtr.Zero, 2, 0, ref required);
                        if (required == 0 || required.ToUInt64() > 65536) throw new Win32Exception();
                        using var attributes = new NativeStorage((int)required);
                        if (!ProcessNative.InitializeProcThreadAttributeList(attributes.Pointer, 2, 0, ref required)) throw new Win32Exception();
                        try
                        {
                            using var jobs = new NativeStorage(IntPtr.Size);
                            Marshal.WriteIntPtr(jobs.Pointer, job.DangerousGetHandle());
                            // PROC_THREAD_ATTRIBUTE_JOB_LIST attaches atomically during CreateProcess, BEFORE any child instruction.
                            if (!ProcessNative.UpdateProcThreadAttribute(attributes.Pointer, 0, (UIntPtr)0x2000D, jobs.Pointer,
                                (UIntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception();
                            using var handles = new NativeStorage(IntPtr.Size * 3);
                            Marshal.WriteIntPtr(handles.Pointer, 0, childInput.DangerousGetHandle());
                            Marshal.WriteIntPtr(handles.Pointer, IntPtr.Size, childOutput.DangerousGetHandle());
                            Marshal.WriteIntPtr(handles.Pointer, IntPtr.Size * 2, childError.DangerousGetHandle());
                            if (!ProcessNative.UpdateProcThreadAttribute(attributes.Pointer, 0, (UIntPtr)0x20002, handles.Pointer,
                                (UIntPtr)handles.Size, IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception();
                            var startup = new ProcessNative.StartupEx { Attributes = attributes.Pointer };
                            startup.Info.Size = Marshal.SizeOf<ProcessNative.StartupEx>();
                            startup.Info.Flags = 0x100; // STARTF_USESTDHANDLES; exactly three inheritable pipe ends.
                            startup.Info.Input = childInput.DangerousGetHandle(); startup.Info.Output = childOutput.DangerousGetHandle();
                            startup.Info.Error = childError.DangerousGetHandle();
                            var option = mode switch { HelperMode.Simulate => "--simulate", HelperMode.SimulateHold => "--simulate-hold", HelperMode.NativeOneShot => "--serve", _ => throw new InvalidOperationException("Invalid mode") };
                            token.ThrowIfCancellationRequested();
                            if (!ProcessNative.CreateProcess(executable, new StringBuilder($"\"{executable}\" {option} {nonce}"),
                                IntPtr.Zero, IntPtr.Zero, true, 0x08000000 | 0x80000 | 4, IntPtr.Zero,
                                Path.GetDirectoryName(executable)!, ref startup, out var info)) throw new Win32Exception();
                            using var rawProcess = new SafeProcessHandle(info.Process, true);
                            using var thread = new JobHandle(info.Thread);
                            var process = Process.GetProcessById((int)info.ProcessId);
                            try { owned = new(process, job, parentOutput, parentInput, errorRead); }
                            catch { process.Dispose(); throw; }
                            owned._queryLease = queryLease; queryLease = null;
                            parentOutput = parentInput = errorRead = null; // Ownership transferred.
                            var queried = ProcessNative.IsProcessInJob(rawProcess, job, out var member);
                            owned._jobMember = queried && member;
                            owned.Contained = owned._jobMember;
                            if (!owned.Contained) owned.StartupWin32Error = queried ? 13 : Marshal.GetLastWin32Error();
                            var child = owned;
                            owned._startupCancellation = token.UnsafeRegister(_ =>
                            {
                                lock (child._gate)
                                {
                                    if (!child._armed)
                                    {
                                        child._retained = true;
                                        try { child.RequestTermination(); } catch (Win32Exception) { }
                                        if (child._exit.Task.IsCompleted) child.Release();
                                    }
                                }
                            }, null);
                            if (token.IsCancellationRequested)
                            {
                                owned.RetainUntilExit(); owned.RequestTermination(); // Also covers a disposed/cancelled startup source on late return.
                            }
                            else if (owned.Contained)
                            {
                                owned.Contained = ProcessNative.ResumeThread(thread) != uint.MaxValue;
                                if (!owned.Contained) owned.StartupWin32Error = Marshal.GetLastWin32Error();
                            }
                            // Failure remains suspended/unarmed; supervisor records refusal and terminates its owned Job.
                            return owned;
                        }
                        finally { ProcessNative.DeleteProcThreadAttributeList(attributes.Pointer); }
                    }
                }
            }
        }
        catch
        {
            if (owned is not null) { owned.RetainUntilExit(); owned.RequestTermination(); }
            else job.Dispose(); // Atomic Job membership prevents an orphan even on a post-CreateProcess exception.
            if (queryLease is not null) { queryLease.Release(); queryLease.Dispose(); }
            parentInput?.Dispose(); parentOutput?.Dispose(); errorRead?.Dispose();
            throw;
        }
    }
    internal void OwnerExiting()
    {
        if (!_jobMember) { try { RequestTermination(); } catch { } } // Never leave an unarmed, suspended creation failure behind.
        _job.Dispose(); // KILL_ON_JOB_CLOSE, not a claim of observed exit; no child-owned HID memory is freed here.
    }

    public Task<string?> ReadFrameAsync(CancellationToken token) => HelperProtocol.ReadFrameAsync(_output, token);
    public async Task SendAsync(HelperCommand command, CancellationToken token)
    {
        if (command.Action == HelperAction.Start)
        {
            lock (_gate) _armed = true;
            _startupCancellation.Unregister();
        }
        await _input.WriteLineAsync(HelperProtocol.Encode(command).AsMemory(), token);
    }
    public void CloseInput() => _input.Dispose();
    public void RequestTermination()
    {
        if (_exit.Task.IsCompleted) return;
        // A failed membership check never resumes the child; use its exact retained handle to terminate it suspended.
        var requested = _jobMember ? ProcessNative.TerminateJobObject(_job, 0xE0) : ProcessNative.TerminateProcess(_killHandle, 0xE0);
        if (!requested) throw new Win32Exception();
    }
    public async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        try { await _exit.Task.WaitAsync(timeout); return true; }
        catch (TimeoutException) { return false; }
    }
    public void RetainUntilExit()
    {
        lock (_gate) { _retained = true; if (_exit.Task.IsCompleted) Release(); }
    }
    private void OnExit()
    {
        lock (_gate)
        {
            try { ExitCode = _process.ExitCode; } catch { ExitCode = null; }
            _exit.TrySetResult();
            if (_retained) Release();
        }
    }
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_exit.Task.IsCompleted) Release();
            else _retained = true; // Root remains in Registry; ProcessExit closes Job; Exited releases resources.
        }
        return ValueTask.CompletedTask;
    }
    private void Release()
    {
        if (_disposed) return;
        _disposed = true;
        _startupCancellation.Unregister();
        foreach (var resource in new IDisposable[] { _input, _output, _errorRead, _job, _process })
        {
            try { resource.Dispose(); } catch (IOException) { } // Attempt every owned release, including Job/Process, on pipe errors.
        }
        if (_queryLease is not null) { _queryLease.Release(); _queryLease.Dispose(); _queryLease = null; }
        lock (RegistryLock) Registry.Remove(this);
    }
}

internal sealed class JobHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public JobHandle() : base(true) { }
    public JobHandle(IntPtr value) : base(true) => SetHandle(value);
    protected override bool ReleaseHandle() => ProcessNative.CloseHandle(handle);
}
internal sealed class NativeStorage(int size) : IDisposable
{
    public int Size { get; } = size;
    public IntPtr Pointer { get; } = Marshal.AllocHGlobal(size);
    public void Dispose() => Marshal.FreeHGlobal(Pointer);
}
internal static class ProcessNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct BasicLimits
    {
        public long PerProcessTime, PerJobTime; public uint Flags; public UIntPtr MinWorkingSet, MaxWorkingSet;
        public uint ActiveProcesses; public UIntPtr Affinity; public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] internal struct ExtendedLimits
    {
        public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Security { public int Size; public IntPtr Descriptor; public int Inherit; }
    [StructLayout(LayoutKind.Sequential)] internal struct Startup
    {
        public int Size; public IntPtr Reserved, Desktop, Title; public uint X, Y, Width, Height, XChars, YChars, Fill, Flags;
        public ushort Show, ReservedSize; public IntPtr ReservedBytes, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct StartupEx { public Startup Info; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] internal struct ProcessInfo { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern JobHandle CreateJobObject(IntPtr security, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetInformationJobObject(JobHandle job, int type, IntPtr info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TerminateJobObject(JobHandle job, uint code);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TerminateProcess(SafeProcessHandle process, uint code);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref Security security, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref UIntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, UIntPtr attribute, IntPtr value, UIntPtr size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] internal static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateProcess(string application, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity,
        [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, IntPtr environment, string directory, ref StartupEx startup, out ProcessInfo info);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsProcessInJob(SafeProcessHandle process, JobHandle job, [MarshalAs(UnmanagedType.Bool)] out bool result);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint ResumeThread(JobHandle thread);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CloseHandle(IntPtr handle);
}
