using WABA.Domain.Models;
using WABA.Recognition.Models;

namespace WABA.Recognition.Services;

/// <summary>
/// Cheap ROI-only frame change detector. It samples a small grid instead of running OCR
/// on every preview frame. OCR is requested only after a meaningful visual change.
/// </summary>
public sealed class OrderPanelChangeDetector
{
    private byte[]? _previous;
    private const int GridColumns = 40;
    private const int GridRows = 20;
    private const int PixelDeltaThreshold = 16;
    private const double ChangedSampleRatio = 0.015;

    public bool HasChanged(CapturedFrame frame, PixelRegion region)
    {
        var signature = CreateSignature(frame, region);
        if (_previous is null)
        {
            _previous = signature;
            return true;
        }

        var changed = 0;
        for (var i = 0; i < signature.Length; i++)
        {
            if (Math.Abs(signature[i] - _previous[i]) >= PixelDeltaThreshold)
                changed++;
        }

        _previous = signature;
        return changed >= signature.Length * ChangedSampleRatio;
    }

    public void Reset() => _previous = null;

    private static byte[] CreateSignature(CapturedFrame frame, PixelRegion region)
    {
        var samples = new byte[GridColumns * GridRows];
        var index = 0;

        for (var gy = 0; gy < GridRows; gy++)
        {
            var y = region.Y + Math.Min(region.Height - 1,
                (int)((gy + 0.5) * region.Height / GridRows));

            for (var gx = 0; gx < GridColumns; gx++)
            {
                var x = region.X + Math.Min(region.Width - 1,
                    (int)((gx + 0.5) * region.Width / GridColumns));
                var offset = y * frame.Stride + x * 4;

                var b = frame.BgraPixels[offset];
                var g = frame.BgraPixels[offset + 1];
                var r = frame.BgraPixels[offset + 2];

                samples[index++] = (byte)((r * 30 + g * 59 + b * 11) / 100);
            }
        }

        return samples;
    }
}
