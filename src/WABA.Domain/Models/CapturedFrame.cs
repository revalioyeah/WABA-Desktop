namespace WABA.Domain.Models;

/// <summary>
/// A 32-bit BGRA frame captured from the visible client area of a window.
/// </summary>
public sealed record CapturedFrame(
    int Width,
    int Height,
    int Stride,
    byte[] BgraPixels,
    DateTimeOffset CapturedAt);
