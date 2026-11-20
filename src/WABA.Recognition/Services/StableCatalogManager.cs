using System.Text.Json;
using WABA.Domain.Models;

namespace WABA.Recognition.Services;

/// <summary>
/// Keeps a stable 37-slot catalogue across transient OCR failures.
/// OCR results are observations; they do not replace the reference catalogue.
/// Coordinates are stored in the reference client-frame space and scaled to
/// the current captured frame for preview.
/// </summary>
public sealed class StableCatalogManager
{
    public const int ReferenceWidth = 1831;
    public const int ReferenceHeight = 1028;
    public const int ExpectedCatalogCount = 37;

    // Presentation/input offsets are applied after loading or recalibrating the
    // reference catalogue. Values are expressed in reference-frame pixels.
    // LEFT_SHELF is deliberately unchanged.
    public const int RightShelfOffsetXReference = 24;
    public const int PulseCounterOffsetYReference = 18;

    private const int SchemaVersion = 1;
    private const int ObservationWindow = 3;
    private const double AspectRatioTolerance = 0.015d;
    private const int MaxRecalibrationDeviationX = 48;
    private const int MaxRecalibrationDeviationY = 42;

    private readonly List<CatalogEntryState> _entries = CreateDefaultCatalog();
    private readonly string _statePath;
    private bool _isLocked = true;
    private int _recalibrationScanCount;

