namespace WABA.Domain.Models;

/// <summary>
/// A visual shelf-label match. X/Y are estimated product tap coordinates
/// relative to the captured game client area; they are not an input command.
/// </summary>
public sealed record ShelfItemDetection(
    string ProductName,
    int ClientX,
    int ClientY,
    string Area,
    string RawLabel,
    double Confidence);

public sealed record ShelfDetectionResult(
    IReadOnlyList<ShelfItemDetection> Items,
    TimeSpan Duration,
    string OcrLanguage,
    string Diagnostics);
