using System.Diagnostics;
using Windows.Graphics.Imaging;
using Windows.Globalization;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;
using WABA.Domain.Models;
using WABA.Recognition.Models;

namespace WABA.Recognition.Services;

/// <summary>
/// Phase 3.1.4 label-based shelf detector. It estimates product positions only;
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
        : $"Shelf OCR • {_engine.RecognizerLanguage.LanguageTag} • targeted ROI fallback v0.3.1.4";

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

            // Targeted fallback crops are only scanned when a catalogue label is
            // missing. This keeps the normal shelf scan fast while giving the
            // visually small Gula and prepaid-data captions a second OCR pass.
            var spatialFallbackNotes = new List<string>();

            if (regionDefinition.Name == "LEFT_SHELF" &&
                !detections.Any(item => item.Area == "LEFT_SHELF" &&
                                        item.ProductName.Equals("Gula", StringComparison.OrdinalIgnoreCase)))
            {
                var gula = await TryDetectGulaFallbackAsync(frame, cancellationToken);
                if (gula is not null)
                {
                    detections.Add(gula);
                    spatialFallbackNotes.Add(
                        $"Gula fallback OCR cocok ({gula.Confidence:P0}); titik preview X={gula.ClientX}, Y={gula.ClientY}");
                }
            }

            if (isPulseCounter)
            {
                var inferred = await TryDetectCounterSlotFallbacksAsync(
                    frame, region, detections, cancellationToken);

                foreach (var candidate in inferred)
                {
                    detections.Add(candidate);
                    spatialFallbackNotes.Add(
                        $"{candidate.ProductName} slot OCR: X={candidate.ClientX}, Y={candidate.ClientY}, " +
                        $"skor teks {candidate.Confidence:P0}");
                }
            }

            matchedInRegion = detections.Count(item =>
                item.Area.Equals(regionDefinition.Name, StringComparison.OrdinalIgnoreCase));

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

    private async Task<ShelfItemDetection?> TryDetectGulaFallbackAsync(
        CapturedFrame frame,
        CancellationToken cancellationToken)
    {
        // The current game layout places the Gula caption at the far-right
        // slot of row three on the left shelf. Scan only that compact label
        // area; do not infer a detection if OCR cannot read the caption.
        var roi = PercentageRegion(frame, 0.307d, 0.488d, 0.081d, 0.091d);
        var read = await ReadExpectedLabelAsync(frame, roi, "Gula", 4, cancellationToken);
        if (!read.Matched)
            return null;

        // Product thumbnail centre, calibrated against the supplied 1767x992
        // game capture. Coordinates scale with the captured client dimensions.
        var targetX = Math.Clamp((int)Math.Round(frame.Width * 0.347d), 0, frame.Width - 1);
        var targetY = Math.Clamp((int)Math.Round(frame.Height * 0.473d), 0, frame.Height - 1);

        return new ShelfItemDetection(
            "Gula", targetX, targetY, "LEFT_SHELF",
            $"{read.Raw} [targeted-label-ROI; verify marker]",
            Math.Min(read.Confidence, 0.94d));
    }

    private async Task<IReadOnlyList<ShelfItemDetection>> TryDetectCounterSlotFallbacksAsync(
        CapturedFrame frame,
        PixelRegion counterRegion,
        IReadOnlyList<ShelfItemDetection> detections,
        CancellationToken cancellationToken)
    {
        // The counter has five fixed horizontal card slots:
        // E-Wallet, 2 GB, 5 GB, 10 GB, Token listrik.
        // Fractions are relative to the counter ROI, so the mapping scales
        // with the actual captured client size rather than desktop resolution.
        var slots = new[]
        {
            new CounterSlot("2 GB", 0.385d),
            new CounterSlot("5 GB", 0.562d)
        };

        var candidates = new List<ShelfItemDetection>();
        foreach (var slot in slots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (detections.Any(item =>
                    item.Area.Equals("PULSE_COUNTER", StringComparison.OrdinalIgnoreCase) &&
                    item.ProductName.Equals(slot.ProductName, StringComparison.OrdinalIgnoreCase)))
                continue;

            var centerX = counterRegion.X + counterRegion.Width * slot.CenterFraction;
            var roiWidth = Math.Clamp((int)Math.Round(counterRegion.Width * 0.13d), 64, 96);
            var roiHeight = Math.Clamp((int)Math.Round(counterRegion.Height * 0.72d), 110, 210);
            var roiTop = counterRegion.Y + (int)Math.Round(counterRegion.Height * 0.20d);
            var roi = ClampedRegion(
                frame,
                (int)Math.Round(centerX - roiWidth / 2d),
                roiTop,
                roiWidth,
                roiHeight);

            var read = await ReadExpectedLabelAsync(
                frame, roi, slot.ProductName, 4, cancellationToken);

            if (!read.Matched && !read.HasWeakExpectedDigit)
                continue;

            var targetX = Math.Clamp((int)Math.Round(centerX), 0, frame.Width - 1);
            // The product artwork is above its small black caption. This
            // target is the centre of the voucher artwork, not the OCR text.
            var targetY = Math.Clamp(
                (int)Math.Round(counterRegion.Y + counterRegion.Height * 0.40d),
                0,
                frame.Height - 1);

            var confidence = read.Matched
                ? Math.Min(read.Confidence, 0.94d)
                : 0.68d;

            candidates.Add(new ShelfItemDetection(
                slot.ProductName,
                targetX,
                targetY,
                "PULSE_COUNTER",
                $"{read.Raw} [slot-ROI; {(read.Matched ? "OCR matched" : "weak digit candidate")}; verify marker]",
                confidence));
        }

        return candidates;
    }

    private async Task<ExpectedLabelRead> ReadExpectedLabelAsync(
        CapturedFrame frame,
        PixelRegion region,
        string expectedName,
        int scale,
        CancellationToken cancellationToken)
    {
        var cropped = Crop(frame, region);
        var scaled = Upscale(cropped, scale);
        var buffer = CryptographicBuffer.CreateFromByteArray(scaled.BgraPixels);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            buffer,
            BitmapPixelFormat.Bgra8,
            scaled.Width,
            scaled.Height,
            BitmapAlphaMode.Ignore);

        cancellationToken.ThrowIfCancellationRequested();
        var recognized = await _engine!.RecognizeAsync(bitmap);
        cancellationToken.ThrowIfCancellationRequested();

        var lineTexts = recognized.Lines
            .Select(line => line.Text.Trim())
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        var raw = lineTexts.Length > 0
            ? string.Join(" | ", lineTexts)
            : string.Join(" ", recognized.Lines
                .SelectMany(line => line.Words)
                .Select(word => word.Text));

        foreach (var line in recognized.Lines)
        {
            var words = line.Words
                .Select(word => word.Text.Trim())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray();

            // Test short adjacent token windows so "2" + "GB" and "5" + "GB"
            // can match even when Windows OCR splits the caption.
            for (var start = 0; start < words.Length; start++)
            {
                for (var length = 1; length <= Math.Min(3, words.Length - start); length++)
                {
                    var candidateText = string.Join(" ", words.Skip(start).Take(length));
                    var match = ProductLabelMatcher.Match(candidateText);
                    if (match.Name is not null &&
                        match.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
                    {
                        return new ExpectedLabelRead(
                            true, false, candidateText, match.Confidence);
                    }
                }
            }

            var lineMatch = ProductLabelMatcher.Match(line.Text);
            if (lineMatch.Name is not null &&
                lineMatch.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
            {
                return new ExpectedLabelRead(true, false, line.Text.Trim(), lineMatch.Confidence);
            }
        }

        var normalizedTokens = recognized.Lines
            .SelectMany(line => line.Words)
            .Select(word => new string(word.Text
                .Where(char.IsLetterOrDigit)
                .ToArray()).ToLowerInvariant())
            .Where(token => token.Length > 0)
            .ToArray();

        var weakDigit = expectedName switch
        {
            "2 GB" => normalizedTokens.Any(token => token is "2" or "z"),
            "5 GB" => normalizedTokens.Any(token => token is "5" or "s"),
            _ => false
        };

        return new ExpectedLabelRead(false, weakDigit, raw, 0d);
    }

    private static PixelRegion PercentageRegion(
        CapturedFrame frame,
        double xPercent,
        double yPercent,
        double widthPercent,
        double heightPercent)
    {
        return ClampedRegion(
            frame,
            (int)Math.Round(frame.Width * xPercent),
            (int)Math.Round(frame.Height * yPercent),
            (int)Math.Round(frame.Width * widthPercent),
            (int)Math.Round(frame.Height * heightPercent));
    }

    private static PixelRegion ClampedRegion(
        CapturedFrame frame,
        int x,
        int y,
        int width,
        int height)
    {
        x = Math.Clamp(x, 0, Math.Max(0, frame.Width - 1));
        y = Math.Clamp(y, 0, Math.Max(0, frame.Height - 1));
        var right = Math.Clamp(x + Math.Max(1, width), x + 1, frame.Width);
        var bottom = Math.Clamp(y + Math.Max(1, height), y + 1, frame.Height);
        return new PixelRegion(x, y, right - x, bottom - y);
    }

    private sealed record CounterSlot(string ProductName, double CenterFraction);

    private sealed record ExpectedLabelRead(
        bool Matched,
        bool HasWeakExpectedDigit,
        string Raw,
        double Confidence);

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
