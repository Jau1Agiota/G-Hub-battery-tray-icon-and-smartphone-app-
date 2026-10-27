namespace PeripheralBatteryCompanion.HyperXBatteryPoc;

public enum ResponseRejection { None, WrongLength, WrongReportId, WrongCommand, InvalidPercentage }

public static class HyperXStatusProtocol
{
    public const int ReportLength = 62;
    public static byte[] CreateStatusRequest() => HyperXHelperProtocol.HelperProtocol.Request();
    public static ResponseRejection Validate(ReadOnlySpan<byte> response, out int? candidatePercentage) =>
        (ResponseRejection)HyperXHelperProtocol.HelperProtocol.ValidateResponse(response, out candidatePercentage);
}
