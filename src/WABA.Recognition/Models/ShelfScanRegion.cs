using WABA.Domain.Models;

namespace WABA.Recognition.Models;

/// <summary>Percentage-based region containing product shelves or counter products.</summary>
public sealed record ShelfScanRegion(string Name, int XPercent, int YPercent, int WidthPercent, int HeightPercent)
{
    public PixelRegion ToPixels(int frameWidth, int frameHeight)
    {
        if (XPercent < 0 || YPercent < 0 || WidthPercent < 1 || HeightPercent < 1 ||
            XPercent + WidthPercent > 100 || YPercent + HeightPercent > 100)
            throw new ArgumentOutOfRangeException(nameof(ShelfScanRegion), "Area rak berada di luar frame.");

        var x = (int)Math.Round(frameWidth * XPercent / 100d);
        var y = (int)Math.Round(frameHeight * YPercent / 100d);
        var right = (int)Math.Round(frameWidth * (XPercent + WidthPercent) / 100d);
        var bottom = (int)Math.Round(frameHeight * (YPercent + HeightPercent) / 100d);
        x = Math.Clamp(x, 0, frameWidth - 1);
        y = Math.Clamp(y, 0, frameHeight - 1);
        right = Math.Clamp(right, x + 1, frameWidth);
        bottom = Math.Clamp(bottom, y + 1, frameHeight);
        return new PixelRegion(x, y, right - x, bottom - y);
    }
}
