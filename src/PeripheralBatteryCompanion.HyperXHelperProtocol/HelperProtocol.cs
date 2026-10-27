using System.Text.Json;
using System.Text.Json.Serialization;

namespace PeripheralBatteryCompanion.HyperXHelperProtocol;

public enum HelperMode { Simulate, SimulateHold, NativeOneShot }
public enum HelperStatus { SimulationCompleted, CandidateObservedAmbiguous, Refused, Busy, NoReceiver, AmbiguousReceiver,
    CapabilitiesMismatch, NativeFailure, BufferedInputBeforeRequest, InvalidResponse, Timeout, Cancelled, UnexpectedFailure }
public enum HelperStage { Guard, Enumerate, Inspect, OpenReadHandle, ReadInput, SubmitOutput, Exchange, Cleanup }
public enum HelperProcessObservation { Unknown, NotObserved, Running }
public enum HelperAction { Start, Cancel }
public enum WireRejection { None, WrongLength, WrongReportId, WrongCommand, InvalidPercentage }

public sealed record HelperCommand(int Version, string Nonce, HelperAction Action, bool AcknowledgeRisk = false);
public sealed record HelperReady(int Version, string Nonce, HelperMode Mode, string Build);
public sealed record HelperReply(int Version, string Nonce, HelperMode Mode, HelperStatus Status,
    HelperStage Stage = HelperStage.Guard, HelperProcessObservation Ngenuity = HelperProcessObservation.Unknown,
    bool CapabilitiesValid = false, bool SharedOpenSucceeded = false, bool RequestAttempted = false,
    bool OutputCompleted = false, byte[]? Response = null, int? Win32Error = null, int? HidStatus = null,
    bool NativeCleanupCompleted = false, double ElapsedMilliseconds = 0, bool DeliveryUnknown = false);

public static class HelperProtocol
{
    public const string GlobalQueryGate = @"Global\PeripheralBatteryCompanion.HyperXOneShot";
    public const int Version = 1, MaxFrameChars = 4096;
    public const string Target = "03F0:05B7 / MI_03 / COL01 / FF13:0001; input/output/feature=62/62/0";
    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }, MaxDepth = 4
    };
    public static string Encode<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Decode<T>(string frame)
    {
        if (frame.Length > MaxFrameChars) throw new InvalidDataException("Protocol frame too large");
        return JsonSerializer.Deserialize<T>(frame, Options) ?? throw new InvalidDataException("Missing frame");
    }
    public static bool ValidNonce(string? nonce) => nonce?.Length == 32 && Guid.TryParseExact(nonce, "N", out _);
    public static bool ValidReady(HelperReady ready, string nonce, HelperMode mode) => ready.Version == Version &&
        ready.Nonce == nonce && ready.Mode == mode && !string.IsNullOrEmpty(ready.Build) && ready.Build.Length <= 100 &&
        ready.Build.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+');
    public static bool ValidReply(HelperReply reply, string nonce, HelperMode mode) => reply.Version == Version &&
        reply.Nonce == nonce && reply.Mode == mode && double.IsFinite(reply.ElapsedMilliseconds) && reply.ElapsedMilliseconds >= 0 &&
        (!reply.OutputCompleted || reply.RequestAttempted) &&
        (reply.Response is null || reply.Status == HelperStatus.CandidateObservedAmbiguous && reply.Response.Length == 62) &&
        (reply.Status != HelperStatus.CandidateObservedAmbiguous || mode == HelperMode.NativeOneShot &&
            !reply.DeliveryUnknown && reply.Win32Error is null && reply.HidStatus is null && reply.Ngenuity == HelperProcessObservation.Running && reply.CapabilitiesValid && reply.SharedOpenSucceeded &&
            reply.RequestAttempted && reply.OutputCompleted && reply.Response is not null &&
            ValidateResponse(reply.Response, out _) == WireRejection.None) &&
        (mode == HelperMode.NativeOneShot || !reply.RequestAttempted && !reply.OutputCompleted && reply.Response is null);

    public static byte[] Request() { var bytes = new byte[62]; bytes[0] = 0x66; bytes[1] = 0x89; return bytes; }
    public static WireRejection ValidateResponse(ReadOnlySpan<byte> report, out int? percentage)
    {
        percentage = null;
        if (report.Length != 62) return WireRejection.WrongLength;
        if (report[0] != 0x66) return WireRejection.WrongReportId;
        if (report[1] != 0x89) return WireRejection.WrongCommand;
        if (report[4] > 100) return WireRejection.InvalidPercentage;
        percentage = report[4]; return WireRejection.None;
    }
    public static async Task<string?> ReadFrameAsync(TextReader reader, CancellationToken token)
    {
        var buffer = new char[MaxFrameChars]; var one = new char[1]; var length = 0;
        while (await reader.ReadAsync(one.AsMemory(), token) != 0)
        {
            if (one[0] == '\n') return new string(buffer, 0, length).TrimEnd('\r');
            if (length == buffer.Length) throw new InvalidDataException("Protocol frame too large");
            buffer[length++] = one[0];
        }
        if (length != 0) throw new InvalidDataException("Truncated frame");
        return null;
    }
}
