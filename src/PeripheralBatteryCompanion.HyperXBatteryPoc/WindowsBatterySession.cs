using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace PeripheralBatteryCompanion.HyperXBatteryPoc;

// The transport cannot accept an arbitrary payload, command, IOCTL, report size or repeat request.
[SupportedOSPlatform("windows")]
internal sealed class WindowsBatterySession : IHyperXBatterySession
{
    private readonly SafeFileHandle _file;
    private readonly ThreadPoolBoundHandle _bound;
    private readonly object _gate = new();
    private readonly HashSet<PendingIo> _pending = [];
    private bool _closing, _sent, _read;
    private PendingIo? _input;

    public WindowsBatterySession(SafeFileHandle file)
    {
        _file = file;
        try { _bound = ThreadPoolBoundHandle.BindHandle(file); }
        catch { file.Dispose(); throw; }
    }

    public Task<byte[]> ReadOneReportAsync(CancellationToken token) => Start(false, token);
    public async Task SubmitOneStatusRequestAsync(CancellationToken token)
    {
        PendingIo input;
        lock (_gate) input = _input ?? throw new InvalidOperationException("Input must be armed before output.");
        await input.SubmissionReturned.WaitAsync(token);
        if (input.Task.IsCompleted)
        {
            await input.Task; // Preserve a read error; never send after already-buffered input.
            throw new AttemptRefusedException(AttemptStatus.BufferedInputBeforeRequest);
        }
        await Start(true, token);
    }

