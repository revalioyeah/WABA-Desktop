using System.Diagnostics;
using Windows.Graphics.Imaging;
using Windows.Globalization;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;
using WABA.Domain.Models;
using WABA.Recognition.Models;

namespace WABA.Recognition.Services;

/// <summary>
/// Phase 3.1.5 label-based shelf detector. It estimates product positions only;
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
        : $"Shelf OCR • {_engine.RecognizerLanguage.LanguageTag} • anchor-based shelf + counter fallback v0.3.1.5 (Stable Catalog managed by desktop layer)";

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
                var gula = await TryDetectGulaFallbackAsync(frame, detections, cancellationToken);
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
                        $"{candidate.ProductName} slot fallback: X={candidate.ClientX}, Y={candidate.ClientY}, " +
                        $"skor {candidate.Confidence:P0}");
                }

                // The main OCR path estimates counter targets from caption
                // baselines. Re-anchor every recognized counter product to
                // the artwork row using the three stable products as anchors.
                ApplyCounterSlotCalibration(frame, detections);
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
        IReadOnlyList<ShelfItemDetection> detections,
        CancellationToken cancellationToken)
    {
        // Gula occupies the fourth column of the same row as Galon and LPG.
        // Derive its target from already detected neighbours so this remains
        // aligned with the actual client capture instead of guessing from
        // desktop or preview dimensions.
        var galon = detections.FirstOrDefault(item =>
            item.Area == "LEFT_SHELF" &&
            item.ProductName.Equals("Galon", StringComparison.OrdinalIgnoreCase));
        var lpg = detections.FirstOrDefault(item =>
            item.Area == "LEFT_SHELF" &&
            item.ProductName.Equals("LPG 3 kg", StringComparison.OrdinalIgnoreCase));
        var obat = detections.FirstOrDefault(item =>
            item.Area == "LEFT_SHELF" &&
            item.ProductName.Equals("Obat", StringComparison.OrdinalIgnoreCase));

        var targetX = galon is not null && lpg is not null
            ? lpg.ClientX + (lpg.ClientX - galon.ClientX)
            : (int)Math.Round(frame.Width * 0.349d);

        var rowYValues = new[] { galon?.ClientY, lpg?.ClientY, obat?.ClientY }
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .OrderBy(value => value)
            .ToArray();

        var targetY = rowYValues.Length > 0
            ? rowYValues[rowYValues.Length / 2]
            : (int)Math.Round(frame.Height * 0.526d);

        // The label sits just below the product artwork. Scan a compact
        // region around the neighbouring-row estimate, plus a colour check
        // over the white/blue package itself.
        var labelRoi = ClampedRegion(
            frame,
            targetX - 88,
            targetY - 15,
            176,
            82);
        var read = await ReadExpectedLabelAsync(
            frame, labelRoi, "Gula", 5, cancellationToken);

        var packageRegion = ClampedRegion(
            frame,
            targetX - 48,
            targetY - 35,
            96,
            92);
        var hasPackageSignature = HasGulaPackageSignature(frame, packageRegion);

        if (!read.Matched && !hasPackageSignature)
            return null;

        var confidence = read.Matched
            ? Math.Min(read.Confidence, 0.94d)
            : 0.72d;

        var evidence = read.Matched
            ? $"{read.Raw} [neighbour-anchored label ROI; verify marker]"
            : $"{read.Raw} [white-blue package signature; neighbour-anchored; verify marker]";

        return new ShelfItemDetection(
            "Gula",
            Math.Clamp(targetX, 0, frame.Width - 1),
            Math.Clamp(targetY, 0, frame.Height - 1),
            "LEFT_SHELF",
            evidence,
            confidence);
    }

    private async Task<IReadOnlyList<ShelfItemDetection>> TryDetectCounterSlotFallbacksAsync(
        CapturedFrame frame,
        PixelRegion counterRegion,
        IReadOnlyList<ShelfItemDetection> detections,
        CancellationToken cancellationToken)
    {
        // Use E-Wallet, 10 GB and Token listrik as stable anchors. In the
        // supplied layout, the five counter cards are evenly spaced:
        // E-Wallet, 2 GB, 5 GB, 10 GB, Token listrik.
        var calibration = TryGetCounterCalibration(frame, detections);
        var fallbackY = (int)Math.Round(counterRegion.Y + counterRegion.Height * 0.60d);

        var slots = new[]
        {
            new CounterSlot(
                "2 GB",
                calibration?.TwoGbX ?? counterRegion.X + counterRegion.Width * 0.43d),
            new CounterSlot(
                "5 GB",
                calibration?.FiveGbX ?? counterRegion.X + counterRegion.Width * 0.59d)
        };

        var candidates = new List<ShelfItemDetection>();
        foreach (var slot in slots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (detections.Any(item =>
                    item.Area.Equals("PULSE_COUNTER", StringComparison.OrdinalIgnoreCase) &&
                    item.ProductName.Equals(slot.ProductName, StringComparison.OrdinalIgnoreCase)))
                continue;

            var centerX = slot.CenterX;
            var targetY = calibration?.ArtworkCenterY ?? fallbackY;

            // Include both the voucher artwork and the small caption beneath it.
            var roiWidth = Math.Clamp((int)Math.Round(counterRegion.Width * 0.16d), 76, 112);
            var roi = ClampedRegion(
                frame,
                (int)Math.Round(centerX - roiWidth / 2d),
                targetY - 38,
                roiWidth,
                104);

            var read = await ReadExpectedLabelAsync(
                frame, roi, slot.ProductName, 5, cancellationToken);

            var artworkRegion = ClampedRegion(
                frame,
                (int)Math.Round(centerX - 38d),
                targetY - 34,
                76,
                68);

            var hasColorSignature = HasCounterPackageColorSignature(
                frame, artworkRegion, slot.ProductName);

            if (!read.Matched && !read.HasWeakExpectedDigit && !hasColorSignature)
                continue;

            var confidence = read.Matched
                ? Math.Min(read.Confidence, 0.94d)
                : read.HasWeakExpectedDigit
                    ? 0.68d
                    : 0.74d;

            var evidence = read.Matched
                ? $"{read.Raw} [anchor-based slot OCR; verify marker]"
                : read.HasWeakExpectedDigit
                    ? $"{read.Raw} [weak digit + anchor slot; verify marker]"
                    : $"{read.Raw} [voucher colour signature + anchor slot; verify marker]";

            candidates.Add(new ShelfItemDetection(
                slot.ProductName,
                Math.Clamp((int)Math.Round(centerX), 0, frame.Width - 1),
                Math.Clamp(targetY, 0, frame.Height - 1),
                "PULSE_COUNTER",
                evidence,
                confidence));
        }

        return candidates;
    }

    private static CounterCalibration? TryGetCounterCalibration(
        CapturedFrame frame,
        IReadOnlyList<ShelfItemDetection> detections)
    {
        var wallet = detections.FirstOrDefault(item =>
            item.Area == "PULSE_COUNTER" &&
            item.ProductName.Equals("E-Wallet", StringComparison.OrdinalIgnoreCase));
        var tenGb = detections.FirstOrDefault(item =>
            item.Area == "PULSE_COUNTER" &&
            item.ProductName.Equals("10 GB", StringComparison.OrdinalIgnoreCase));
        var token = detections.FirstOrDefault(item =>
            item.Area == "PULSE_COUNTER" &&
            item.ProductName.Equals("Token listrik", StringComparison.OrdinalIgnoreCase));

        if (wallet is null || tenGb is null || token is null)
            return null;

        var spacingBetweenWalletAndTen = (tenGb.ClientX - wallet.ClientX) / 3d;
        double spacingBetweenTenAndToken = token.ClientX - tenGb.ClientX;

        if (spacingBetweenWalletAndTen is < 65d or > 145d ||
            spacingBetweenTenAndToken is < 65d or > 145d ||
            Math.Abs(spacingBetweenWalletAndTen - spacingBetweenTenAndToken) > 24d)
            return null;

        var spacing = (spacingBetweenWalletAndTen + spacingBetweenTenAndToken) / 2d;
        var anchorY = (wallet.ClientY + tenGb.ClientY + token.ClientY) / 3d;

        // Main OCR coordinates are just above the caption baseline. Move
        // upward by the calibrated artwork-to-caption offset to the voucher
        // centre. This is based on the supplied screen and the detected anchors.
        var artworkCenterY = (int)Math.Round(anchorY - frame.Height * 0.035d);

        return new CounterCalibration(
            wallet.ClientX,
            (int)Math.Round(wallet.ClientX + spacing),
            (int)Math.Round(wallet.ClientX + spacing * 2d),
            tenGb.ClientX,
            token.ClientX,
            Math.Clamp(artworkCenterY, 0, frame.Height - 1));
    }

    private static void ApplyCounterSlotCalibration(
        CapturedFrame frame,
        List<ShelfItemDetection> detections)
    {
        var calibration = TryGetCounterCalibration(frame, detections);
        if (calibration is null)
            return;

        for (var index = 0; index < detections.Count; index++)
        {
            var item = detections[index];
            if (!item.Area.Equals("PULSE_COUNTER", StringComparison.OrdinalIgnoreCase))
                continue;

            var targetX = item.ProductName switch
            {
                "E-Wallet" => calibration.WalletX,
                "2 GB" => calibration.TwoGbX,
                "5 GB" => calibration.FiveGbX,
                "10 GB" => calibration.TenGbX,
                "Token listrik" => calibration.TokenX,
                _ => item.ClientX
            };

            if (item.ProductName is not ("E-Wallet" or "2 GB" or "5 GB" or "10 GB" or "Token listrik"))
                continue;

            detections[index] = new ShelfItemDetection(
                item.ProductName,
                Math.Clamp(targetX, 0, frame.Width - 1),
                calibration.ArtworkCenterY,
                item.Area,
                item.RawLabel.Contains("[anchor-centre-calibrated]", StringComparison.Ordinal)
                    ? item.RawLabel
                    : $"{item.RawLabel} [anchor-centre-calibrated]",
                item.Confidence);
        }
    }

    private static bool HasGulaPackageSignature(CapturedFrame frame, PixelRegion region)
    {
        var sampled = 0;
        var whitePixels = 0;
        var bluePixels = 0;

        for (var y = region.Y; y < region.Y + region.Height; y += 2)
        {
            for (var x = region.X; x < region.X + region.Width; x += 2)
            {
                var offset = y * frame.Stride + x * 4;
                var blue = frame.BgraPixels[offset];
                var green = frame.BgraPixels[offset + 1];
                var red = frame.BgraPixels[offset + 2];

                sampled++;

                var max = Math.Max(red, Math.Max(green, blue));
                var min = Math.Min(red, Math.Min(green, blue));
                if (red > 175 && green > 175 && blue > 165 && max - min < 75)
                    whitePixels++;

                if (blue > 85 && blue > red * 1.10d &&
                    blue > green * 0.75d && red < 190)
                    bluePixels++;
            }
        }

        if (sampled == 0)
            return false;

        var whiteRatio = (double)whitePixels / sampled;
        var blueRatio = (double)bluePixels / sampled;
        return whiteRatio >= 0.06d && blueRatio >= 0.035d;
    }

    private static bool HasCounterPackageColorSignature(
        CapturedFrame frame,
        PixelRegion region,
        string productName)
    {
        var sampled = 0;
        var matchingPixels = 0;

        for (var y = region.Y; y < region.Y + region.Height; y += 2)
        {
            for (var x = region.X; x < region.X + region.Width; x += 2)
            {
                var offset = y * frame.Stride + x * 4;
                var blue = frame.BgraPixels[offset];
                var green = frame.BgraPixels[offset + 1];
                var red = frame.BgraPixels[offset + 2];

                sampled++;

                var matches = productName switch
                {
                    "2 GB" => red > 155 && green > 95 && blue < 150 &&
                              red > blue * 1.20d && green > blue * 0.85d,
                    "5 GB" => blue > 115 && blue > red * 1.25d &&
                              blue > green * 0.95d && red < 145,
                    _ => false
                };

                if (matches)
                    matchingPixels++;
            }
        }

        return sampled > 0 && (double)matchingPixels / sampled >= 0.055d;
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

    private sealed record CounterSlot(string ProductName, double CenterX);

    private sealed record CounterCalibration(
        int WalletX,
        int TwoGbX,
        int FiveGbX,
        int TenGbX,
        int TokenX,
        int ArtworkCenterY);

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
