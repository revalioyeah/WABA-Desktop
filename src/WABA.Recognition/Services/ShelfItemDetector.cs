using System.Diagnostics;
using Windows.Graphics.Imaging;
using Windows.Globalization;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;
using WABA.Domain.Models;
using WABA.Recognition.Models;

namespace WABA.Recognition.Services;

/// <summary>
/// Phase 3.1.1 label-based shelf detector. It estimates product positions only;
/// it never sends mouse input. All target coordinates require visual validation.
/// </summary>
public sealed class ShelfItemDetector
{
    private readonly OcrEngine? _engine;

    // Calibrated for the user's current 1920x1080-style shop layout. The pulse
    // region is slightly taller because small package captions sit near its
    // lower edge. Regions remain percentage-based for other client sizes.
    private static readonly ShelfScanRegion[] DefaultRegions =
    [
        new("LEFT_SHELF", 4, 12, 35, 61),
        new("RIGHT_SHELF", 60, 12, 37, 61),
        new("PULSE_COUNTER", 7, 72, 34, 27)
    ];

    public ShelfItemDetector()
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
        ? "Windows OCR tidak tersedia untuk deteksi rak."
        : $"Shelf OCR • {_engine.RecognizerLanguage.LanguageTag} • katalog alias v0.3.1.1";

    public async Task<ShelfDetectionResult> DetectAsync(
        CapturedFrame frame,
        CancellationToken cancellationToken = default)
    {
        if (_engine is null)
            throw new InvalidOperationException("Windows OCR tidak tersedia. Periksa OCR language pack.");

        var timer = Stopwatch.StartNew();
        var detections = new List<ShelfItemDetection>();
        var diagnostics = new List<string>();

        foreach (var regionDefinition in DefaultRegions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var region = regionDefinition.ToPixels(frame.Width, frame.Height);
            var crop = Crop(frame, region);

            // The counter labels are smaller than shelf captions. Use 3x only
            // for that region to improve 2GB/5GB/E-Wallet recall without making
            // every OCR pass more expensive.
            var scale = regionDefinition.Name == "PULSE_COUNTER" ? 3 : 2;
            var scaled = Upscale(crop, scale);
            var buffer = CryptographicBuffer.CreateFromByteArray(scaled.BgraPixels);
            using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
                buffer, BitmapPixelFormat.Bgra8, scaled.Width, scaled.Height, BitmapAlphaMode.Ignore);

            var recognized = await _engine.RecognizeAsync(bitmap);
            cancellationToken.ThrowIfCancellationRequested();

            var words = recognized.Lines
                .SelectMany(line => line.Words)
                .Where(word => !string.IsNullOrWhiteSpace(word.Text) &&
                               word.BoundingRect.Width > 0 && word.BoundingRect.Height > 0)
                .Select(word => new ShelfWord(
                    word.Text.Trim(),
                    region.X + word.BoundingRect.X / (double)scale,
                    region.Y + word.BoundingRect.Y / (double)scale,
                    word.BoundingRect.Width / (double)scale,
                    word.BoundingRect.Height / (double)scale))
                .ToArray();

            var rows = BuildCaptionClusters(words, frame.Width);
            var unmatchedLabels = new List<string>();
            var matchedInRegion = 0;

            foreach (var row in rows)
            {
                // Keep short numeric tokens for labels such as 2 GB, 5 GB and
                // 10 GB. Long numeric-only price/count tokens are excluded.
                var labelWords = row.Words.Where(w =>
                    w.Text.Any(char.IsLetter) ||
                    (w.Text.Length <= 2 && w.Text.Any(char.IsDigit))).ToArray();
                if (labelWords.Length == 0)
                    continue;

                var orderedWords = labelWords.OrderBy(w => w.X).ToArray();
                var raw = string.Join(" ", orderedWords.Select(w => w.Text));
                var match = ProductLabelMatcher.Match(raw);
                if (match.Name is null)
                {
                    if (unmatchedLabels.Count < 20)
                        unmatchedLabels.Add(raw);
                    continue;
                }

                // Use the complete caption bounds, including numeric tokens.
                // Previously only alphabetic words were used, which shifted
                // the target for labels such as "2 GB" and "10 GB".
                var captionLeft = orderedWords.Min(w => w.X);
                var captionRight = orderedWords.Max(w => w.X + w.Width);
                var captionTop = orderedWords.Min(w => w.Y);
                var captionBottom = orderedWords.Max(w => w.Y + w.Height);
                var captionCenterX = (captionLeft + captionRight) / 2d;
                var labelCenterY = (captionTop + captionBottom) / 2d;

                // Product thumbnails are immediately above their captions.
                // This remains an estimate and must be verified on the overlay.
                var verticalOffset = Math.Clamp(frame.Height * 0.027d, 12d, 42d);
                var targetX = Math.Clamp((int)Math.Round(captionCenterX), 0, frame.Width - 1);
                var targetY = Math.Clamp((int)Math.Round(labelCenterY - verticalOffset), 0, frame.Height - 1);

                var candidate = new ShelfItemDetection(
                    match.Name, targetX, targetY, regionDefinition.Name, raw, match.Confidence);

                // Suppress duplicate OCR fragments at nearly identical points,
                // but preserve the same product if it is stocked in two places.
                var duplicateIndex = detections.FindIndex(existing =>
                    existing.ProductName == candidate.ProductName &&
                    existing.Area == candidate.Area &&
                    Math.Abs(existing.ClientX - candidate.ClientX) < 24 &&
                    Math.Abs(existing.ClientY - candidate.ClientY) < 24);

                if (duplicateIndex < 0)
                {
                    detections.Add(candidate);
                    matchedInRegion++;
                }
                else if (candidate.Confidence > detections[duplicateIndex].Confidence)
                {
                    detections[duplicateIndex] = candidate;
                }
            }

            var unmatched = unmatchedLabels.Count == 0
                ? "tidak ada"
                : string.Join(" | ", unmatchedLabels);
            diagnostics.Add(
                $"{regionDefinition.Name}: skala {scale}x, {words.Length} token, " +
                $"{rows.Count} klaster, {matchedInRegion} cocok. Label belum cocok: {unmatched}");
        }

