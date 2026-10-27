namespace PeripheralBatteryCompanion.HyperXBatteryPoc;

public enum ResponseRejection { None, WrongLength, WrongReportId, WrongCommand, InvalidPercentage }

public static class HyperXStatusProtocol
{
    public const int ReportLength = 62;
    public static byte[] CreateStatusRequest()
    {
        var bytes = new byte[ReportLength];
        bytes[0] = 0x66;
        bytes[1] = 0x89;
        return bytes; // Exactly 60 zero bytes follow the two-byte prefix, including report ID.
    }

    public static ResponseRejection Validate(ReadOnlySpan<byte> response, out int? candidatePercentage)
    {
        candidatePercentage = null;
        if (response.Length != ReportLength) return ResponseRejection.WrongLength;
        if (response[0] != 0x66) return ResponseRejection.WrongReportId;
        if (response[1] != 0x89) return ResponseRejection.WrongCommand;
        if (response[4] > 100) return ResponseRejection.InvalidPercentage;
        candidatePercentage = response[4];
        return ResponseRejection.None; // Bytes 2, 3 and all other fields remain uninterpreted.
    }
}
