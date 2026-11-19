namespace WABA.Domain.Models;

public sealed record OrderScanResult(
    string RawText,
    IReadOnlyList<RecognizedOrderItem> Items,
    string OcrLanguage,
    TimeSpan Duration,
    PixelRegion Region,
    CapturedFrame CroppedFrame);