        timer.Stop();
        return new ShelfDetectionResult(
            detections.OrderBy(item => item.Area)
                .ThenBy(item => item.ClientY)
                .ThenBy(item => item.ClientX)
                .ToArray(),
            timer.Elapsed,
            _engine.RecognizerLanguage.LanguageTag,
            string.Join(Environment.NewLine, diagnostics));
    }

    private static List<ShelfRow> BuildCaptionClusters(IReadOnlyList<ShelfWord> words, int frameWidth)
    {
        // Group tokens sharing a visual baseline first.
        var verticalRows = new List<ShelfRow>();
        foreach (var word in words.OrderBy(w => w.CenterY).ThenBy(w => w.X))
        {
            ShelfRow? best = null;
            var bestDistance = double.MaxValue;
            foreach (var row in verticalRows)
            {
                var distance = Math.Abs(word.CenterY - row.CenterY);
                var tolerance = Math.Clamp(Math.Min(word.Height, row.AverageHeight) * 0.85d, 4d, 14d);
                if (distance <= tolerance && distance < bestDistance)
                {
                    best = row;
                    bestDistance = distance;
                }
            }

            if (best is null)
            {
                best = new ShelfRow();
                verticalRows.Add(best);
            }
            best.Words.Add(word);
        }

        // Split adjacent products by horizontal gaps. A slightly wider gap
        // keeps OCR-separated caption tokens together while the product slots
        // themselves remain much farther apart in the current game layout.
        var horizontalGap = Math.Clamp(frameWidth * 0.015d, 18d, 30d);
        var clusters = new List<ShelfRow>();
        foreach (var visualRow in verticalRows.OrderBy(row => row.CenterY))
        {
            ShelfRow? current = null;
            var previousRight = double.NaN;
            foreach (var word in visualRow.Words.OrderBy(w => w.X))
            {
                var gap = double.IsNaN(previousRight) ? 0d : word.X - previousRight;
                if (current is null || gap > horizontalGap)
                {
                    current = new ShelfRow();
                    clusters.Add(current);
                }

                current.Words.Add(word);
                previousRight = double.IsNaN(previousRight)
                    ? word.X + word.Width
                    : Math.Max(previousRight, word.X + word.Width);
            }
        }

        return clusters;
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
        return new CapturedFrame(region.Width, region.Height, stride, pixels, source.CapturedAt);
    }

    private static CapturedFrame Upscale(CapturedFrame source, int factor)
    {
        if (factor <= 1)
            return source;

        var width = source.Width * factor;
        var height = source.Height * factor;
        var stride = width * 4;
        var pixels = new byte[stride * height];

        for (var y = 0; y < source.Height; y++)
        {
            var sourceRow = y * source.Stride;
            for (var x = 0; x < source.Width; x++)
            {
                var sourceOffset = sourceRow + x * 4;
                for (var dy = 0; dy < factor; dy++)
                {
                    var targetRow = (y * factor + dy) * stride;
                    for (var dx = 0; dx < factor; dx++)
                    {
                        var targetOffset = targetRow + (x * factor + dx) * 4;
                        Buffer.BlockCopy(source.BgraPixels, sourceOffset, pixels, targetOffset, 4);
                    }
                }
            }
        }

        return new CapturedFrame(width, height, stride, pixels, source.CapturedAt);
    }

    private sealed record ShelfWord(string Text, double X, double Y, double Width, double Height)
    {
        public double CenterX => X + Width / 2d;
        public double CenterY => Y + Height / 2d;
    }

    private sealed class ShelfRow
    {
        public List<ShelfWord> Words { get; } = [];
        public double CenterY => Words.Average(w => w.CenterY);
        public double AverageHeight => Words.Average(w => Math.Max(1d, w.Height));
    }
}
