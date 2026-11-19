using WABA.Domain.Models;

namespace WABA.Recognition.Models;

/// <summary>
/// Order-board ROI expressed as percentages of the captured game client area.
/// Percent values make the same calibration portable across game window sizes.
/// </summary>
public sealed record OrderPanelRegion(int XPercent, int YPercent, int WidthPercent, int HeightPercent)
{
    public PixelRegion ToPixels(int frameWidth, int frameHeight)
    {
        if (XPercent < 0 || YPercent < 0 ||
            WidthPercent < 1 || HeightPercent < 1 ||
            XPercent + WidthPercent > 100 ||
            YPercent + HeightPercent > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(OrderPanelRegion),
                "ROI harus berada di dalam gambar: X/Y 0-99, lebar/tinggi minimal 1%, dan batas akhir maksimal 100%.");
        }

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