    private Task<byte[]> Start(bool output, CancellationToken token)
    {
        PendingIo operation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (output ? _sent : _read) throw new InvalidOperationException("Only one output and one input are permitted.");
            if (output) _sent = true; else _read = true;
            operation = new(_bound, _file.DangerousGetHandle(), output, OperationFinished);
            _pending.Add(operation);
            if (!output) _input = operation;
        }
        operation.Start(token);
        return operation.Task;
    }

    private void OperationFinished(PendingIo operation)
    {
        lock (_gate)
        {
            _pending.Remove(operation);
            if (_closing && _pending.Count == 0) ReleaseHandles();
        }
    }

    public void Dispose()
    {
        PendingIo[] operations;
        lock (_gate)
        {
            if (_closing) return;
            _closing = true;
            operations = _pending.ToArray();
            if (operations.Length == 0) ReleaseHandles();
        }
        foreach (var operation in operations) operation.Cancel();
        // No unbounded GetOverlappedResult(wait=true). Pending buffers/handles remain owned
        // until their IOCP completion, so timeout cannot free memory still used by the kernel.
    }

    private void ReleaseHandles() { _bound.Dispose(); _file.Dispose(); }

    private sealed unsafe class PendingIo
    {
        private readonly ThreadPoolBoundHandle _bound;
        private readonly IntPtr _file;
        private readonly bool _output;
        private readonly Action<PendingIo> _finished;
        private readonly byte[] _buffer;
        private readonly NativeOverlapped* _overlapped;
        private readonly object _gate = new();
        private readonly TaskCompletionSource<byte[]> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _submissionReturned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task SubmissionReturned => _submissionReturned.Task;
        private CancellationTokenRegistration _registration;
        private CancellationToken _token;
        private bool _submitted, _apiReturned, _nativeCompleted, _released;
        private int _cancelRequested, _cancelCalls;
        private uint _error, _bytes;
        private Exception? _submissionFailure;
        public Task<byte[]> Task => _completion.Task;

        public PendingIo(ThreadPoolBoundHandle bound, IntPtr file, bool output, Action<PendingIo> finished)
        {
            _bound = bound; _file = file; _output = output; _finished = finished;
            _buffer = output ? HyperXStatusProtocol.CreateStatusRequest() : new byte[HyperXStatusProtocol.ReportLength];
            _overlapped = bound.AllocateNativeOverlapped(Completed, this, _buffer);
        }

        public void Start(CancellationToken token)
        {
            _token = token;
            _registration = token.UnsafeRegister(static state => ((PendingIo)state!).Cancel(), this);
            // Even an overlapped API's entry can spend time in a driver. Never submit on the caller thread.
            ThreadPool.QueueUserWorkItem(static state => state.SubmitNative(), this, preferLocal: false);
        }

        private void SubmitNative()
        {
            lock (_gate)
            {
                if (Volatile.Read(ref _cancelRequested) != 0 || _token.IsCancellationRequested)
                {
                    _apiReturned = _nativeCompleted = true;
                    _error = ActiveNativeMethods.ErrorOperationAborted;
                    TryFinish();
                    _submissionReturned.TrySetResult();
                    return;
                }
                _submitted = true;
            }
            bool ok = false;
            int error = 13;
            Exception? failure = null;
            try
            {
                var buffer = Marshal.UnsafeAddrOfPinnedArrayElement(_buffer, 0);
                ok = _output
                    ? ActiveNativeMethods.DeviceIoControl(_file, ActiveNativeMethods.SetOutputReport, buffer,
                        HyperXStatusProtocol.ReportLength, IntPtr.Zero, 0, IntPtr.Zero, _overlapped)
                    : ActiveNativeMethods.ReadFile(_file, buffer, HyperXStatusProtocol.ReportLength, IntPtr.Zero, _overlapped);
                error = Marshal.GetLastWin32Error();
            }
            catch (Exception caught) { failure = caught; }
            lock (_gate)
            {
                _apiReturned = true;
                if (failure is not null || (!ok && error != ActiveNativeMethods.ErrorIoPending))
                {
                    _nativeCompleted = true; // No completion packet for submission failure.
                    _error = (uint)(error == 0 ? 13 : error);
                    _submissionFailure = failure;
                }
                // Immediate success still queues IOCP completion; never free the buffer at API return alone.
                TryFinish();
            }
            _submissionReturned.TrySetResult();
            // A cancellation before the API reached the kernel may have returned ERROR_NOT_FOUND.
            if (Volatile.Read(ref _cancelRequested) != 0) QueueNativeCancel();
        }

        public void Cancel()
        {
            Interlocked.Exchange(ref _cancelRequested, 1);
            _completion.TrySetCanceled(_token); // Deadline is independent of driver cancellation completion.
            QueueNativeCancel();
        }

        private void QueueNativeCancel() => ThreadPool.QueueUserWorkItem(static state => state.CancelNative(), this, preferLocal: false);

        private void CancelNative()
        {
            lock (_gate)
            {
                if (_released || !_submitted || _nativeCompleted) return;
                _cancelCalls++; // Lease the OVERLAPPED while CancelIoEx may use it.
            }
            try { _ = ActiveNativeMethods.CancelIoEx(_file, _overlapped); }
            catch (Exception) { /* Task is already canceled; never free an uncompleted operation as a fallback. */ }
            finally
            {
                lock (_gate) { _cancelCalls--; TryFinish(); }
            }
        }

        private static void Completed(uint error, uint bytes, NativeOverlapped* native)
        {
            var operation = (PendingIo)ThreadPoolBoundHandle.GetNativeOverlappedState(native)!;
            lock (operation._gate)
            {
                operation._nativeCompleted = true;
                operation._error = error;
                operation._bytes = bytes;
                operation.TryFinish();
            }
        }

        private void TryFinish()
        {
            // Callback can race API return/cancellation. All three must stop using the native allocation first.
            if (_released || !_apiReturned || !_nativeCompleted || _cancelCalls != 0) return;
            _released = true;
            _registration.Unregister();
            try
            {
                if (Volatile.Read(ref _cancelRequested) != 0 || _token.IsCancellationRequested) _completion.TrySetCanceled(_token);
                else if (_submissionFailure is not null) _completion.TrySetException(_submissionFailure);
                else if (_error != 0) _completion.TrySetException(new BatteryIoException(
                    _output ? BatteryIoStep.SubmitOutput : BatteryIoStep.ReadInput, unchecked((int)_error)));
                else if (!_output && _bytes > _buffer.Length) _completion.TrySetException(new BatteryIoException(BatteryIoStep.ReadInput, 13));
                else _completion.TrySetResult(_output ? [] : _buffer.AsSpan(0, (int)_bytes).ToArray());
            }
            finally
            {
                _bound.FreeNativeOverlapped(_overlapped);
                _finished(this);
            }
        }
    }
}
