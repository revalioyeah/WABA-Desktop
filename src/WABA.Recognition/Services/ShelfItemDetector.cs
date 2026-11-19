using System.Diagnostics;
using Windows.Graphics.Imaging;
using Windows.Globalization;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;
using WABA.Domain.Models;
using WABA.Recognition.Models;

namespace WABA.Recognition.Services;

/// <summary>
/// Phase 3.1 label-based shelf detector. It only estimates product positions;
/// it never sends mouse input. Results must be visually verified before use.
/// </summary>
public sealed class ShelfItemDetector
{
    private readonly OcrEngine? _engine;

    // Calibrated as broad starting regions for the standard landscape shop layout.
    // They are intentionally independent from the order-board ROI.
    private static readonly ShelfScanRegion[] DefaultRegions =
    [
        new("LEFT_SHELF", 4, 13, 35, 58),
        new("RIGHT_SHELF", 60, 13, 37, 58),
        new("PULSE_COUNTER", 7, 75, 34, 23)
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
        : $"Shelf OCR • {_engine.RecognizerLanguage.LanguageTag}";

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
            var scaled = Upscale2x(crop);
            var buffer = CryptographicBuffer.CreateFromByteArray(scaled.BgraPixels);
            using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
                buffer, BitmapPixelFormat.Bgra8, scaled.Width, scaled.Height, BitmapAlphaMode.Ignore);

            var recognized = await _engine.RecognizeAsync(bitmap);
            cancellationToken.ThrowIfCancellationRequested();

            var words = recognized.Lines
                .SelectMany(line => line.Words)
                .Where(word => !string.IsNullOrWhiteSpace(word.Text) && word.BoundingRect.Width > 0 && word.BoundingRect.Height > 0)
                .Select(word => new ShelfWord(
                    word.Text.Trim(),
                    region.X + word.BoundingRect.X / 2d,
                    region.Y + word.BoundingRect.Y / 2d,
                    word.BoundingRect.Width / 2d,
                    word.BoundingRect.Height / 2d))
                .ToArray();

            var rows = BuildCaptionClusters(words, frame.Width);
            var unmatchedLabels = new List<string>();
            foreach (var row in rows)
            {
                // Keep short numeric tokens because some catalog labels are "2 GB",
                // "5 GB", and "10 GB". Ignore long numeric-only price tokens.
                var labelWords = row.Words.Where(w =>
                    w.Text.Any(char.IsLetter) ||
                    (w.Text.Length <= 2 && w.Text.Any(char.IsDigit))).ToArray();
                if (labelWords.Length == 0)
                    continue;

                var raw = string.Join(" ", labelWords.OrderBy(w => w.X).Select(w => w.Text));
                var match = ProductLabelMatcher.Match(raw);
                if (match.Name is null)
                {
                    if (unmatchedLabels.Count < 12)
                        unmatchedLabels.Add(raw);
                    continue;
                }

                var anchorWords = labelWords.Where(w => w.Text.Any(char.IsLetter)).ToArray();
                if (anchorWords.Length == 0)
                    anchorWords = labelWords;
                var centerX = anchorWords.Average(w => w.CenterX);
                var labelCenterY = anchorWords.Average(w => w.CenterY);

                // Product thumbnails sit immediately above their caption. This is
                // an estimate only; the overlay lets the user verify the point.
                var verticalOffset = Math.Clamp(frame.Height * 0.027d, 12d, 42d);
                var targetX = (int)Math.Round(centerX);
                var targetY = (int)Math.Round(labelCenterY - verticalOffset);
                targetX = Math.Clamp(targetX, 0, frame.Width - 1);
                targetY = Math.Clamp(targetY, 0, frame.Height - 1);

                var candidate = new ShelfItemDetection(
                    match.Name, targetX, targetY, regionDefinition.Name, raw, match.Confidence);

                // Avoid duplicate OCR fragments for the same caption while allowing
                // the same product to appear in two different shelf positions.
                var duplicate = detections.Any(existing =>
                    existing.ProductName == candidate.ProductName &&
                    Math.Abs(existing.ClientX - candidate.ClientX) < 18 &&
                    Math.Abs(existing.ClientY - candidate.ClientY) < 18);
                if (!duplicate)
                    detections.Add(candidate);
            }

            var unmatched = unmatchedLabels.Count == 0
                ? "tidak ada"
                : string.Join(" | ", unmatchedLabels);
            diagnostics.Add($"{regionDefinition.Name}: {words.Length} token, {rows.Count} klaster, kandidat kumulatif {detections.Count}. Label belum cocok: {unmatched}");
        }

        timer.Stop();
        return new ShelfDetectionResult(
            detections.OrderBy(item => item.Area).ThenBy(item => item.ClientY).ThenBy(item => item.ClientX).ToArray(),
            timer.Elapsed,
            _engine.RecognizerLanguage.LanguageTag,
            string.Join(Environment.NewLine, diagnostics));
    }

    private static List<ShelfRow> BuildCaptionClusters(IReadOnlyList<ShelfWord> words, int frameWidth)
    {
        // First group tokens that share a visual baseline.
        var verticalRows = new List<ShelfRow>();
        foreach (var word in words.OrderBy(w => w.CenterY).ThenBy(w => w.X))
        {
            ShelfRow? best = null;
            var bestDistance = double.MaxValue;
            foreach (var row in verticalRows)
            {
                var distance = Math.Abs(word.CenterY - row.CenterY);
                var tolerance = Math.Clamp(Math.Min(word.Height, row.AverageHeight) * 0.8d, 4d, 13d);
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

        // Then split a long shelf row into separate product captions by horizontal gap.
        // This matters because several products share the same y-coordinate on a shelf.
        var clusters = new List<ShelfRow>();
        var horizontalGap = Math.Clamp(frameWidth * 0.012d, 14d, 28d);
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
