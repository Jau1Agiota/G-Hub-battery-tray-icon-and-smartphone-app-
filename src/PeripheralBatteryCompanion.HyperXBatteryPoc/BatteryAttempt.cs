namespace PeripheralBatteryCompanion.HyperXBatteryPoc;

public enum AttemptStatus
{
    CandidateReceived, Timeout, Cancelled, NoReceiver, AmbiguousReceiver, EnumerationFailed,
    CapabilitiesMismatch, NativeFailure, BufferedInputBeforeRequest, InvalidResponse,
    UnsupportedPlatform, NgenuityRunning, ProcessCheckFailed, Busy, UnexpectedFailure
}
public enum BatteryIoStep { Enumerate, Inspect, OpenReadHandle, ReadInput, SubmitOutput, ProcessCheck }
public sealed class BatteryIoException(BatteryIoStep step, int code, bool isHidStatus = false) : Exception("HID operation failed")
{
    public BatteryIoStep Step { get; } = step;
    public int Code { get; } = code;
    public bool IsHidStatus { get; } = isHidStatus;
}
public sealed class AttemptRefusedException(AttemptStatus status) : Exception("Battery attempt refused")
{
    public AttemptStatus Status { get; } = status;
}
public sealed record BatteryAttemptResult(AttemptStatus Status, bool RequestAttempted = false,
    bool OutputCompleted = false, int? CandidatePercentage = null, byte[]? ValidatedResponse = null,
    ResponseRejection Rejection = ResponseRejection.None, BatteryIoStep? ErrorStep = null, int? Win32Error = null, int? HidStatus = null);

public interface IHyperXBatterySession : IDisposable
{
    Task<byte[]> ReadOneReportAsync(CancellationToken token);
    Task SubmitOneStatusRequestAsync(CancellationToken token);
}
public interface IHyperXBatterySessionFactory
{
    IHyperXBatterySession OpenValidatedSession(); // No output report is sent by this operation.
}

public sealed class BatteryAttempt(IHyperXBatterySessionFactory factory)
{
    public static TimeSpan ExchangeTimeout { get; } = TimeSpan.FromSeconds(3);
    public Task<BatteryAttemptResult> RunAsync(CancellationToken token = default) => RunCoreAsync(ExchangeTimeout, token);

    // Short deterministic deadlines are available only to the test assembly, not CLI/users.
    internal async Task<BatteryAttemptResult> RunCoreAsync(TimeSpan timeout, CancellationToken token)
    {
        bool attempted = false, outputCompleted = false;
        Task? read = null, output = null;
        try
        {
            token.ThrowIfCancellationRequested();
            using var session = factory.OpenValidatedSession();
            token.ThrowIfCancellationRequested();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(timeout);
            try
            {
                // Arm the interrupt read first. Do not flush/drain arbitrary reports.
                var responseTask = session.ReadOneReportAsync(deadline.Token);
                read = responseTask;
                if (responseTask.IsCompleted)
                {
                    await responseTask; // Preserve any immediate API/cancellation error.
                    return new(AttemptStatus.BufferedInputBeforeRequest); // No request sent.
                }
                deadline.Token.ThrowIfCancellationRequested();
                attempted = true;
                output = session.SubmitOneStatusRequestAsync(deadline.Token);
                await output.WaitAsync(deadline.Token);
                outputCompleted = true;
                var response = await responseTask.WaitAsync(deadline.Token);
                deadline.Token.ThrowIfCancellationRequested();
                var rejected = HyperXStatusProtocol.Validate(response, out var percentage);
                return rejected == ResponseRejection.None
                    ? new(AttemptStatus.CandidateReceived, attempted, outputCompleted, percentage, response.ToArray())
                    : new(AttemptStatus.InvalidResponse, attempted, outputCompleted, Rejection: rejected);
            }
            catch (OperationCanceledException)
            {
                return new(token.IsCancellationRequested ? AttemptStatus.Cancelled : AttemptStatus.Timeout, attempted, outputCompleted);
            }
            finally { await deadline.CancelAsync(); } // Also cancel the armed read after an output failure.
        }
        catch (OperationCanceledException) { return new(AttemptStatus.Cancelled, attempted, outputCompleted); }
        catch (AttemptRefusedException error) { return new(error.Status, attempted, outputCompleted); }
        catch (BatteryIoException error) { return new(AttemptStatus.NativeFailure, attempted, outputCompleted, ErrorStep: error.Step, Win32Error: error.IsHidStatus ? null : error.Code, HidStatus: error.IsHidStatus ? error.Code : null); }
        finally
        {
            ObserveLateFault(read);
            ObserveLateFault(output);
        }
    }

    private static void ObserveLateFault(Task? task)
    {
        if (task is not null) _ = task.ContinueWith(completed => { _ = completed.Exception; },
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