    public StableCatalogManager()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(appData, "WABA Desktop");
        _statePath = Path.Combine(directory, "stable-catalog.json");
        TryLoadPersistedCatalog();
    }

    public bool IsLocked => _isLocked;
    public int RecalibrationScanCount => _recalibrationScanCount;
    public bool CanLockCatalog => !_isLocked && _recalibrationScanCount >= ObservationWindow;

    public string GetInitialStatus()
        => _isLocked
            ? $"KATALOG TERKUNCI • {_entries.Count}/{ExpectedCatalogCount} slot referensi tersedia. " +
              $"Koreksi marker aktif: RIGHT_SHELF X+{RightShelfOffsetXReference}px; " +
              $"PULSE_COUNTER Y+{PulseCounterOffsetYReference}px. OCR hanya memperbarui status pengamatan."
            : $"KALIBRASI TERBUKA • lakukan hingga 3 pemindaian; koordinat hanya diperbarui jika konsisten pada minimal 2 dari 3 scan. " +
              $"Koreksi marker aktif: RIGHT_SHELF X+{RightShelfOffsetXReference}px; " +
              $"PULSE_COUNTER Y+{PulseCounterOffsetYReference}px.";

    public StableCatalogSnapshot Merge(
        ShelfDetectionResult scan,
        int frameWidth,
        int frameHeight)
    {
        if (frameWidth <= 0 || frameHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameWidth), "Ukuran frame harus lebih besar dari nol.");

        var currentAspect = (double)frameWidth / frameHeight;
        var referenceAspect = (double)ReferenceWidth / ReferenceHeight;
        var layoutMatches = Math.Abs(currentAspect / referenceAspect - 1d) <= AspectRatioTolerance;

        var byKey = scan.Items
            .GroupBy(item => MakeKey(item.Area, item.ProductName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.Confidence).First(),
                StringComparer.OrdinalIgnoreCase);

        var observedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var coordinatesChanged = false;

        foreach (var entry in _entries)
        {
            var key = MakeKey(entry.Area, entry.ProductName);
            byKey.TryGetValue(key, out var observation);
            entry.SeenInLatestScan = observation is not null;

            if (observation is not null)
            {
                observedKeys.Add(key);
                entry.LastObservedConfidence = observation.Confidence;
                entry.LastSeenUtc = DateTimeOffset.UtcNow;
            }

            if (!_isLocked && layoutMatches)
            {
                entry.RecentObservations.Enqueue(observation is null
                    ? null
                    : new PositionObservation(observation.ClientX, observation.ClientY));

                while (entry.RecentObservations.Count > ObservationWindow)
                    entry.RecentObservations.Dequeue();

                if (TryApplyConsensus(entry, frameWidth, frameHeight))
                    coordinatesChanged = true;
            }
        }

        if (!_isLocked && layoutMatches)
            _recalibrationScanCount++;

        if (coordinatesChanged)
            TrySavePersistedCatalog();

        var items = _entries
            .Select(entry =>
            {
                // Apply area-specific calibration in reference-frame space so
                // the correction scales consistently with the captured client
                // dimensions. Do not mutate persisted catalogue coordinates:
                // this keeps the user baseline and OCR recalibration independent
                // from the display/target offsets.
                var adjustedReferenceX = entry.ReferenceX +
                    (entry.Area.Equals("RIGHT_SHELF", StringComparison.OrdinalIgnoreCase)
                        ? RightShelfOffsetXReference
                        : 0);
                var adjustedReferenceY = entry.ReferenceY +
                    (entry.Area.Equals("PULSE_COUNTER", StringComparison.OrdinalIgnoreCase)
                        ? PulseCounterOffsetYReference
                        : 0);

                var x = (int)Math.Round(adjustedReferenceX * (double)frameWidth / ReferenceWidth);
                var y = (int)Math.Round(adjustedReferenceY * (double)frameHeight / ReferenceHeight);
                var seen = entry.SeenInLatestScan;
                var confidence = seen ? entry.LastObservedConfidence : entry.BaselineConfidence;
                var rawLabel = seen
                    ? "[LIVE OCR • stable catalogue reference]"
                    : "[BASELINE RETAINED • not seen in latest OCR scan]";

                return new ShelfItemDetection(
                    entry.ProductName,
                    Math.Clamp(x, 0, frameWidth - 1),
                    Math.Clamp(y, 0, frameHeight - 1),
                    entry.Area,
                    rawLabel,
                    confidence);
            })
            .OrderBy(item => item.Area, StringComparer.Ordinal)
            .ThenBy(item => item.ClientY)
            .ThenBy(item => item.ClientX)
            .ToArray();

        var status = BuildStatus(observedKeys.Count, layoutMatches, frameWidth, frameHeight);
        return new StableCatalogSnapshot(
            items,
            observedKeys,
            _isLocked,
            observedKeys.Count,
            _entries.Count,
            layoutMatches,
            status);
    }

    public string LockCatalog()
    {
        _isLocked = true;
        var saved = TrySavePersistedCatalog();
        return saved
            ? $"KATALOG TERKUNCI • {_entries.Count}/{ExpectedCatalogCount} slot tersimpan. OCR berikutnya tidak dapat menghapus atau menggeser koordinat."
            : $"KATALOG TERKUNCI untuk sesi ini • penyimpanan lokal gagal; baseline bawaan akan dipakai lagi saat aplikasi dibuka ulang.";
    }

    public string BeginRecalibration()
    {
        _isLocked = false;
        _recalibrationScanCount = 0;
        foreach (var entry in _entries)
            entry.RecentObservations.Clear();

        var saved = TrySavePersistedCatalog();
        return saved
            ? "KALIBRASI TERBUKA • lakukan 3 scan manual. Posisi hanya diperbarui jika minimal 2 dari 3 pengamatan konsisten dan masih dalam batas toleransi."
            : "KALIBRASI TERBUKA untuk sesi ini • penyimpanan lokal gagal.";
    }

    public static string MakeKey(string area, string productName)
        => $"{area.Trim().ToUpperInvariant()}|{productName.Trim().ToLowerInvariant()}";

    private bool TryApplyConsensus(CatalogEntryState entry, int frameWidth, int frameHeight)
    {
        var valid = entry.RecentObservations
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();

        if (valid.Length < 2)
            return false;

        var minX = valid.Min(item => item.X);
        var maxX = valid.Max(item => item.X);
        var minY = valid.Min(item => item.Y);
        var maxY = valid.Max(item => item.Y);

        // Reject unstable OCR coordinates instead of allowing a single bad
        // frame to move the saved product target.
        if (maxX - minX > 24 || maxY - minY > 22)
            return false;

        var medianX = Median(valid.Select(item => item.X));
        var medianY = Median(valid.Select(item => item.Y));
        var referenceX = (int)Math.Round(medianX * (double)ReferenceWidth / frameWidth);
        var referenceY = (int)Math.Round(medianY * (double)ReferenceHeight / frameHeight);

        if (Math.Abs(referenceX - entry.ReferenceX) > MaxRecalibrationDeviationX ||
            Math.Abs(referenceY - entry.ReferenceY) > MaxRecalibrationDeviationY)
            return false;

        if (referenceX == entry.ReferenceX && referenceY == entry.ReferenceY)
            return false;

        entry.ReferenceX = Math.Clamp(referenceX, 0, ReferenceWidth - 1);
        entry.ReferenceY = Math.Clamp(referenceY, 0, ReferenceHeight - 1);
        return true;
    }

    private string BuildStatus(int observedCount, bool layoutMatches, int frameWidth, int frameHeight)
    {
        var offsetStatus =
            $"Koreksi marker aktif (frame referensi {ReferenceWidth}×{ReferenceHeight}): " +
            $"RIGHT_SHELF X+{RightShelfOffsetXReference}px; " +
            $"PULSE_COUNTER Y+{PulseCounterOffsetYReference}px.";

        if (!layoutMatches)
        {
            return $"PERINGATAN LAYOUT • frame {frameWidth}×{frameHeight} memiliki rasio berbeda dari baseline " +
                   $"{ReferenceWidth}×{ReferenceHeight}. Katalog tetap dipertahankan dan koordinat hanya diskalakan untuk preview; " +
                   $"kalibrasi ulang posisi tidak diterapkan. {offsetStatus}";
        }

        var retained = _entries.Count - observedCount;
        if (_isLocked)
        {
            return $"KATALOG TERKUNCI • {_entries.Count}/{ExpectedCatalogCount} slot stabil. " +
                   $"Scan ini membaca {observedCount}; {retained} produk dipertahankan dari baseline. " +
                   $"Hasil OCR tidak menghapus produk atau mengubah koordinat. {offsetStatus}";
        }

        return $"KALIBRASI TERBUKA • scan ke-{_recalibrationScanCount}. " +
               $"{observedCount}/{_entries.Count} produk terlihat pada scan ini; " +
               $"koordinat diperbarui hanya setelah 2 dari 3 pengamatan konsisten. {offsetStatus}";
    }

    private bool TrySavePersistedCatalog()
    {
        try
        {
            var directory = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var document = new PersistedCatalog(
                SchemaVersion,
                ReferenceWidth,
                ReferenceHeight,
                _isLocked,
                _entries.Select(entry => new PersistedCatalogEntry(
                    entry.ProductName,
                    entry.Area,
                    entry.ReferenceX,
                    entry.ReferenceY,
                    entry.BaselineConfidence)).ToList());

            var json = JsonSerializer.Serialize(document, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var temporaryPath = _statePath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _statePath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void TryLoadPersistedCatalog()
    {
        try
        {
            if (!File.Exists(_statePath))
                return;

            var document = JsonSerializer.Deserialize<PersistedCatalog>(File.ReadAllText(_statePath));
            if (document is null ||
                document.SchemaVersion != SchemaVersion ||
                document.ReferenceWidth <= 0 ||
                document.ReferenceHeight <= 0 ||
                document.Entries.Count != _entries.Count)
                return;

            var saved = document.Entries.ToDictionary(
                item => MakeKey(item.Area, item.ProductName),
                StringComparer.OrdinalIgnoreCase);

            if (_entries.Any(entry => !saved.ContainsKey(MakeKey(entry.Area, entry.ProductName))))
                return;

            foreach (var entry in _entries)
            {
                var item = saved[MakeKey(entry.Area, entry.ProductName)];
                entry.ReferenceX = Math.Clamp(item.ClientX, 0, ReferenceWidth - 1);
                entry.ReferenceY = Math.Clamp(item.ClientY, 0, ReferenceHeight - 1);
            }

            _isLocked = document.IsLocked;
        }
        catch
        {
            // A malformed or older local file must never prevent the app from
            // starting. The built-in 37-product baseline remains authoritative.
        }
    }

    private static double Median(IEnumerable<int> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2d
            : ordered[middle];
    }

    private static List<CatalogEntryState> CreateDefaultCatalog() =>
    [
        new("Mie rebus", "LEFT_SHELF", 351, 316, 1.00d),
        new("Telur", "LEFT_SHELF", 494, 316, 1.00d),
        new("Mie goreng", "LEFT_SHELF", 206, 318, 1.00d),
        new("Kopi sachet", "LEFT_SHELF", 638, 318, 1.00d),
        new("Beras", "LEFT_SHELF", 206, 428, 1.00d),
        new("Buku tulis", "LEFT_SHELF", 351, 429, 1.00d),
        new("Makanan kucing", "LEFT_SHELF", 495, 429, 1.00d),
        new("Sayuran", "LEFT_SHELF", 639, 430, 1.00d),
        new("Obat", "LEFT_SHELF", 208, 541, 1.00d),
        new("Galon", "LEFT_SHELF", 350, 541, 1.00d),
        new("Gula", "LEFT_SHELF", 638, 541, 0.72d),
        new("LPG 3 kg", "LEFT_SHELF", 494, 543, 1.00d),
        new("Minyak goreng", "LEFT_SHELF", 351, 653, 1.00d),
        new("Sabun mandi", "LEFT_SHELF", 495, 653, 1.00d),
        new("Teh celup", "LEFT_SHELF", 206, 655, 0.89d),
        new("Deterjen", "LEFT_SHELF", 638, 655, 1.00d),

        new("E-Wallet", "PULSE_COUNTER", 268, 874, 1.00d),
        new("2 GB", "PULSE_COUNTER", 374, 874, 0.74d),
        new("5 GB", "PULSE_COUNTER", 480, 874, 0.94d),
        new("10 GB", "PULSE_COUNTER", 586, 874, 0.80d),
        new("Token listrik", "PULSE_COUNTER", 692, 874, 1.00d),

        new("Ciki", "RIGHT_SHELF", 1133, 316, 1.00d),
        new("Permen", "RIGHT_SHELF", 1278, 316, 1.00d),
        new("Biskuit", "RIGHT_SHELF", 1423, 316, 1.00d),
        new("Air mineral", "RIGHT_SHELF", 1566, 316, 1.00d),
        new("Roti", "RIGHT_SHELF", 1133, 428, 1.00d),
        new("Jamu", "RIGHT_SHELF", 1277, 428, 1.00d),
        new("Teh botol", "RIGHT_SHELF", 1422, 429, 0.89d),
        new("Kerupuk", "RIGHT_SHELF", 1566, 430, 1.00d),
        new("Minuman energi", "RIGHT_SHELF", 1422, 541, 1.00d),
        new("Soda", "RIGHT_SHELF", 1565, 541, 1.00d),
        new("Rengginang", "RIGHT_SHELF", 1134, 543, 1.00d),
        new("Kopi susu", "RIGHT_SHELF", 1278, 543, 1.00d),
        new("Susu kotak", "RIGHT_SHELF", 1134, 653, 1.00d),
        new("Es krim", "RIGHT_SHELF", 1277, 653, 1.00d),
        new("Kurma", "RIGHT_SHELF", 1422, 653, 1.00d),
        new("Sirup", "RIGHT_SHELF", 1566, 655, 1.00d)
    ];

    private sealed class CatalogEntryState(
        string productName,
        string area,
        int referenceX,
        int referenceY,
        double baselineConfidence)
    {
        public string ProductName { get; } = productName;
        public string Area { get; } = area;
        public int ReferenceX { get; set; } = referenceX;
        public int ReferenceY { get; set; } = referenceY;
        public double BaselineConfidence { get; } = baselineConfidence;
        public double LastObservedConfidence { get; set; } = baselineConfidence;
        public bool SeenInLatestScan { get; set; }
        public DateTimeOffset? LastSeenUtc { get; set; }
        public Queue<PositionObservation?> RecentObservations { get; } = new();
    }

    private sealed record PositionObservation(int X, int Y);

    public sealed record PersistedCatalog(
        int SchemaVersion,
        int ReferenceWidth,
        int ReferenceHeight,
        bool IsLocked,
        List<PersistedCatalogEntry> Entries);

    public sealed record PersistedCatalogEntry(
        string ProductName,
        string Area,
        int ClientX,
        int ClientY,
        double Confidence);
}

public sealed record StableCatalogSnapshot(
    IReadOnlyList<ShelfItemDetection> Items,
    IReadOnlySet<string> ObservedKeys,
    bool IsLocked,
    int ObservedCount,
    int TotalCount,
    bool LayoutMatches,
    string Status);
