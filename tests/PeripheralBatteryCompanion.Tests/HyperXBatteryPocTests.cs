using System.Reflection;
using System.Runtime.InteropServices;
using PeripheralBatteryCompanion.G305Inspector;
using PeripheralBatteryCompanion.HyperXBatteryPoc;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class HyperXBatteryPocTests
{
    private static byte[] Response(int percentage, byte unknown2 = 0x0F, byte unknown3 = 0x27)
    {
        var bytes = new byte[62];
        bytes[0] = 0x66; bytes[1] = 0x89; bytes[2] = unknown2; bytes[3] = unknown3; bytes[4] = (byte)percentage;
        return bytes;
    }
    private static string PathFor(string hardware = "vid_03f0&pid_05b7&mi_03&col01", string instance = "synthetic") =>
        $@"\\?\hid#{hardware}#{instance}#{{4d1e55b2-f16f-11cf-88cb-001111000030}}";
    private static HidCapabilities Caps() => new(0xFF13, 1, 62, 62, 0, 0, new(0, 0, 0), new(0, 0, 0), new(0, 0, 0));

    [Fact]
    public void OutgoingPayloadIsOnlyCapturedCommandAndNeverSharedMutableState()
    {
        var request = HyperXStatusProtocol.CreateStatusRequest();
        Assert.Equal(62, request.Length);
        Assert.Equal(0x66, request[0]); Assert.Equal(0x89, request[1]);
        Assert.All(request.Skip(2), value => Assert.Equal(0, value));
        request[1] = 0x8F;
        Assert.Equal(0x89, HyperXStatusProtocol.CreateStatusRequest()[1]);
        Assert.Contains("byte 0=66, byte 1=89, bytes 2-61=00", PocFormatter.Preview());
    }

    [Theory]
    [InlineData(64, 0x0F, 0x1B)] [InlineData(66, 0x0F, 0x27)]
    [InlineData(0, 0, 0)] [InlineData(100, 255, 255)]
    public void SuppliedCaptureEvidenceAndPercentageBoundariesParseWithoutGuessingUnknownFields(int value, byte second, byte third)
    {
        Assert.Equal(ResponseRejection.None, HyperXStatusProtocol.Validate(Response(value, second, third), out var candidate));
        Assert.Equal(value, candidate);
    }

    [Theory]
    [InlineData(0)] [InlineData(4)] [InlineData(5)] [InlineData(61)] [InlineData(63)] [InlineData(124)]
    public void OnlyExact62ByteInputReportsAreAccepted(int size)
    {
        var report = new byte[size];
        if (size >= 5) Response(66).AsSpan(0, Math.Min(size, 62)).CopyTo(report);
        Assert.Equal(ResponseRejection.WrongLength, HyperXStatusProtocol.Validate(report, out var candidate));
        Assert.Null(candidate);
    }

    [Theory]
    [InlineData(0x65, 0x89, 66, ResponseRejection.WrongReportId)]
    [InlineData(0x66, 0x8F, 66, ResponseRejection.WrongCommand)]
    [InlineData(0x66, 0x82, 66, ResponseRejection.WrongCommand)]
    [InlineData(0x66, 0x84, 66, ResponseRejection.WrongCommand)]
    [InlineData(0x66, 0x85, 66, ResponseRejection.WrongCommand)]
    [InlineData(0x66, 0x86, 66, ResponseRejection.WrongCommand)]
    [InlineData(0x66, 0x8A, 66, ResponseRejection.WrongCommand)]
    [InlineData(0x66, 0x83, 66, ResponseRejection.WrongCommand)]
    [InlineData(0x66, 0x89, 101, ResponseRejection.InvalidPercentage)]
    [InlineData(0x66, 0x89, 255, ResponseRejection.InvalidPercentage)]
    public void BadHeadersOtherCommandsAndInvalidPercentagesAreRejected(byte id, byte command, byte percentage, ResponseRejection rejection)
    {
        var report = Response(percentage); report[0] = id; report[1] = command;
        Assert.Equal(rejection, HyperXStatusProtocol.Validate(report, out var candidate));
        Assert.Null(candidate);
    }

    [Fact]
    public void SelectionExcludesOtherCollectionsDevicesAndAmbiguousReceivers()
    {
        var selected = PathFor();
        var paths = new[] { PathFor("vid_03f0&pid_05b7&mi_03&col02"),
            PathFor("vid_03f0&pid_05b7&mi_03&col03"), PathFor("vid_046d&pid_c53f&mi_02&col01"), selected, selected.ToUpperInvariant() };
        Assert.Equal(selected, HyperXReceiverSelection.SelectSinglePath(paths));
        Assert.Equal(AttemptStatus.NoReceiver, Assert.Throws<AttemptRefusedException>(() => HyperXReceiverSelection.SelectSinglePath(paths.Take(3))).Status);
        Assert.Equal(AttemptStatus.AmbiguousReceiver, Assert.Throws<AttemptRefusedException>(() =>
            HyperXReceiverSelection.SelectSinglePath([selected, PathFor(instance: "other-synthetic-receiver")])).Status);
    }

    [Fact]
    public void CapabilityGateMatchesOnlyPhysicallyConfirmedUsageAndReportLengths()
    {
        Assert.True(HyperXReceiverSelection.HasExpectedCapabilities(Caps()));
        foreach (var changed in new[] { Caps() with { UsagePage = 0xFF00 }, Caps() with { Usage = 2 },
            Caps() with { InputReportBytes = 63 }, Caps() with { OutputReportBytes = 61 }, Caps() with { FeatureReportBytes = 1 } })
            Assert.False(HyperXReceiverSelection.HasExpectedCapabilities(changed));
    }

    [Fact]
    public void OptInRequiresBothRequestFlagAndExplicitNgenuityClosureConfirmation()
    {
        Assert.Equal(PocMode.Inspect, PocPlan.Parse([]));
        Assert.Equal(PocMode.Preview, PocPlan.Parse(["--preview-request"]));
        Assert.Equal(PocMode.ReadBattery, PocPlan.Parse(["--read-battery", "--confirm-ngenuity-closed"]));
        Assert.Equal(PocMode.ReadBattery, PocPlan.Parse(["--confirm-ngenuity-closed", "--read-battery"]));
        foreach (var args in new[] { new[] { "--read-battery" }, new[] { "--confirm-ngenuity-closed" },
            new[] { "--preview-request", "--read-battery" }, new[] { "--read-battery", "--read-battery" },
            new[] { "--read-battery", "--confirm-ngenuity-closed", "--timeout", "99" }, new[] { "--command", "8F" } })
            Assert.Equal(PocMode.Invalid, PocPlan.Parse(args));
        Assert.True(PocPlan.IsNgenuityName("NGENUITY"));
        Assert.True(PocPlan.IsNgenuityName("HyperX_Ngenuity2Helper"));
        Assert.False(PocPlan.IsNgenuityName("unrelated-synthetic-process"));
    }

    [Fact]
    public async Task OneSendAndOneReadReturnOnlyValidatedCandidateAndDisposeSession()
    {
        var session = new FakeSession();
        var factory = new FakeFactory(session);
        var result = await new BatteryAttempt(factory).RunAsync();
        Assert.Equal(AttemptStatus.CandidateReceived, result.Status);
        Assert.Equal(66, result.CandidatePercentage);
        Assert.Equal(Response(66), result.ValidatedResponse);
        Assert.Equal(new[] { "read", "send", "dispose" }, session.Events);
        Assert.Equal(1, factory.Opens); Assert.Equal(1, session.Sends); Assert.Equal(1, session.Reads);
        Assert.True(result.RequestAttempted); Assert.True(result.OutputCompleted); Assert.True(session.Disposed);
        var report = PocFormatter.Format(result);
        Assert.Contains("Candidate battery percentage: 66%", report);
        Assert.Contains("not yet independently validated", report);
        Assert.Contains("66 89 0F 27 42", report);
    }

    [Fact]
    public async Task AlreadyBufferedInputPreventsAnyRequest()
    {
        var session = new FakeSession { PreBuffered = true };
        var result = await new BatteryAttempt(new FakeFactory(session)).RunAsync();
        Assert.Equal(AttemptStatus.BufferedInputBeforeRequest, result.Status);
        Assert.False(result.RequestAttempted);
        Assert.Equal(0, session.Sends); Assert.True(session.Disposed);
        Assert.Null(result.CandidatePercentage);
    }

    [Fact]
    public async Task InvalidResponseIsNotDumpedOrInterpretedAndDoesNotRetry()
    {
        var invalid = Response(200);
        var session = new FakeSession { Reply = invalid };
        var result = await new BatteryAttempt(new FakeFactory(session)).RunAsync();
        Assert.Equal(AttemptStatus.InvalidResponse, result.Status);
        Assert.Equal(ResponseRejection.InvalidPercentage, result.Rejection);
        Assert.Null(result.CandidatePercentage); Assert.Null(result.ValidatedResponse);
        Assert.Equal(1, session.Sends); Assert.Equal(1, session.Reads);
        Assert.DoesNotContain("Validated 62-byte response:", PocFormatter.Format(result));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task DeadlineCoversBlockedOutputOrMissingInputWithoutRetry(bool blockedOutput)
    {
        var session = new FakeSession { BlockOutput = blockedOutput, NeverReply = true };
        var result = await new BatteryAttempt(new FakeFactory(session)).RunCoreAsync(TimeSpan.FromMilliseconds(20), default)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(AttemptStatus.Timeout, result.Status);
        Assert.True(result.RequestAttempted);
        Assert.Equal(!blockedOutput, result.OutputCompleted);
        Assert.Equal(1, session.Sends); Assert.Equal(1, session.Reads); Assert.True(session.Disposed);
        Assert.True(session.ReadCancelled);
        Assert.Equal(TimeSpan.FromSeconds(3), BatteryAttempt.ExchangeTimeout);
    }

    [Fact]
    public async Task OutputErrorCancelsArmedReadAndPreservesNumericError()
    {
        var session = new FakeSession { OutputFailure = new BatteryIoException(BatteryIoStep.SubmitOutput, 5) };
        var result = await new BatteryAttempt(new FakeFactory(session)).RunAsync();
        Assert.Equal(AttemptStatus.NativeFailure, result.Status);
        Assert.Equal(BatteryIoStep.SubmitOutput, result.ErrorStep); Assert.Equal(5, result.Win32Error);
        Assert.False(result.OutputCompleted); Assert.True(session.ReadCancelled); Assert.True(session.Disposed);
        Assert.Equal(1, session.Sends); Assert.Equal(1, session.Reads);
    }

    [Fact]
    public async Task InputErrorDoesNotResendAndHidStatusIsNotLabeledWin32()
    {
        var session = new FakeSession { InputFailure = new BatteryIoException(BatteryIoStep.ReadInput, 1167) };
        var result = await new BatteryAttempt(new FakeFactory(session)).RunAsync();
        Assert.Equal(AttemptStatus.NativeFailure, result.Status); Assert.Equal(1167, result.Win32Error);
        Assert.True(result.OutputCompleted); Assert.Equal(1, session.Sends); Assert.True(session.Disposed);
        var status = unchecked((int)0xC0110001);
        var factory = new FakeFactory(new()) { OpenFailure = new BatteryIoException(BatteryIoStep.Inspect, status, true) };
        result = await new BatteryAttempt(factory).RunAsync();
        Assert.Null(result.Win32Error); Assert.Equal(status, result.HidStatus);
        Assert.Contains("HID status: Inspect; 0xC0110001", PocFormatter.Format(result));
    }

    [Fact]
    public async Task CallerCancellationStopsAttemptAndPreCanceledCallsDoNotOpen()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var factory = new FakeFactory(new());
        var result = await new BatteryAttempt(factory).RunAsync(cancel.Token);
        Assert.Equal(AttemptStatus.Cancelled, result.Status); Assert.Equal(0, factory.Opens);
        using var during = new CancellationTokenSource();
        var session = new FakeSession { NeverReply = true, OnSend = during.Cancel };
        result = await new BatteryAttempt(new FakeFactory(session)).RunAsync(during.Token);
        Assert.Equal(AttemptStatus.Cancelled, result.Status); Assert.True(session.Disposed); Assert.Equal(1, session.Sends);
    }

    [Theory]
    [InlineData(AttemptStatus.NoReceiver)] [InlineData(AttemptStatus.AmbiguousReceiver)] [InlineData(AttemptStatus.CapabilitiesMismatch)]
    public async Task RefusalsNeverAttemptRequest(AttemptStatus status)
    {
        var session = new FakeSession();
        var factory = new FakeFactory(session) { OpenFailure = new AttemptRefusedException(status) };
        var result = await new BatteryAttempt(factory).RunAsync();
        Assert.Equal(status, result.Status); Assert.False(result.RequestAttempted); Assert.Equal(0, session.Sends);
    }

    [Fact]
    public void ActiveInteropHasOnlyControlOutputReadAndCancellationAndNoArbitraryPayloadApi()
    {
        Assert.Equal((0xBu << 16) | (101u << 2) | 1u, ActiveNativeMethods.SetOutputReport);
        Assert.Equal(0x80000000u, ActiveNativeMethods.GenericRead);
        Assert.Equal(0x40000000u, ActiveNativeMethods.FileFlagOverlapped);
        if (OperatingSystem.IsWindows()) Assert.Equal(IntPtr.Size == 8 ? 32 : 20, Marshal.SizeOf<NativeOverlapped>());
        var imports = typeof(ActiveNativeMethods).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Select(method => method.GetCustomAttribute<DllImportAttribute>()).OfType<DllImportAttribute>()
            .Select(attribute => attribute.EntryPoint).Order().ToArray();
        Assert.Equal(new[] { "CancelIoEx", "DeviceIoControl", "ReadFile" }, imports);
        var send = typeof(IHyperXBatterySession).GetMethod(nameof(IHyperXBatterySession.SubmitOneStatusRequestAsync))!;
        Assert.Equal(typeof(CancellationToken), Assert.Single(send.GetParameters()).ParameterType);
    }

    [Fact]
    public void UnexpectedFailureDoesNotClaimThatNothingWasSent()
    {
        var report = PocFormatter.Format(new(AttemptStatus.UnexpectedFailure));
        Assert.Contains("Request outcome unknown", report);
        Assert.DoesNotContain("Request submission attempted: False", report);
    }

    private sealed class FakeFactory(FakeSession session) : IHyperXBatterySessionFactory
    {
        public int Opens { get; private set; }
        public Exception? OpenFailure { get; init; }
        public IHyperXBatterySession OpenValidatedSession() { Opens++; if (OpenFailure is not null) throw OpenFailure; return session; }
    }

    private sealed class FakeSession : IHyperXBatterySession
    {
        private readonly TaskCompletionSource<byte[]> _reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration _registration;
        public List<string> Events { get; } = [];
        public int Reads { get; private set; }
        public int Sends { get; private set; }
        public bool Disposed { get; private set; }
        public bool ReadCancelled { get; private set; }
        public bool PreBuffered { get; init; }
        public bool NeverReply { get; init; }
        public bool BlockOutput { get; init; }
        public byte[] Reply { get; init; } = Response(66);
        public Exception? OutputFailure { get; init; }
        public Exception? InputFailure { get; init; }
        public Action? OnSend { get; init; }
        public Task<byte[]> ReadOneReportAsync(CancellationToken token)
        {
            Events.Add("read"); Reads++;
            _registration = token.Register(() => { ReadCancelled = true; _reply.TrySetCanceled(token); });
            return PreBuffered ? Task.FromResult(Reply) : _reply.Task;
        }
        public Task SubmitOneStatusRequestAsync(CancellationToken token)
        {
            Events.Add("send"); Sends++; OnSend?.Invoke();
            if (OutputFailure is not null) return Task.FromException(OutputFailure);
            if (InputFailure is not null) _reply.TrySetException(InputFailure);
            else if (!NeverReply) _reply.TrySetResult(Reply);
            return BlockOutput ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task : Task.CompletedTask;
        }
        public void Dispose() { Events.Add("dispose"); Disposed = true; _registration.Dispose(); }
    }
}
