using PeripheralBatteryCompanion.HyperXBatteryPoc;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class ActiveCoexistenceTests
{
    [Fact]
    public void RequiresSeparateAcknowledgementInEitherOrderAndRejectsEveryMixedMode()
    {
        var pair = new[] { "--active-coexistence", "--acknowledge-coexistence-risk" };
        Assert.Equal(PocMode.ActiveCoexistence, PocPlan.Parse(pair));
        Assert.Equal(PocMode.ActiveCoexistence, PocPlan.Parse(pair.Reverse().ToArray()));
        Assert.Equal(PocMode.Invalid, PocPlan.Parse([pair[0]]));
        Assert.Equal(PocMode.Invalid, PocPlan.Parse([pair[1]]));
        Assert.Equal(PocMode.Invalid, PocPlan.Parse([pair[0], pair[0]]));
        foreach (var extra in new[] { "--read-battery", "--confirm-ngenuity-closed", "--passive-coexistence", "--preview-request", "--timeout" })
        {
            Assert.Equal(PocMode.Invalid, PocPlan.Parse([.. pair, extra]));
            Assert.Equal(PocMode.Invalid, PocPlan.Parse([pair[0], extra]));
            Assert.Equal(PocMode.Invalid, PocPlan.Parse([pair[1], extra]));
        }
    }

    [Fact]
    public void NormalReadGuardRemainsClosedWhileCoexistenceRequiresObservedNgenuity()
    {
        Assert.Equal(AttemptStatus.NgenuityRunning, PocPlan.ActiveGuard(PocMode.ReadBattery, AttemptStatus.NgenuityRunning));
        Assert.Null(PocPlan.ActiveGuard(PocMode.ReadBattery, null));
        Assert.Equal(AttemptStatus.ProcessCheckFailed, PocPlan.ActiveGuard(PocMode.ReadBattery, AttemptStatus.ProcessCheckFailed));
        Assert.Null(PocPlan.ActiveGuard(PocMode.ActiveCoexistence, AttemptStatus.NgenuityRunning));
        Assert.Equal(AttemptStatus.NgenuityNotObserved, PocPlan.ActiveGuard(PocMode.ActiveCoexistence, null));
        Assert.Equal(AttemptStatus.ProcessCheckFailed, PocPlan.ActiveGuard(PocMode.ActiveCoexistence, AttemptStatus.ProcessCheckFailed));
        Assert.NotNull(PocPlan.ActiveGuard(PocMode.Preview, AttemptStatus.NgenuityRunning));
    }

    [Fact]
    public async Task OneCandidateIsAlwaysAmbiguousAndSessionIsDisposed()
    {
        var session = new Session();
        var result = await new BatteryAttempt(new Factory(session)).RunAsync();
        var text = PocFormatter.Format(result, coexistence: true);
        Assert.Equal(AttemptStatus.CandidateReceived, result.Status);
        Assert.Equal(1, session.Sends); Assert.Equal(1, session.Reads); Assert.True(session.Disposed);
        Assert.Contains("Status: CandidateObservedAmbiguous", text);
        Assert.Contains("Response ownership: ambiguous, never uniquely correlated", text);
        Assert.DoesNotContain("Status: CandidateReceived", text);
        Assert.Contains("56%", text); Assert.Contains("66 89 0E E3 38", text);
        Assert.Contains("consistency only", text);
        Assert.Equal(0, PocFormatter.ExitCode(result.Status)); // Observation succeeded, not unique correlation.
    }

    [Theory]
    [InlineData(0, 0x66, 0x89, 56)] [InlineData(5, 0x66, 0x89, 56)]
    [InlineData(61, 0x66, 0x89, 56)] [InlineData(63, 0x66, 0x89, 56)]
    [InlineData(62, 0x65, 0x89, 56)] [InlineData(62, 0x66, 0x8F, 56)]
    [InlineData(62, 0x66, 0x89, 101)]
    public async Task InvalidConcurrentResponseIsRejectedWithoutSecondReadOrSend(int size, byte id, byte command, byte percent)
    {
        var session = new Session { Reply = Reply(size, id, command, percent) };
        var result = await new BatteryAttempt(new Factory(session)).RunAsync();
        Assert.Equal(AttemptStatus.InvalidResponse, result.Status);
        Assert.Null(result.ValidatedResponse); Assert.Null(result.CandidatePercentage);
        Assert.Equal(1, session.Sends); Assert.Equal(1, session.Reads); Assert.True(session.Disposed);
        Assert.DoesNotContain("Candidate battery percentage", PocFormatter.Format(result, true));
    }

    [Theory]
    [InlineData(5)] [InlineData(32)]
    public async Task SharedOpenRefusalDoesNotSendOrClaimAReply(int error)
    {
        var session = new Session();
        var result = await new BatteryAttempt(new Factory(session, error)).RunAsync();
        Assert.Equal(error, result.Win32Error); Assert.Equal(BatteryIoStep.OpenReadHandle, result.ErrorStep);
        Assert.False(result.RequestAttempted); Assert.Equal(0, session.Sends); Assert.Equal(0, session.Reads);
        Assert.Contains($"code={error}", PocFormatter.Format(result, true));
    }

    [Fact]
    public async Task TimeoutCancelsInputDisposesAndDoesNotRepeat()
    {
        var session = new Session { NeverReply = true };
        var result = await new BatteryAttempt(new Factory(session)).RunCoreAsync(TimeSpan.FromMilliseconds(20), default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(AttemptStatus.Timeout, result.Status);
        Assert.True(session.Cancelled); Assert.True(session.Disposed);
        Assert.Equal(1, session.Reads); Assert.Equal(1, session.Sends);
        Assert.Contains("Status: Timeout", PocFormatter.Format(result, true));
        Assert.Equal(TimeSpan.FromSeconds(3), BatteryAttempt.ExchangeTimeout);
    }

    [Fact]
    public async Task CancellationDuringSendDisposesAndDoesNotClaimNonDelivery()
    {
        using var cancellation = new CancellationTokenSource();
        var session = new Session { OnSend = cancellation.Cancel, NeverReply = true };
        var result = await new BatteryAttempt(new Factory(session)).RunAsync(cancellation.Token);
        Assert.Equal(AttemptStatus.Cancelled, result.Status);
        Assert.True(session.Disposed); Assert.True(session.Cancelled); Assert.Equal(1, session.Sends);
        Assert.Contains("does not prove that the device received nothing", PocFormatter.Format(result, true));
        Assert.Contains("Request outcome unknown", PocFormatter.Format(new(AttemptStatus.UnexpectedFailure), true));
    }

    [Fact]
    public void CoexistencePreviewDoesNotTellOperatorToCloseNgenuity()
    {
        var text = PocFormatter.Preview(false, true);
        Assert.Contains("NGENUITY stays open", text);
        Assert.DoesNotContain("Close NGENUITY AND", text);
        Assert.Contains("Close NGENUITY AND", PocFormatter.Preview(false));
        Assert.Equal(62, HyperXStatusProtocol.CreateStatusRequest().Length);
    }

    private static byte[] Reply(int size = 62, byte id = 0x66, byte command = 0x89, byte percentage = 56)
    {
        var bytes = new byte[size];
        if (size >= 5) { bytes[0] = id; bytes[1] = command; bytes[2] = 0x0E; bytes[3] = 0xE3; bytes[4] = percentage; }
        return bytes;
    }
    private sealed class Factory(Session session, int? error = null) : IHyperXBatterySessionFactory
    {
        public IHyperXBatterySession OpenValidatedSession() => error.HasValue
            ? throw new BatteryIoException(BatteryIoStep.OpenReadHandle, error.Value) : session;
    }
    private sealed class Session : IHyperXBatterySession
    {
        private readonly TaskCompletionSource<byte[]> _reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration _registration;
        public byte[] Reply { get; init; } = ActiveCoexistenceTests.Reply();
        public bool NeverReply { get; init; }
        public Action? OnSend { get; init; }
        public bool Disposed, Cancelled;
        public int Reads, Sends;
        public Task<byte[]> ReadOneReportAsync(CancellationToken token)
        {
            Reads++;
            _registration = token.Register(() => { Cancelled = true; _reply.TrySetCanceled(token); });
            return _reply.Task;
        }
        public Task SubmitOneStatusRequestAsync(CancellationToken token)
        {
            Sends++; OnSend?.Invoke();
            if (!NeverReply) _reply.TrySetResult(Reply);
            return Task.CompletedTask;
        }
        public void Dispose() { Disposed = true; _registration.Dispose(); }
    }
}
