namespace WABA.Recognition.Models;

/// <summary>
/// One OCR word with its bounding rectangle in the upscaled order-panel crop.
/// Coordinates are retained so product labels and quantities can be associated
/// by their visual row instead of depending on Windows OCR line grouping.
/// </summary>
public sealed record OcrWordFragment(
    string Text,
    double X,
    double Y,
    double Width,
    double Height)
{
    public double CenterX => X + Width / 2d;
    public double CenterY => Y + Height / 2d;
}
