using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WABA.Domain.Models;

namespace WABA.Infrastructure.Windows;

/// <summary>
/// Phase 1 capture implementation: captures the visible client rectangle from the desktop.
/// Keep the target game window visible and unobstructed. A later phase can replace this
/// adapter with Windows.Graphics.Capture without changing the UI contract.
/// </summary>
public sealed class DesktopCaptureService
{
    public CapturedFrame CaptureClientArea(nint hWnd)
    {
        if (!WindowCatalog.TryGetClientBounds(
                hWnd, out var left, out var top, out var width, out var height))
        {
            throw new InvalidOperationException(
                "Jendela target tidak aktif, diminimalkan, atau ukuran client area tidak valid.");
        }

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(
                left,
                top,
                0,
                0,
                new Size(width, height),
                CopyPixelOperation.SourceCopy);
        }

        var rectangle = new Rectangle(0, 0, width, height);
        var bitmapData = bitmap.LockBits(
            rectangle,
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            var stride = width * 4;
            var pixels = new byte[stride * height];

            for (var y = 0; y < height; y++)
            {
                var sourceRow = IntPtr.Add(bitmapData.Scan0, y * bitmapData.Stride);
                Marshal.Copy(sourceRow, pixels, y * stride, stride);
            }

            return new CapturedFrame(
                width,
                height,
                stride,
                pixels,
                DateTimeOffset.Now);
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }
}
