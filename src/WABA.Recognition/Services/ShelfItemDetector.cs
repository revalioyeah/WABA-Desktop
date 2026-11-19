using System.Diagnostics;
using Windows.Graphics.Imaging;
using Windows.Globalization;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;
using WABA.Domain.Models;
using WABA.Recognition.Models;

namespace WABA.Recognition.Services;

/// <summary>
/// Phase 3.1.2 label-based shelf detector. It estimates product positions only;
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
        : $"Shelf OCR • {_engine.RecognizerLanguage.LanguageTag} • katalog & pulse OCR v0.3.1.3";

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
            var isPulseCounter = regionDefinition.Name == "PULSE_COUNTER";

            // Counter captions are materially smaller than shelf captions.
            // Upscale only this small ROI to 4x; the larger shelf areas remain
            // at 2x to keep the overall scan responsive.
            var scale = isPulseCounter ? 4 : 2;
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

            var rows = BuildCaptionClusters(words, frame.Width, isPulseCounter);
            var unmatchedLabels = new List<string>();
            var unmatchedCaptions = new List<UnmatchedCaption>();
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
                    if (isPulseCounter)
                    {
                        unmatchedCaptions.Add(new UnmatchedCaption(
                            raw,
                            orderedWords.Min(w => w.X),
                            orderedWords.Max(w => w.X + w.Width),
                            orderedWords.Min(w => w.Y),
                            orderedWords.Max(w => w.Y + w.Height)));
                    }
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

            // A lone OCR digit can be the complete reading of the tiny "2 GB"
            // counter caption. Infer that product only when the digit is inside
            // the geometrically expected slot between already-recognized
            // E-Wallet and 10 GB anchors. The resulting confidence is deliberately
            // low and the UI renders it as a non-green candidate for validation.
            var spatialFallbackNotes = new List<string>();
            if (isPulseCounter)
            {
                var inferred = TryAddSpatiallyAnchoredDataPackages(
                    detections, unmatchedCaptions, frame.Height);
                foreach (var candidate in inferred)
                {
                    detections.Add(candidate);
                    spatialFallbackNotes.Add(
                        $"{candidate.ProductName} kandidat slot X={candidate.ClientX}, Y={candidate.ClientY} " +
                        $"(skor {candidate.Confidence:P0}; wajib verifikasi visual)");
                }
                matchedInRegion = detections.Count(item => item.Area == regionDefinition.Name);
            }

            var unmatched = unmatchedCaptions.Count > 0 && isPulseCounter
                ? string.Join(" | ", unmatchedCaptions.Select(caption =>
                    $"{caption.Raw}@X={(caption.Left + caption.Right) / 2d:0},Y={(caption.Top + caption.Bottom) / 2d:0}"))
                : unmatchedLabels.Count == 0
                    ? "tidak ada"
                    : string.Join(" | ", unmatchedLabels);
            if (spatialFallbackNotes.Count > 0)
                unmatched += " | kandidat berbasis posisi: " + string.Join("; ", spatialFallbackNotes);
            var expectedProducts = ExpectedProductsByArea[regionDefinition.Name];
            var foundProducts = detections
                .Where(item => item.Area == regionDefinition.Name)
                .Select(item => item.ProductName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missingProducts = expectedProducts
                .Where(name => !foundProducts.Contains(name))
                .ToArray();
            var missing = missingProducts.Length == 0
                ? "tidak ada"
                : string.Join(", ", missingProducts);

            diagnostics.Add(
                $"{regionDefinition.Name}: skala {scale}x, {words.Length} token, " +
                $"{rows.Count} klaster, {matchedInRegion} cocok. " +
                $"Label belum cocok: {unmatched}. Katalog belum terdeteksi: {missing}");
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

    private static readonly IReadOnlyDictionary<string, string[]> ExpectedProductsByArea =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["LEFT_SHELF"] =
            [
                "Mie rebus", "Telur", "Mie goreng", "Kopi sachet", "Beras", "Buku tulis",
                "Makanan kucing", "Sayuran", "Obat", "Galon", "LPG 3 kg", "Gula",
                "Minyak goreng", "Sabun mandi", "Teh celup", "Deterjen"
            ],
            ["RIGHT_SHELF"] =
            [
                "Ciki", "Permen", "Biskuit", "Air mineral", "Roti", "Jamu", "Teh botol",
                "Kerupuk", "Minuman energi", "Soda", "Rengginang", "Kopi susu", "Susu kotak",
                "Es krim", "Kurma", "Sirup"
            ],
            ["PULSE_COUNTER"] =
            ["E-Wallet", "2 GB", "5 GB", "10 GB", "Token listrik"]
        };

    private static List<ShelfRow> BuildCaptionClusters(
        IReadOnlyList<ShelfWord> words,
        int frameWidth,
        bool isPulseCounter)
    {
        // Group tokens sharing a visual baseline first. The pulse counter has
        // small two-part captions (for example '2' + 'GB'), so use a wider
        // vertical tolerance only in that region.

        var verticalRows = new List<ShelfRow>();
        foreach (var word in words.OrderBy(w => w.CenterY).ThenBy(w => w.X))
        {
            ShelfRow? best = null;
            var bestDistance = double.MaxValue;
            foreach (var row in verticalRows)
            {
                var distance = Math.Abs(word.CenterY - row.CenterY);
                var tolerance = isPulseCounter
                    ? Math.Clamp(Math.Min(word.Height, row.AverageHeight) * 1.35d, 6d, 22d)
                    : Math.Clamp(Math.Min(word.Height, row.AverageHeight) * 0.85d, 4d, 14d);
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
        var horizontalGap = isPulseCounter
            ? Math.Clamp(frameWidth * 0.026d, 34d, 52d)
            : Math.Clamp(frameWidth * 0.015d, 18d, 30d);
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

    private static IReadOnlyList<ShelfItemDetection> TryAddSpatiallyAnchoredDataPackages(
        List<ShelfItemDetection> detections,
        IReadOnlyList<UnmatchedCaption> unmatchedCaptions,
        int frameHeight)
    {
        var wallet = detections.FirstOrDefault(item =>
            item.Area == "PULSE_COUNTER" && item.ProductName == "E-Wallet");
        var tenGb = detections.FirstOrDefault(item =>
            item.Area == "PULSE_COUNTER" && item.ProductName == "10 GB");
        var token = detections.FirstOrDefault(item =>
            item.Area == "PULSE_COUNTER" && item.ProductName == "Token listrik");

        // Three detected anchors establish the five-card counter spacing:
        // E-Wallet, [2 GB], [5 GB], 10 GB, Token listrik.
        // Missing packages are only proposed when an OCR fragment is present
        // near the corresponding geometric slot; no coordinate is fabricated
        // from the catalogue alone.
        if (wallet is null || tenGb is null || token is null)
            return [];

        var spacingFromWalletToTen = (tenGb.ClientX - wallet.ClientX) / 3d;
        var spacingFromTenToToken = (double)token.ClientX - tenGb.ClientX;
        if (spacingFromWalletToTen is < 65d or > 145d ||
            spacingFromTenToToken is < 65d or > 145d ||
            Math.Abs(spacingFromWalletToTen - spacingFromTenToToken) > 24d)
            return [];

        var spacing = (spacingFromWalletToTen + spacingFromTenToToken) / 2d;
        var expectedY = (wallet.ClientY + tenGb.ClientY + token.ClientY) / 3d;
        var maxXDistance = Math.Clamp(spacing * 0.42d, 28d, 48d);
        var verticalOffset = Math.Clamp(frameHeight * 0.027d, 12d, 42d);
        var candidates = new List<ShelfItemDetection>();

        TryCreateCandidate("2 GB", wallet.ClientX + spacing, ["2", "z"], 0.68d);
        TryCreateCandidate("5 GB", wallet.ClientX + spacing * 2d, ["50b", "5ob", "s0b", "5g", "sgb"], 0.72d);
        return candidates;

        void TryCreateCandidate(string productName, double expectedX, string[] acceptedFragments, double confidence)
        {
            if (detections.Any(item => item.Area == "PULSE_COUNTER" &&
                                       item.ProductName.Equals(productName, StringComparison.OrdinalIgnoreCase)))
                return;

            var fragment = unmatchedCaptions
                .Select(caption => new
                {
                    Caption = caption,
                    Compact = new string(caption.Raw.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant(),
                    CenterX = (caption.Left + caption.Right) / 2d,
                    CenterY = (caption.Top + caption.Bottom) / 2d
                })
                .Where(item => acceptedFragments.Contains(item.Compact, StringComparer.OrdinalIgnoreCase))
                .Where(item => Math.Abs(item.CenterX - expectedX) <= maxXDistance)
                .Where(item => Math.Abs((item.CenterY - verticalOffset) - expectedY) <= 42d)
                .OrderBy(item => Math.Abs(item.CenterX - expectedX))
                .FirstOrDefault();

            if (fragment is null)
                return;

            var targetY = (int)Math.Round(fragment.CenterY - verticalOffset);
            candidates.Add(new ShelfItemDetection(
                productName,
                (int)Math.Round(expectedX),
                Math.Clamp(targetY, 0, frameHeight - 1),
                "PULSE_COUNTER",
                $"{fragment.Caption.Raw} [slot-inferred; verify marker]",
                confidence));
        }
    }

    private sealed record UnmatchedCaption(
        string Raw,
        double Left,
        double Right,
        double Top,
        double Bottom);

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
