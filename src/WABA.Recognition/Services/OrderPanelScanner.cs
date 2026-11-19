using System.Diagnostics;
using Windows.Graphics.Imaging;
using Windows.Globalization;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;
using WABA.Domain.Models;
using WABA.Recognition.Models;

namespace WABA.Recognition.Services;

/// <summary>
/// Windows built-in OCR adapter. It uses the OCR language packs already installed
/// on the user's Windows profile and does not download a cloud model.
/// </summary>
public sealed class OrderPanelScanner
{
    private readonly OcrEngine? _engine;
    private readonly OrderTextParser _parser = new();

    public OrderPanelScanner()
    {
        _engine = OcrEngine.TryCreateFromUserProfileLanguages();

        if (_engine is null)
        {
            var english = new Language("en-US");
            if (OcrEngine.IsLanguageSupported(english))
                _engine = OcrEngine.TryCreateFromLanguage(english);
        }
    }

    public bool IsAvailable => _engine is not null;

    public string EngineDescription => _engine is null
        ? "Windows OCR tidak tersedia. Pasang OCR language pack melalui Windows Settings."
        : $"Windows OCR • {_engine.RecognizerLanguage.LanguageTag}";

    public async Task<OrderScanResult> ScanAsync(
        CapturedFrame source,
        OrderPanelRegion requestedRegion,
        CancellationToken cancellationToken = default)
    {
        if (_engine is null)
        {
            throw new InvalidOperationException(
                "Windows OCR tidak tersedia. Periksa OCR language pack di Windows Settings > Time & language > Language & region.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();

        var pixelRegion = requestedRegion.ToPixels(source.Width, source.Height);
        var cropped = Crop(source, pixelRegion);
        var ocrInput = Upscale2x(cropped);

        var buffer = CryptographicBuffer.CreateFromByteArray(ocrInput.BgraPixels);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            buffer,
            BitmapPixelFormat.Bgra8,
            ocrInput.Width,
            ocrInput.Height,
            BitmapAlphaMode.Ignore);

        cancellationToken.ThrowIfCancellationRequested();
        var recognized = await _engine.RecognizeAsync(bitmap);
        cancellationToken.ThrowIfCancellationRequested();

        // Keep both the readable OCR lines and each word's bounding box.
        // Windows OCR may split a product label and its 0/1 quantity into
        // separate text lines even though they occupy the same visual row.
        var lines = recognized.Lines
            .Select(line => line.Text.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        var words = recognized.Lines
            .SelectMany(line => line.Words)
            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
            .Select(word => new OcrWordFragment(
                word.Text,
                word.BoundingRect.X,
                word.BoundingRect.Y,
                word.BoundingRect.Width,
                word.BoundingRect.Height))
            .ToArray();

        var parsedItems = words.Length > 0
            ? _parser.ParseWords(words)
            : _parser.Parse(lines);

        var wordDiagnostics = words.Length == 0
            ? "(Windows OCR tidak mengembalikan token kata.)"
            : string.Join(Environment.NewLine, words.Select(word =>
                $"{word.Text} @ X={word.X:0}, Y={word.Y:0}, W={word.Width:0}, H={word.Height:0}"));

        timer.Stop();

        return new OrderScanResult(
            string.Join(Environment.NewLine, lines),
            parsedItems,
            _engine.RecognizerLanguage.LanguageTag,
            timer.Elapsed,
            pixelRegion,
            cropped,
            wordDiagnostics);
    }

    private static CapturedFrame Crop(CapturedFrame source, PixelRegion region)
    {
        var stride = region.Width * 4;
        var pixels = new byte[stride * region.Height];

        for (var y = 0; y < region.Height; y++)
        {
            var sourceOffset = (region.Y + y) * source.Stride + region.X * 4;
            Buffer.BlockCopy(source.BgraPixels, sourceOffset, pixels, y * stride, stride);
        }

        return new CapturedFrame(
            region.Width, region.Height, stride, pixels, source.CapturedAt);
    }

    private static CapturedFrame Upscale2x(CapturedFrame source)
    {
        var width = source.Width * 2;
        var height = source.Height * 2;
        var stride = width * 4;
        var pixels = new byte[stride * height];

        for (var y = 0; y < source.Height; y++)
        {
            var sourceRow = y * source.Stride;
            var targetRow1 = (y * 2) * stride;
            var targetRow2 = targetRow1 + stride;

            for (var x = 0; x < source.Width; x++)
            {
                var sourceOffset = sourceRow + x * 4;
                var targetOffset1 = targetRow1 + x * 8;
                var targetOffset2 = targetRow2 + x * 8;

                Buffer.BlockCopy(source.BgraPixels, sourceOffset, pixels, targetOffset1, 4);
                Buffer.BlockCopy(source.BgraPixels, sourceOffset, pixels, targetOffset1 + 4, 4);
                Buffer.BlockCopy(source.BgraPixels, sourceOffset, pixels, targetOffset2, 4);
                Buffer.BlockCopy(source.BgraPixels, sourceOffset, pixels, targetOffset2 + 4, 4);
            }
        }

        return new CapturedFrame(width, height, stride, pixels, source.CapturedAt);
    }
}
